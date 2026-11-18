using System.Collections.Concurrent;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace FortressIntelligenceRM.Web.Services;

public class CalendarAutoSyncService : IHostedService, IDisposable
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly CalendarService _calendarService;
    private readonly AutoJoinSchedulerService _autoJoinScheduler;
    private readonly MeetingService _meetingService;
    private readonly ILogger<CalendarAutoSyncService> _logger;
    private readonly IConfiguration _config;
    private Timer? _timer;

    // WI #8863: how far apart a row's start and a calendar occurrence's start may be and still be
    // matched on iCalUId (which recurring-series occurrences can share).
    private static readonly TimeSpan IcalUidMatchTolerance = TimeSpan.FromHours(2);

    // WI #9678: calendar reconcile (remove auto-added rows whose calendar event is gone).
    /// <summary>Consecutive Complete polls a row must be missing on before it is removed.</summary>
    internal const int ReconcileMissesRequired = 2;
    /// <summary>Rows starting sooner than this are never judged (the bot may be about to launch).</summary>
    internal static readonly TimeSpan ReconcileMinLeadTime = TimeSpan.FromMinutes(15);
    /// <summary>Rows starting within this of the polled window's end are never judged (edge of window).</summary>
    internal static readonly TimeSpan ReconcileWindowEdgeMargin = TimeSpan.FromHours(1);

    /// <summary>
    /// WI #9678 debounce: meeting id → (owner, consecutive Complete polls the row was missing on).
    /// In memory only: a restart resets every count, which only delays a removal (the safe
    /// direction). Cleared for a row when it is seen again or stops being a candidate; incomplete
    /// polls neither add nor clear.
    /// </summary>
    private readonly ConcurrentDictionary<long, (Guid UserId, int Misses)> _reconcileMisses = new();

    // WI #8863: rows the calendar must never re-anchor or reschedule onto another occurrence.
    private static bool IsTerminal(FirmMeeting m) =>
        m.Status is MeetingStatus.Complete or MeetingStatus.Failed;

    public CalendarAutoSyncService(
        IDbContextFactory<FirmDbContext> dbFactory,
        CalendarService calendarService,
        AutoJoinSchedulerService autoJoinScheduler,
        MeetingService meetingService,
        ILogger<CalendarAutoSyncService> logger,
        IConfiguration config)
    {
        _dbFactory = dbFactory;
        _calendarService = calendarService;
        _autoJoinScheduler = autoJoinScheduler;
        _meetingService = meetingService;
        _logger = logger;
        _config = config;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Dev and prod web share one database. If an instance that cannot create EventBridge
        // schedules (AutoJoinEnabled=false, e.g. dev) polled calendars, it would insert meetings
        // as Scheduled with no auto-join schedule, and those meetings never join (only a later
        // prod restart's startup backfill would repair them). Only run the sync where scheduling
        // is available.
        if (!_autoJoinScheduler.IsSchedulingAvailable)
        {
            _logger.LogWarning("[AutoSync] Calendar auto-sync NOT started: AutoJoin scheduling is unavailable on this instance (Firm:AutoJoinEnabled=false or Lambda/scheduler role ARN not configured). Meetings are only auto-added by an instance that can schedule their auto-join.");
            return Task.CompletedTask;
        }

        var intervalMinutes = _config.GetValue<int>("Firm:CalendarSyncIntervalMinutes", 15);
        _logger.LogInformation("[AutoSync] Service started. Poll interval: {Minutes}m", intervalMinutes);
        _timer = new Timer(PollAsync, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(intervalMinutes));

        // Issue 3: refresh any stale pre-5ce95fdc EventBridge schedules on every startup.
        // Fire-and-forget — a large backlog must not delay host startup; failures are
        // caught/logged per-meeting inside BackfillStaleSchedulesAsync.
        _ = RunStartupScheduleBackfillAsync();

        return Task.CompletedTask;
    }

    private async Task RunStartupScheduleBackfillAsync()
    {
        try
        {
            await _autoJoinScheduler.BackfillStaleSchedulesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSync] Unhandled error during startup AutoJoin schedule backfill");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async void PollAsync(object? state)
    {
        try
        {
            await PollCoreAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AutoSync] Unhandled error in poll cycle");
        }
    }

    private async Task PollCoreAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var users = await db.Users
            .Where(u => u.AutoAddCalendarMeetings && u.IsActive)
            .ToListAsync(ct);

        // WI #9678: kill switch, read every poll. Off also forgets the debounce counts, so turning it
        // back on starts every row from zero misses.
        var reconcileEnabled = _config.GetValue<bool>("Firm:CalendarReconcileEnabled", true);
        if (!reconcileEnabled)
        {
            _reconcileMisses.Clear();
        }
        else
        {
            // Forget counts for users who are no longer polled (auto-add turned off, deactivated).
            var polledUserIds = users.Select(u => u.Id).ToHashSet();
            foreach (var entry in _reconcileMisses)
            {
                if (!polledUserIds.Contains(entry.Value.UserId))
                    _reconcileMisses.TryRemove(entry.Key, out _);
            }
        }

        if (users.Count == 0) return;

        _logger.LogInformation("[AutoSync] Polling calendars for {Count} opted-in users.", users.Count);

        foreach (var user in users)
        {
            try
            {
                // WI #9678: one paged Graph read per user. The insert/reconcile loop below uses its
                // Meetings list exactly as before; Complete gates the removal step only.
                var poll = await _calendarService.GetUpcomingCalendarMeetingsPollAsync(user.EntraOid, user.Email, ct);
                var meetings = poll.Meetings;

                foreach (var dto in meetings)
                {
                    var startDatetime = DateTime.Parse(dto.StartDateTime);
                    FirmMeeting? matched = null;

                    // Step 1 — iCalUId match: the most stable Graph identifier, doesn't drift across
                    // polls the way the Graph `id` (CalendarEventId) can (Issue 5b).
                    // WI #8863: every occurrence of an externally-organized (cross-tenant/iMIP) recurring
                    // series shares one iCalUId, so the match is bounded to rows starting within
                    // IcalUidMatchTolerance of this occurrence — otherwise a sibling occurrence's row is
                    // hijacked (its start overwritten) and this occurrence never gets its own row. A
                    // genuine single-meeting reschedule beyond the tolerance still matches in Step 2.
                    if (!string.IsNullOrEmpty(dto.ICalUId))
                    {
                        var icalCandidates = await db.Meetings
                            .Where(m => m.GraphMeetingId == dto.ICalUId && m.CreatedBy == user.Id)
                            .ToListAsync(ct);
                        matched = icalCandidates
                            .Where(m => m.StartDatetime == null
                                     || (m.StartDatetime.Value - startDatetime).Duration() < IcalUidMatchTolerance)
                            .OrderBy(m => m.StartDatetime == null ? TimeSpan.MaxValue : (m.StartDatetime.Value - startDatetime).Duration())
                            .FirstOrDefault();
                        if (matched != null && IsTerminal(matched))
                        {
                            if (matched.StartDatetime == startDatetime) continue;
                            matched = null;
                        }
                        if (matched != null)
                        {
                            var changed = false;
                            if (matched.CalendarEventId != dto.CalendarEventId)
                            {
                                // Self-repair: Graph's `id` drifted but the iCalUId anchor still matched.
                                matched.CalendarEventId = dto.CalendarEventId;
                                changed = true;
                                _logger.LogInformation(
                                    "[AutoSync] Self-repaired CalendarEventId on meeting {Id} via iCalUId match (Graph ID drift)",
                                    matched.Id);
                            }
                            await ReconcileMatchedMeetingAsync(db, matched, startDatetime, changed, ct);
                            continue;
                        }
                    }

                    // Step 2 — exact CalendarEventId match (fast path when the Graph ID is stable).
                    matched = await db.Meetings.FirstOrDefaultAsync(
                        m => m.CalendarEventId == dto.CalendarEventId && m.CreatedBy == user.Id, ct);
                    if (matched != null && IsTerminal(matched))
                    {
                        // WI #8863: a finished row is only ever "this occurrence" at its own start —
                        // never re-anchor or reschedule it onto a different one.
                        if (matched.StartDatetime == startDatetime) continue;
                        _logger.LogWarning(
                            "[AutoSync] Terminal meeting {Id} ({Status}) holds calendar event {CalendarEventId} but starts {Old}, not {New} — not reconciling",
                            matched.Id, matched.Status, dto.CalendarEventId, matched.StartDatetime, startDatetime);
                        matched = null;
                    }
                    if (matched != null)
                    {
                        var changed = false;
                        if (!string.IsNullOrEmpty(dto.ICalUId) && matched.GraphMeetingId != dto.ICalUId)
                        {
                            matched.GraphMeetingId = dto.ICalUId;
                            changed = true;
                        }
                        await ReconcileMatchedMeetingAsync(db, matched, startDatetime, changed, ct);
                        continue;
                    }

                    // Step 3 — normalized URL + start time (guards against Graph ID instability when
                    // both anchors above miss, e.g. a recurring occurrence returning a fresh id and
                    // iCalUId hasn't been backfilled onto the row yet). NormalizeMeetingUrl doesn't
                    // reliably translate to SQL via EF, so pull the StartDatetime+user candidate set
                    // first (both indexable) and normalize/filter in memory.
                    var normalizedDtoUrl = CalendarService.NormalizeMeetingUrl(dto.JoinUrl);
                    var startCandidates = await db.Meetings
                        .Where(m => m.StartDatetime == startDatetime && m.CreatedBy == user.Id)
                        .ToListAsync(ct);
                    matched = startCandidates.FirstOrDefault(
                        m => CalendarService.NormalizeMeetingUrl(m.MeetingUrl) == normalizedDtoUrl);
                    // WI #8863: same start, so a terminal match is this occurrence — leave its anchors alone.
                    if (matched != null && IsTerminal(matched)) continue;
                    if (matched != null)
                    {
                        var changed = false;
                        if (matched.CalendarEventId != dto.CalendarEventId)
                        {
                            matched.CalendarEventId = dto.CalendarEventId;
                            changed = true;
                        }
                        if (!string.IsNullOrEmpty(dto.ICalUId) && matched.GraphMeetingId != dto.ICalUId)
                        {
                            matched.GraphMeetingId = dto.ICalUId;
                            changed = true;
                        }
                        if (changed)
                        {
                            _logger.LogInformation(
                                "[AutoSync] Updated CalendarEventId/GraphMeetingId on meeting {Id} (Graph ID drift on recurring occurrence)",
                                matched.Id);
                        }
                        await ReconcileMatchedMeetingAsync(db, matched, startDatetime, changed, ct);
                        continue;
                    }

                    // WI #8577: same-user dedup, checked before the cross-user lookup below. The
                    // anchors above only match rows that carry this event's identifiers at the exact
                    // calendar start; a meeting the user added manually (no calendar_event_id, null or
                    // join-time start) slips past them, and AutoSync would add a second record.
                    if (await HasActiveSameUserMeetingAsync(db, user.Id, dto, normalizedDtoUrl, startDatetime, ct))
                    {
                        _logger.LogDebug(
                            "[AutoSync] Skipping CalendarAutoSync record creation for user {UserId}, calendar event {CalendarEventId} — existing non-failed record found",
                            user.Id, dto.CalendarEventId);
                        continue;
                    }

                    // All per-user anchors missed — genuinely new meeting for this user.
                    // WI #7033: before creating it, check whether ANY user already owns the bot
                    // slot for this same real-world meeting (same normalized URL, overlapping
                    // start time window). If so, this becomes a subscriber instead of a primary.
                    var meeting = new FirmMeeting
                    {
                        Platform = dto.Platform,
                        MeetingUrl = dto.JoinUrl,
                        CalendarEventId = dto.CalendarEventId,
                        GraphMeetingId = string.IsNullOrEmpty(dto.ICalUId) ? null : dto.ICalUId,
                        Title = dto.Subject,
                        StartDatetime = startDatetime,
                        CreatedBy = user.Id,
                        CreatorEntraOid = user.EntraOid,
                        Source = "autoadd",
                        Mode = dto.Mode,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    var existingPrimary = await FindExistingPrimaryAsync(db, normalizedDtoUrl, startDatetime, ct);
                    if (existingPrimary != null)
                    {
                        meeting.IsPrimaryRecorder = false;
                        meeting.PrimaryMeetingId = existingPrimary.Id;
                        await InsertScheduledMeetingAsync(db, meeting, MeetingStatus.Waiting, ct);
                        _logger.LogInformation(
                            "[AutoSync] Added meeting {Id} as SUBSCRIBER of primary {PrimaryId} for user {UserId} (calendar {CalendarEventId})",
                            meeting.Id, existingPrimary.Id, user.Id, dto.CalendarEventId);
                        await FanOutIfPrimaryCompleteAsync(existingPrimary, ct);
                        continue;
                    }

                    meeting.IsPrimaryRecorder = true;
                    meeting.NormalizedMeetingUrl = normalizedDtoUrl;
                    try
                    {
                        await InsertScheduledMeetingAsync(db, meeting, MeetingStatus.Scheduled, ct);
                    }
                    catch (DbUpdateException dbEx) when (IsDuplicateKeyException(dbEx))
                    {
                        // Race: another user's poll cycle (or the same user across two overlapping
                        // ticks) won the primary slot for this normalized URL + start time between
                        // our lookup and our INSERT. `db`'s change tracker still holds the failed
                        // Added entity, so retry on a fresh DbContext rather than reusing `db`.
                        _logger.LogInformation(
                            "[AutoSync] Primary insert raced for normalized URL {Url} @ {Start} — retrying as subscriber",
                            normalizedDtoUrl, startDatetime);
                        await using var raceDb = await _dbFactory.CreateDbContextAsync(ct);
                        var racedPrimary = await FindExistingPrimaryAsync(raceDb, normalizedDtoUrl, startDatetime, ct);
                        if (racedPrimary == null)
                        {
                            _logger.LogWarning(
                                "[AutoSync] Could not resolve winning primary after duplicate-key race for {Url} @ {Start} — leaving meeting unscheduled",
                                normalizedDtoUrl, startDatetime);
                            continue;
                        }
                        meeting.IsPrimaryRecorder = false;
                        meeting.PrimaryMeetingId = racedPrimary.Id;
                        meeting.NormalizedMeetingUrl = null;
                        await InsertScheduledMeetingAsync(raceDb, meeting, MeetingStatus.Waiting, ct);
                        _logger.LogInformation(
                            "[AutoSync] Added meeting {Id} as SUBSCRIBER of primary {PrimaryId} for user {UserId} after race",
                            meeting.Id, racedPrimary.Id, user.Id);
                        await FanOutIfPrimaryCompleteAsync(racedPrimary, ct);
                        continue;
                    }

                    // WI #7848: don't auto-launch a bot into a meeting that's already under way (e.g.
                    // auto-add enabled mid-meeting). Leave it Scheduled so the user can Join Now.
                    if (startDatetime > DateTime.UtcNow)
                    {
                        await _autoJoinScheduler.CreateScheduleAsync(meeting.Id, dto.JoinUrl, startDatetime);

                        _logger.LogInformation("[AutoSync] Added meeting {Id} as PRIMARY from calendar {CalendarEventId} for user {UserId}",
                            meeting.Id, dto.CalendarEventId, user.Id);
                    }
                    else
                    {
                        _logger.LogInformation("[AutoSync] Meeting {Id} start time {Start} is already past — skipping auto-launch, leaving in Scheduled status for user {UserId} (calendar {CalendarEventId})",
                            meeting.Id, startDatetime, user.Id, dto.CalendarEventId);
                    }
                }

                // WI #9678: after the insert/reconcile loop, remove this user's auto-added rows whose
                // calendar event is gone. Has its own try/catch: it can never break the poll.
                if (reconcileEnabled)
                    await ReconcileRemovedCalendarEventsAsync(user.Id, poll, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AutoSync] Failed to sync calendar for user {UserId}", user.Id);
            }
        }
    }

    /// <summary>
    /// WI #9678: removes the user's auto-added meetings whose calendar event has been deleted or
    /// declined, together with their EventBridge auto-join schedule, so the bot never launches into
    /// a cancelled meeting. Acts only on a Complete poll (an empty or truncated read proves nothing)
    /// and only after the row was missing on <see cref="ReconcileMissesRequired"/> consecutive
    /// Complete polls. The decision itself is <see cref="PlanCalendarRemovals"/> (pure); this method
    /// only loads rows, applies the debounce counts and performs the removals. Any failure is logged
    /// (exception type only) and swallowed so the poll carries on with the next user.
    /// </summary>
    private async Task ReconcileRemovedCalendarEventsAsync(Guid userId, CalendarPollResult poll, CancellationToken ct)
    {
        try
        {
            if (!poll.Complete)
            {
                _logger.LogInformation("[AutoSync] Calendar reconcile skipped for user {UserId}: calendar read was not complete", userId);
                return;
            }

            var now = DateTime.UtcNow;
            var earliestStart = now + ReconcileMinLeadTime;
            var latestStart = poll.WindowEndUtc - ReconcileWindowEdgeMargin;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            // SQL pre-filter only; PlanCalendarRemovals re-applies every condition in memory.
            var rows = await db.Meetings.AsNoTracking()
                .Where(m => m.CreatedBy == userId
                         && m.Source == "autoadd"
                         && m.CalendarEventId != null && m.CalendarEventId != ""
                         && (m.Status == MeetingStatus.Scheduled || m.Status == MeetingStatus.Waiting)
                         && m.StartedAt == null
                         && m.StartDatetime != null
                         && m.StartDatetime > earliestStart
                         && m.StartDatetime < latestStart)
                .ToListAsync(ct);

            var primaryIds = rows.Where(m => m.IsPrimaryRecorder).Select(m => m.Id).ToList();
            var primariesWithSubscribers = primaryIds.Count == 0
                ? new HashSet<long>()
                : (await db.Meetings.AsNoTracking()
                    .Where(s => s.PrimaryMeetingId != null && primaryIds.Contains(s.PrimaryMeetingId.Value))
                    .Select(s => s.PrimaryMeetingId!.Value)
                    .Distinct()
                    .ToListAsync(ct)).ToHashSet();

            var priorMisses = _reconcileMisses
                .Where(kv => kv.Value.UserId == userId)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Misses);

            var plan = PlanCalendarRemovals(userId, rows, primariesWithSubscribers,
                CalendarAnchors.From(poll.Meetings), priorMisses, now, poll.WindowEndUtc);

            // Apply the debounce: rows seen again (or no longer candidates) lose their count.
            foreach (var id in priorMisses.Keys)
            {
                if (!plan.MissCounts.ContainsKey(id))
                    _reconcileMisses.TryRemove(id, out _);
            }
            foreach (var (id, misses) in plan.MissCounts)
                _reconcileMisses[id] = (userId, misses);

            foreach (var id in plan.SkippedPrimaries)
            {
                _logger.LogInformation(
                    "[AutoSync] Not removing meeting {Id} for user {UserId}: calendar event no longer present, but it is the primary recorder for other users' subscriber meetings",
                    id, userId);
            }

            foreach (var id in plan.ToRemove)
            {
                try
                {
                    // Same path as the manual Remove button: RemoveMeetingAsync (refuses in-progress
                    // statuses), then the EventBridge schedule.
                    var (removed, error) = await _meetingService.RemoveMeetingAsync(id, userId);
                    if (!removed)
                    {
                        _logger.LogWarning(
                            "[AutoSync] Calendar reconcile did not remove meeting {Id} for user {UserId}: {Error}",
                            id, userId, error);
                        continue;
                    }
                    _reconcileMisses.TryRemove(id, out _);
                    await _autoJoinScheduler.DeleteScheduleAsync(id);
                    _logger.LogInformation(
                        "[AutoSync] Removed meeting {Id} (calendar event no longer present, seen missing on {Misses} polls) for user {UserId}",
                        id, ReconcileMissesRequired, userId);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "[AutoSync] Calendar reconcile failed to remove meeting {Id} for user {UserId} ({ExceptionType})",
                        id, userId, ex.GetType().Name);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "[AutoSync] Calendar reconcile failed for user {UserId} ({ExceptionType}) — poll continues",
                userId, ex.GetType().Name);
        }
    }

    /// <summary>WI #9678: the outcome of one user's reconcile decision.</summary>
    /// <param name="MissCounts">New consecutive-miss count for every candidate row that is gone on this
    /// poll. Rows absent from this map have their count cleared.</param>
    /// <param name="ToRemove">Rows to delete now (missing on enough consecutive Complete polls).</param>
    /// <param name="SkippedPrimaries">Rows that qualify but are primaries with subscribers — never auto-removed.</param>
    internal sealed record CalendarRemovalPlan(
        IReadOnlyDictionary<long, int> MissCounts,
        IReadOnlyList<long> ToRemove,
        IReadOnlyList<long> SkippedPrimaries);

    /// <summary>
    /// WI #9678: may this row be judged by the calendar reconcile at all? Only the user's own
    /// auto-added rows (never manual Join Now rows or any other source) that carry a Graph event id,
    /// are still waiting to run (Scheduled or Waiting, never started, no bot run recorded), and start
    /// more than <see cref="ReconcileMinLeadTime"/> from now and at least
    /// <see cref="ReconcileWindowEdgeMargin"/> before the end of the polled window.
    /// </summary>
    internal static bool IsReconcileCandidate(FirmMeeting m, Guid userId, DateTime nowUtc, DateTime windowEndUtc) =>
        m.CreatedBy == userId
        && m.Source == "autoadd"
        && !string.IsNullOrEmpty(m.CalendarEventId)
        && m.Status is MeetingStatus.Scheduled or MeetingStatus.Waiting
        && m.StartedAt == null
        && string.IsNullOrEmpty(m.BotTaskArn)
        && string.IsNullOrEmpty(m.LastFailureReason)
        && m.StartDatetime is DateTime start
        && start > nowUtc + ReconcileMinLeadTime
        && start < windowEndUtc - ReconcileWindowEdgeMargin;

    /// <summary>
    /// WI #9678: pure decision over (rows, current calendar anchors, prior miss counts, now). The
    /// caller must only call it for a Complete poll. A candidate row that is gone by all three
    /// anchors (<see cref="CalendarService.IsGoneFromCalendar"/>, the same rule as the Meetings page
    /// chip) gets its miss count incremented; at <see cref="ReconcileMissesRequired"/> it is removed,
    /// unless it is a primary that other rows point to.
    /// </summary>
    internal static CalendarRemovalPlan PlanCalendarRemovals(
        Guid userId,
        IEnumerable<FirmMeeting> rows,
        IReadOnlySet<long> primariesWithSubscribers,
        CalendarAnchors current,
        IReadOnlyDictionary<long, int> priorMisses,
        DateTime nowUtc,
        DateTime windowEndUtc)
    {
        var missCounts = new Dictionary<long, int>();
        var toRemove = new List<long>();
        var skippedPrimaries = new List<long>();

        foreach (var m in rows)
        {
            if (!IsReconcileCandidate(m, userId, nowUtc, windowEndUtc)) continue;
            if (!CalendarService.IsGoneFromCalendar(m, current)) continue;

            var misses = (priorMisses.TryGetValue(m.Id, out var prior) ? prior : 0) + 1;
            missCounts[m.Id] = misses;
            if (misses < ReconcileMissesRequired) continue;

            if (m.IsPrimaryRecorder && primariesWithSubscribers.Contains(m.Id))
            {
                skippedPrimaries.Add(m.Id);
                continue;
            }
            toRemove.Add(m.Id);
        }

        return new CalendarRemovalPlan(missCounts, toRemove, skippedPrimaries);
    }

    /// <summary>
    /// Inserts a new calendar-sourced meeting and lands it in the given target status (Scheduled for
    /// a primary, Waiting for a subscriber — WI #7033) with a single INSERT.
    ///
    /// NOTE: MeetingStatus.Scheduled == 0 (the CLR default). FirmDbContext configures
    /// .HasSentinel(MeetingStatus.Joining) (ADO#17), so EF Core includes Status=Scheduled in the
    /// INSERT rather than letting the DB default (Joining) win. The former Joining→target two-step
    /// workaround is no longer needed (verified in production, WI #7784).
    /// </summary>
    /// <summary>
    /// WI #8278: FindExistingPrimaryAsync can match a primary that has already finished (e.g. a
    /// user's calendar syncs after the meeting ended). Its completion fan-out has already run, so
    /// without this the new Waiting subscriber would never receive the recording.
    /// </summary>
    private async Task FanOutIfPrimaryCompleteAsync(FirmMeeting primary, CancellationToken ct)
    {
        if (primary.Status != MeetingStatus.Complete) return;
        try
        {
            await _meetingService.FanOutToSubscribersAsync(primary.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "[AutoSync] Fan-out to late subscriber(s) of completed primary {PrimaryId} failed", primary.Id);
        }
    }

    private async Task<FirmMeeting> InsertScheduledMeetingAsync(FirmDbContext db, FirmMeeting draft, MeetingStatus targetStatus, CancellationToken ct)
    {
        draft.Status = targetStatus;
        db.Meetings.Add(draft);
        await db.SaveChangesAsync(ct);
        return draft;
    }

    /// <summary>
    /// WI #7033 dedup lookup: does any user already have a PRIMARY firm_meetings record for this
    /// same real-world meeting? Matched on normalized_meeting_url + start time within ±15 minutes,
    /// excluding Failed (a failed primary shouldn't block a fresh attempt by another user); falls
    /// back to a URL-only match against active null-start primaries (WI #7847).
    /// </summary>
    /// <summary>
    /// Shared tail for the three match paths. WI #7906: a meeting created via the UI (source=teams)
    /// before AutoSync first saw it is matched here but was never promoted to autoadd, so the
    /// AutoJoin backfill skipped it and no EventBridge schedule was ever created. Promote it, save
    /// any pending changes, and (re)create the schedule — CreateScheduleAsync upserts, so this is
    /// safe for meetings that already have one.
    /// WI #7907: also picks up reschedules — if the calendar event's start moved, update the row
    /// and re-point the EventBridge schedule so the bot doesn't fire at the old time.
    /// </summary>
    private async Task ReconcileMatchedMeetingAsync(FirmDbContext db, FirmMeeting matched, DateTime startDatetime, bool changed, CancellationToken ct)
    {
        var rescheduled = false;
        if (matched.StartDatetime != startDatetime)
        {
            _logger.LogInformation(
                "[AutoSync] Rescheduled: updated start_datetime for meeting {Id} from {Old} to {New}",
                matched.Id, matched.StartDatetime, startDatetime);
            matched.StartDatetime = startDatetime;
            changed = true;
            rescheduled = true;
        }

        if (matched.Source != "autoadd")
        {
            _logger.LogInformation(
                "[AutoSync] Promoted meeting {Id} source from {OldSource} to autoadd (calendar match)",
                matched.Id, matched.Source);
            matched.Source = "autoadd";
            changed = true;
        }

        if (!changed) return;

        matched.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (matched.Status != MeetingStatus.Scheduled) return;

        if (startDatetime > DateTime.UtcNow)
            await _autoJoinScheduler.CreateScheduleAsync(matched.Id, matched.MeetingUrl ?? "", startDatetime);
        else if (rescheduled)
            // Moved to a time that's already past — drop the stale schedule so it can't fire at the
            // old time (mirrors the WI #7848 rule: no auto-launch into a meeting already under way).
            await _autoJoinScheduler.DeleteScheduleAsync(matched.Id);
    }

    /// <summary>
    /// WI #8577: does this user already have a non-Failed firm_meetings record for this calendar
    /// event? Matched on calendar_event_id first (WI #8863: ignoring Complete rows too, which may
    /// hold a stolen calendar_event_id from a later occurrence), then on normalized meeting URL for rows whose
    /// start (or created_at, for manually-added rows with no start) falls between 15 minutes before
    /// the event and its end. The time bound keeps a recurring meeting's earlier occurrences (same
    /// join URL) from blocking later ones. Failed rows are ignored so they never block a fresh attempt.
    /// </summary>
    private static async Task<bool> HasActiveSameUserMeetingAsync(FirmDbContext db, Guid userId, CalendarMeetingDto dto, string normalizedUrl, DateTime startDatetime, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(dto.CalendarEventId)
            && await db.Meetings.AnyAsync(m => m.CreatedBy == userId
                                            && m.CalendarEventId == dto.CalendarEventId
                                            && m.Status != MeetingStatus.Failed
                                            && m.Status != MeetingStatus.Complete, ct))
            return true;

        if (string.IsNullOrEmpty(normalizedUrl)) return false;

        var windowStart = startDatetime.AddMinutes(-15);
        var windowEnd = DateTime.TryParse(dto.EndDateTime, out var end) && end > startDatetime.AddMinutes(15)
            ? end
            : startDatetime.AddMinutes(15);

        // NormalizeMeetingUrl doesn't translate to SQL, so narrow by user/status/time first and
        // normalize in memory (same approach as the Step 3 match).
        var candidates = await db.Meetings
            .Where(m => m.CreatedBy == userId
                     && m.Status != MeetingStatus.Failed
                     && (m.StartDatetime ?? m.CreatedAt) >= windowStart
                     && (m.StartDatetime ?? m.CreatedAt) <= windowEnd)
            .ToListAsync(ct);

        return candidates.Any(m => CalendarService.NormalizeMeetingUrl(m.MeetingUrl) == normalizedUrl);
    }

    internal static async Task<FirmMeeting?> FindExistingPrimaryAsync(FirmDbContext db, string normalizedUrl, DateTime startDatetime, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(normalizedUrl)) return null;

        var windowStart = startDatetime.AddMinutes(-15);
        var windowEnd = startDatetime.AddMinutes(15);

        var candidates = await db.Meetings
            .Where(m => m.IsPrimaryRecorder
                     && m.StartDatetime != null
                     && m.StartDatetime >= windowStart
                     && m.StartDatetime <= windowEnd
                     && m.Status != MeetingStatus.Failed)
            .ToListAsync(ct);

        var windowed = candidates.FirstOrDefault(m => m.NormalizedMeetingUrl == normalizedUrl);
        if (windowed != null) return windowed;

        // WI #7847: an active primary with a null StartDatetime (e.g. created for an already
        // in-progress meeting) is invisible to the time-window check above, which let a second
        // primary — and a second bot — be created. Fall back to a URL-only match on those rows.
        // Scoped to null-start rows so recurring meetings (same join URL every occurrence) still
        // get one primary per occurrence.
        return await db.Meetings.FirstOrDefaultAsync(m =>
            m.IsPrimaryRecorder
            && m.StartDatetime == null
            && m.NormalizedMeetingUrl == normalizedUrl
            && m.Status != MeetingStatus.Failed
            && m.Status != MeetingStatus.Complete, ct);
    }

    /// <summary>MySQL error 1062 (duplicate entry) surfaced through EF Core as a DbUpdateException —
    /// used to detect the uk_fm_normalized_url_start race described in FindExistingPrimaryAsync's caller.</summary>
    internal static bool IsDuplicateKeyException(DbUpdateException ex) =>
        ex.InnerException is MySqlException { Number: 1062 };
}
