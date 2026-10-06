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

        if (users.Count == 0) return;

        _logger.LogInformation("[AutoSync] Polling calendars for {Count} opted-in users.", users.Count);

        foreach (var user in users)
        {
            try
            {
                var meetings = await _calendarService.GetUpcomingCalendarMeetingsAsync(user.EntraOid, user.Email, ct);

                foreach (var dto in meetings)
                {
                    var startDatetime = DateTime.Parse(dto.StartDateTime);
                    FirmMeeting? matched = null;

                    // Step 1 — iCalUId match: the most stable Graph identifier, doesn't drift across
                    // polls the way the Graph `id` (CalendarEventId) can (Issue 5b).
                    if (!string.IsNullOrEmpty(dto.ICalUId))
                    {
                        matched = await db.Meetings.FirstOrDefaultAsync(
                            m => m.GraphMeetingId == dto.ICalUId && m.CreatedBy == user.Id, ct);
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
                            await ReconcileMatchedMeetingAsync(db, matched, changed, ct);
                            continue;
                        }
                    }

                    // Step 2 — exact CalendarEventId match (fast path when the Graph ID is stable).
                    matched = await db.Meetings.FirstOrDefaultAsync(
                        m => m.CalendarEventId == dto.CalendarEventId && m.CreatedBy == user.Id, ct);
                    if (matched != null)
                    {
                        var changed = false;
                        if (!string.IsNullOrEmpty(dto.ICalUId) && matched.GraphMeetingId != dto.ICalUId)
                        {
                            matched.GraphMeetingId = dto.ICalUId;
                            changed = true;
                        }
                        await ReconcileMatchedMeetingAsync(db, matched, changed, ct);
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
                        await ReconcileMatchedMeetingAsync(db, matched, changed, ct);
                        continue;
                    }

                    // All three per-user anchors missed — genuinely new meeting for this user.
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
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AutoSync] Failed to sync calendar for user {UserId}", user.Id);
            }
        }
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
    /// </summary>
    private async Task ReconcileMatchedMeetingAsync(FirmDbContext db, FirmMeeting matched, bool changed, CancellationToken ct)
    {
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

        if (matched.Status == MeetingStatus.Scheduled && matched.StartDatetime > DateTime.UtcNow)
            await _autoJoinScheduler.CreateScheduleAsync(matched.Id, matched.MeetingUrl ?? "", matched.StartDatetime!.Value);
    }

    private static async Task<FirmMeeting?> FindExistingPrimaryAsync(FirmDbContext db, string normalizedUrl, DateTime startDatetime, CancellationToken ct)
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
    private static bool IsDuplicateKeyException(DbUpdateException ex) =>
        ex.InnerException is MySqlException { Number: 1062 };
}
