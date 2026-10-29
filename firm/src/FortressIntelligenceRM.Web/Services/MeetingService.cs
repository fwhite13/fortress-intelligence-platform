using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Services;

public class MeetingService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<MeetingService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBatchTranscriptionService _batchService;
    private readonly AutoJoinSchedulerService _autoJoinScheduler;

    public MeetingService(IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config, ILogger<MeetingService> logger, IHttpClientFactory httpClientFactory, IBatchTranscriptionService batchService, AutoJoinSchedulerService autoJoinScheduler)
    {
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _batchService = batchService;
        _autoJoinScheduler = autoJoinScheduler;
    }

    public async Task<List<FirmMeeting>> GetMeetingsAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meetings = await db.Meetings
            .Where(m => m.CreatedBy == userId)
            .OrderByDescending(m => m.StartDatetime ?? m.CreatedAt)
            .ToListAsync();

        // WI #8289: flag primaries that have at least one subscriber row (drives "Scheduled - Primary" label).
        var primaryIds = meetings.Where(m => m.IsPrimaryRecorder).Select(m => m.Id).ToList();
        if (primaryIds.Count > 0)
        {
            var withSubscribers = (await db.Meetings
                .Where(s => s.PrimaryMeetingId != null && primaryIds.Contains(s.PrimaryMeetingId.Value))
                .Select(s => s.PrimaryMeetingId!.Value)
                .Distinct()
                .ToListAsync()).ToHashSet();
            foreach (var m in meetings)
                m.HasSubscribers = withSubscribers.Contains(m.Id);
        }

        return meetings;
    }

    /// <summary>
    /// Resolve the display name of a meeting's owning (creating) user, for the
    /// bot's post-join chat notification (BOT_NAMES_CSV — WI #7034).
    /// Single-user only for now; multi-user/subscriber aggregation via
    /// primary_meeting_id lands with the WI #7033 dedup work.
    /// </summary>
    public async Task<string?> GetMeetingOwnerNameAsync(long meetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.Include(m => m.CreatedByUser).FirstOrDefaultAsync(m => m.Id == meetingId);
        return meeting?.CreatedByUser?.DisplayName;
    }

    public async Task<FirmMeeting?> GetMeetingAsync(long id, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == id && m.CreatedBy == userId);
        if (meeting == null) return null;

        // WI #8289: drives "Scheduled - Primary" label only when a subscriber actually exists.
        meeting.HasSubscribers = meeting.IsPrimaryRecorder
            && await db.Meetings.AnyAsync(s => s.PrimaryMeetingId == meeting.Id);

        // WI #7033/#7035: a subscriber's own Participants/Transcripts/Summary rows are never
        // duplicated on fan-out — only the meeting-row fields are copied onto the subscriber row
        // (see FanOutToSubscribersAsync). Resolve the effective source-of-truth meeting id for these
        // relational artifacts here so a subscriber's detail page renders the shared content.
        var artifactMeetingId = meeting.PrimaryMeetingId ?? meeting.Id;
        meeting.Participants = await db.Participants.Where(p => p.MeetingId == artifactMeetingId).ToListAsync();
        // #8345: firm_meeting_participants is not yet populated by the vpbot callback;
        // fall back to roster_timeline JSON (the existing attendee store) when the table is empty.
        if (!meeting.Participants.Any() && !string.IsNullOrWhiteSpace(meeting.RosterTimeline))
        {
            try
            {
                var entries = JsonSerializer.Deserialize<List<JsonElement>>(meeting.RosterTimeline) ?? new();
                meeting.Participants = entries
                    .Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString()?.Trim() : null)
                    .Where(name => !string.IsNullOrWhiteSpace(name) && !IsRosterBotName(name!))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => new FirmMeetingParticipant { MeetingId = artifactMeetingId, DisplayName = name! })
                    .ToList();
            }
            catch (Exception ex)
            {
                // Non-fatal — correction dialog will just have fewer name suggestions
                _logger.LogWarning(ex, "FIRM: Failed to parse roster_timeline for meeting {MeetingId}", meeting.Id);
            }
        }
        // WI #8335: ThenBy(Id) makes the order deterministic — speaker-correction segment_index is a
        // position in this list, and transcribe.py reproduces it with a stable sort on startTimeMs.
        meeting.Transcripts = await db.Transcripts.Where(t => t.MeetingId == artifactMeetingId).OrderBy(t => t.StartTimeMs).ThenBy(t => t.Id).ToListAsync();
        meeting.Summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == artifactMeetingId);
        if (meeting.PrimaryMeetingId != null)
        {
            var primaryVersion = await db.Meetings
                .Where(m => m.Id == artifactMeetingId)
                .Select(m => (int?)m.SummaryVersion)
                .FirstOrDefaultAsync();
            if (primaryVersion != null) meeting.SummaryVersion = primaryVersion.Value;
            // WI #8335: corrections overlay the shared transcript, so they live on the primary row too.
            meeting.SpeakerCorrections = await db.Meetings
                .Where(m => m.Id == artifactMeetingId)
                .Select(m => m.SpeakerCorrections)
                .FirstOrDefaultAsync();
        }

        return meeting;
    }

    /// <summary>
    /// WI #8345: conservative bot filter for roster_timeline names — exact matches for the short
    /// notetaker names ("RN", "FIRM") so human names containing those letters aren't dropped,
    /// plus clear notetaker/bot markers ("Fireflies.ai Notetaker Jamal", "Fortress Notetaker", "... Bot").
    /// </summary>
    private static bool IsRosterBotName(string name) =>
        name.Equals("RN", StringComparison.OrdinalIgnoreCase)
        || name.Equals("FIRM", StringComparison.OrdinalIgnoreCase)
        || name.Contains("notetaker", StringComparison.OrdinalIgnoreCase)
        || name.Contains("fireflies", StringComparison.OrdinalIgnoreCase)
        || System.Text.RegularExpressions.Regex.IsMatch(name, @"\bbot\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public const string InvalidMeetingUrlError = "That doesn't look like a meeting link — paste the full https:// join URL.";

    /// <summary>
    /// Manual (UI/API) meeting add. WI #8575: the raw input is cleaned (leading "Join: "-style text
    /// stripped) and normalized like the AutoSync path, so NormalizedMeetingUrl is populated and the
    /// WI #7033 cross-user dedup applies — if another user already owns the primary for this meeting
    /// the new row is created as a Waiting subscriber (MeetingUrl/PrimaryMeeting set, no bot slot).
    /// Returns (null, error) without creating a row when the input isn't a recognizable URL.
    /// </summary>
    public async Task<(FirmMeeting? meeting, string? error)> CreateMeetingAsync(Guid userId, string meetingUrl, string? title, DateTime? startDatetime = null, string? calendarEventId = null, string? platform = null)
    {
        var cleanedUrl = CalendarService.ExtractMeetingUrl(meetingUrl);
        if (cleanedUrl == null)
        {
            _logger.LogInformation("FIRM: Rejected manual meeting add for user {UserId} — unrecognizable URL input", userId);
            return (null, InvalidMeetingUrlError);
        }
        var normalizedUrl = CalendarService.NormalizeMeetingUrl(cleanedUrl);
        if (string.IsNullOrEmpty(normalizedUrl))
            return (null, InvalidMeetingUrlError);

        var meeting = new FirmMeeting
        {
            Title = title ?? $"Meeting — {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            MeetingUrl = cleanedUrl,
            Platform = platform ?? DerivePlatformFromUrl(cleanedUrl),
            Status = MeetingStatus.Joining,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StartDatetime = startDatetime,
            CalendarEventId = calendarEventId,
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existingPrimary = await FindPrimaryForManualAddAsync(db, normalizedUrl, startDatetime);
        if (existingPrimary != null)
            return (await InsertSubscriberAsync(db, meeting, existingPrimary), null);

        meeting.IsPrimaryRecorder = true;
        meeting.NormalizedMeetingUrl = normalizedUrl;
        try
        {
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException dbEx) when (CalendarAutoSyncService.IsDuplicateKeyException(dbEx))
        {
            // Race on uk_fm_normalized_url_start: another user won the primary slot between our
            // lookup and INSERT. Retry as a subscriber on a fresh context (same as AutoSync).
            await using var raceDb = await _dbFactory.CreateDbContextAsync();
            var racedPrimary = await FindPrimaryForManualAddAsync(raceDb, normalizedUrl, startDatetime);
            if (racedPrimary == null)
            {
                _logger.LogWarning("FIRM: Manual add for {Url} @ {Start} hit a duplicate key with no active primary to subscribe to", normalizedUrl, startDatetime);
                return (null, "This meeting has already been added.");
            }
            meeting.Id = 0;
            meeting.NormalizedMeetingUrl = null;
            return (await InsertSubscriberAsync(raceDb, meeting, racedPrimary), null);
        }
        _logger.LogInformation("FIRM: Created meeting {Id} for user {UserId}", meeting.Id, userId);
        return (meeting, null);
    }

    /// <summary>
    /// WI #8575: the AutoSync dedup lookup (normalized URL + start ±15 min). A Join Now has no
    /// start time, so it is matched against "now" and, failing that, against any primary whose
    /// bot is already in this meeting regardless of its start time.
    /// </summary>
    private static async Task<FirmMeeting?> FindPrimaryForManualAddAsync(FirmDbContext db, string normalizedUrl, DateTime? startDatetime)
    {
        var primary = await CalendarAutoSyncService.FindExistingPrimaryAsync(db, normalizedUrl, startDatetime ?? DateTime.UtcNow, CancellationToken.None);
        if (primary != null || startDatetime != null) return primary;

        return await db.Meetings.FirstOrDefaultAsync(m =>
            m.IsPrimaryRecorder
            && m.NormalizedMeetingUrl == normalizedUrl
            && (m.Status == MeetingStatus.Pending || m.Status == MeetingStatus.Joining || m.Status == MeetingStatus.Recording));
    }

    private async Task<FirmMeeting> InsertSubscriberAsync(FirmDbContext db, FirmMeeting meeting, FirmMeeting primary)
    {
        meeting.IsPrimaryRecorder = false;
        meeting.PrimaryMeetingId = primary.Id;
        meeting.Status = MeetingStatus.Waiting;
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        meeting.PrimaryMeeting = primary;
        _logger.LogInformation("FIRM: Created meeting {Id} for user {UserId} as SUBSCRIBER of primary {PrimaryId}",
            meeting.Id, meeting.CreatedBy, primary.Id);

        // WI #8278: a primary that already finished won't fan out again on its own.
        if (primary.Status == MeetingStatus.Complete)
        {
            try { await FanOutToSubscribersAsync(primary.Id); }
            catch (Exception ex) { _logger.LogError(ex, "FIRM: Fan-out to late subscriber of completed primary {PrimaryId} failed", primary.Id); }
        }
        return meeting;
    }

    private static string DerivePlatformFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return "teams";
        if (url.Contains("zoom.us")) return "zoom";
        if (url.Contains("meet.google.com")) return "meet";
        return "teams";
    }

    public async Task UpdateStatusAsync(long id, MeetingStatus status, string? errorMessage = null, string? lastFailureReason = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(id);
        if (meeting == null) return;
        // WI #8576: error fields are only ever written together with a Failed transition, in the
        // same save — a row must never carry a failure reason while sitting at Scheduled/Pending.
        if ((errorMessage != null || lastFailureReason != null) && status != MeetingStatus.Failed)
        {
            _logger.LogWarning("FIRM: Meeting {Id} status update to {Status} carried error fields (reason={Reason}) — transitioning to Failed instead",
                id, status, lastFailureReason);
            status = MeetingStatus.Failed;
        }
        _logger.LogInformation("FIRM: Meeting {Id} status transition: {OldStatus} → {NewStatus}",
            id, meeting.Status, status);
        meeting.Status = status;
        meeting.UpdatedAt = DateTime.UtcNow;
        if (errorMessage != null) meeting.ErrorMessage = errorMessage;
        if (lastFailureReason != null) meeting.LastFailureReason = lastFailureReason;
        if (status == MeetingStatus.Recording)
        {
            // Bot successfully joined — any previously recorded failure (e.g. lobby_timeout) no longer applies.
            meeting.LastFailureReason = null;
            if (meeting.StartedAt == null)
                meeting.StartedAt = DateTime.UtcNow;
        }
        // Only set EndedAt/DurationSeconds on recording-end transitions (bot leaving the meeting)
        // Do NOT overwrite on retranscription callbacks (Summarizing, Complete)
        if (status is MeetingStatus.WaitingTranscript or MeetingStatus.Transcribing)
        {
            meeting.EndedAt = DateTime.UtcNow; // always update — allows a later retry bot's callback to win
            if (meeting.StartedAt != null)
                meeting.DurationSeconds = (int)(meeting.EndedAt.Value - meeting.StartedAt.Value).TotalSeconds;
        }
        // Failed: keep ??= — a subsequent retry shouldn't clear a meaningful EndedAt already set
        if (status == MeetingStatus.Failed)
        {
            meeting.EndedAt ??= DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        // WI #7035: only ever fires for a primary transitioning to Complete/Failed. Subscribers
        // (IsPrimaryRecorder=false) never trigger this, so fan-out cannot re-trigger itself —
        // subscriber rows are updated directly by FanOutToSubscribersAsync/PromoteOrFailSubscribersAsync
        // below, not via a recursive UpdateStatusAsync call. Wrapped in try/catch so a fan-out/
        // promotion failure isn't mistaken by VpCallback's caller for "the status update itself
        // failed" and retried — the primary's own status transition above already committed.
        try
        {
            if (meeting.IsPrimaryRecorder && status == MeetingStatus.Complete)
            {
                await FanOutToSubscribersAsync(meeting.Id);
            }
            else if (meeting.IsPrimaryRecorder && status == MeetingStatus.Failed)
            {
                await PromoteOrFailSubscribersAsync(meeting.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Subscriber fan-out/promotion failed for primary {Id} (status={Status}) — primary status transition itself still succeeded", id, status);
        }
    }

    /// <summary>WI #7035: subscriber ids currently pointing at this primary (any non-Failed status).</summary>
    public async Task<List<long>> GetSubscriberIdsAsync(long primaryMeetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings
            .Where(m => m.PrimaryMeetingId == primaryMeetingId && m.Status != MeetingStatus.Failed)
            .Select(m => m.Id)
            .ToListAsync();
    }

    /// <summary>
    /// WI #7035 Part A / WI #8278: when a primary meeting completes, copies its shared artifacts onto
    /// every linked subscriber and marks them Complete too, so a subscriber's detail page is identical
    /// to the primary's. Idempotent — safe to call again whenever the primary's artifacts change
    /// (VpCallback re-runs it after persisting the final S3 keys/roster/duration; a retranscribe
    /// re-runs it via the same summary_complete callback; AutoSync runs it for a subscriber attached
    /// after the primary already finished). Always re-reads the primary so the latest committed
    /// artifacts win. Returns the subscriber ids so the caller (the VpCallback controller) can fire
    /// the same per-user completion notifications it fires for the primary. Transcript segments/
    /// Participants/Summary rows are intentionally NOT duplicated — GetMeetingAsync resolves those
    /// from the primary via PrimaryMeetingId instead.
    /// </summary>
    public async Task<List<long>> FanOutToSubscribersAsync(long primaryMeetingId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var primary = await db.Meetings.FindAsync(new object[] { primaryMeetingId }, ct);
        if (primary == null || !primary.IsPrimaryRecorder || primary.Status != MeetingStatus.Complete)
            return new List<long>();

        var subscribers = await db.Meetings
            .Where(m => m.PrimaryMeetingId == primaryMeetingId && m.Status != MeetingStatus.Failed)
            .ToListAsync(ct);
        if (subscribers.Count == 0) return new List<long>();

        var now = DateTime.UtcNow;
        foreach (var sub in subscribers)
        {
            sub.AudioS3Key = primary.AudioS3Key;
            sub.TranscriptS3Key = primary.TranscriptS3Key;
            sub.RosterTimeline = primary.RosterTimeline;
            sub.SummaryVersion = primary.SummaryVersion;
            // WI #8294: suggestions are per-row so each organizer's dismissals are independent —
            // only seed a subscriber that has none yet, never clobber its Create/dismiss state.
            if (sub.SuggestedEvents == null) sub.SuggestedEvents = primary.SuggestedEvents;
            sub.StartedAt = primary.StartedAt;
            sub.EndedAt = primary.EndedAt;
            sub.DurationSeconds = primary.DurationSeconds;
            sub.Status = MeetingStatus.Complete;
            sub.ErrorMessage = null;
            sub.LastFailureReason = null;
            sub.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("FIRM: Fanned out completion from primary {Id} to {Count} subscriber(s)", primaryMeetingId, subscribers.Count);
        return subscribers.Select(s => s.Id).ToList();
    }

    /// <summary>WI #8576: how long after a meeting's start (ScheduledAt ?? StartDatetime) a bot has to
    /// reach Recording before no further subscribers are promoted and the remaining ones are failed.</summary>
    public static readonly TimeSpan PrimaryJoinGracePeriod = TimeSpan.FromMinutes(10);
    public const string NoPrimaryJoinedReason = "no_primary_joined";
    private const string NoPrimaryJoinedMessage = "No recording bot was able to join this meeting.";

    /// <summary>
    /// WI #7035 Part B / WI #8576: when a primary meeting fails (any reason), promotes the
    /// earliest-created Waiting subscriber to primary and dispatches its bot, re-pointing the
    /// remaining subscribers at it. Each promoted bot that fails re-enters here via
    /// UpdateStatusAsync, so subscribers are tried in created_at order until one records.
    /// No promotion once the join window has passed: start + 10 min if no bot ever reached
    /// Recording (remaining subscribers fail with no_primary_joined), or start + 30 min if the
    /// primary recorded and then failed (primary_failed_window_passed).
    /// </summary>
    public async Task PromoteOrFailSubscribersAsync(long primaryMeetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var primary = await db.Meetings.FindAsync(primaryMeetingId);
        if (primary == null || !primary.IsPrimaryRecorder) return;

        var subscribers = await db.Meetings
            .Where(m => m.PrimaryMeetingId == primaryMeetingId
                     && !m.IsPrimaryRecorder
                     && m.Status != MeetingStatus.Failed
                     && m.Status != MeetingStatus.Complete)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id)
            .ToListAsync();
        if (subscribers.Count == 0) return;

        var now = DateTime.UtcNow;
        var start = primary.ScheduledAt ?? primary.StartDatetime
            ?? subscribers.Select(m => m.ScheduledAt ?? m.StartDatetime).FirstOrDefault(d => d != null);
        var primaryRecorded = primary.StartedAt != null;
        var joinDeadline = start?.Add(primaryRecorded ? TimeSpan.FromMinutes(30) : PrimaryJoinGracePeriod);

        if (joinDeadline.HasValue && joinDeadline.Value <= now)
        {
            var reason = primaryRecorded ? "primary_failed_window_passed" : NoPrimaryJoinedReason;
            var message = primaryRecorded ? "Primary recorder failed and meeting window has passed." : NoPrimaryJoinedMessage;
            foreach (var sub in subscribers)
            {
                sub.Status = MeetingStatus.Failed;
                sub.ErrorMessage = message;
                sub.LastFailureReason = reason;
                sub.EndedAt ??= now;
                sub.UpdatedAt = now;
            }
            await db.SaveChangesAsync();
            _logger.LogInformation("FIRM: Primary {Id} failed past join window — {Count} subscriber(s) set to Failed ({Reason})", primaryMeetingId, subscribers.Count, reason);
            return;
        }

        var candidate = subscribers.FirstOrDefault(m => m.Status == MeetingStatus.Waiting);
        if (candidate == null)
        {
            _logger.LogInformation("FIRM: Primary {Id} failed within join window but no Waiting subscriber to promote", primaryMeetingId);
            return;
        }

        // Retrying execution strategy (EnableRetryOnFailure) requires user transactions to run inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // Release the failed primary's dedup anchor first so the promoted row can take it
            // without colliding on uk_fm_normalized_url_start (same as PrepareJoinNowAsync).
            var normalizedUrl = primary.NormalizedMeetingUrl;
            primary.NormalizedMeetingUrl = null;
            primary.UpdatedAt = now;
            await db.SaveChangesAsync();

            candidate.IsPrimaryRecorder = true;
            candidate.PrimaryMeetingId = null;
            candidate.NormalizedMeetingUrl = normalizedUrl;
            candidate.Status = MeetingStatus.Scheduled;
            candidate.UpdatedAt = now;

            foreach (var other in subscribers.Where(m => m.Id != candidate.Id))
            {
                other.PrimaryMeetingId = candidate.Id;
                other.UpdatedAt = now;
            }

            await db.SaveChangesAsync();
            await tx.CommitAsync();
        });
        _logger.LogInformation("FIRM: Promoted subscriber {CandidateId} to primary after primary {PrimaryId} failed (reason={Reason}); {Count} subscriber(s) re-pointed",
            candidate.Id, primaryMeetingId, primary.LastFailureReason, subscribers.Count - 1);

        try
        {
            // Same EventBridge → autojoin Lambda → AutoJoinTrigger path as the original dispatch,
            // fired immediately when the normal T-5 slot has already passed.
            await _autoJoinScheduler.DispatchPromotedAsync(candidate.Id, candidate.MeetingUrl ?? primary.MeetingUrl ?? "", start ?? now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to dispatch AutoJoin for promoted meeting {Id}", candidate.Id);
        }
    }

    /// <summary>
    /// WI #8576 T+10 cascade: fails every still-Waiting subscriber whose meeting started more than
    /// PrimaryJoinGracePeriod ago without any bot reaching Recording (the current primary has no
    /// StartedAt and isn't past Recording). Run periodically by StuckMeetingRecoveryService; the
    /// in-band equivalent on primary failure lives in PromoteOrFailSubscribersAsync.
    /// </summary>
    public async Task<int> FailSubscribersWithNoPrimaryJoinedAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - PrimaryJoinGracePeriod;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var stranded = await db.Meetings
            .Where(m => !m.IsPrimaryRecorder
                     && m.PrimaryMeetingId != null
                     && (m.Status == MeetingStatus.Waiting || m.Status == MeetingStatus.Scheduled)
                     && (m.ScheduledAt ?? m.StartDatetime ?? m.PrimaryMeeting!.ScheduledAt ?? m.PrimaryMeeting!.StartDatetime) < cutoff
                     && m.PrimaryMeeting!.StartedAt == null
                     && m.PrimaryMeeting!.Status != MeetingStatus.Recording
                     && m.PrimaryMeeting!.Status != MeetingStatus.WaitingTranscript
                     && m.PrimaryMeeting!.Status != MeetingStatus.Transcribing
                     && m.PrimaryMeeting!.Status != MeetingStatus.Summarizing
                     && m.PrimaryMeeting!.Status != MeetingStatus.Complete)
            .ToListAsync(ct);
        if (stranded.Count == 0) return 0;

        foreach (var sub in stranded)
        {
            sub.Status = MeetingStatus.Failed;
            sub.ErrorMessage = NoPrimaryJoinedMessage;
            sub.LastFailureReason = NoPrimaryJoinedReason;
            sub.EndedAt ??= now;
            sub.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        _logger.LogWarning("FIRM: T+10 cascade — {Count} subscriber(s) set to Failed ({Reason}): {Ids}",
            stranded.Count, NoPrimaryJoinedReason, string.Join(",", stranded.Select(m => m.Id)));
        return stranded.Count;
    }

    /// <summary>
    /// Recalculates DurationSeconds from the max transcript segment end time (ADO bug 2+3 follow-up).
    /// Called on summary_complete so the final duration reflects actual transcript coverage
    /// rather than whichever bot's callback happened to set EndedAt last.
    /// </summary>
    public async Task RecalculateDurationFromTranscriptAsync(long meetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(meetingId);
        if (meeting == null) return;

        var maxEndMs = await db.Transcripts
            .Where(t => t.MeetingId == meetingId)
            .MaxAsync(t => (long?)t.EndTimeMs);

        if (maxEndMs.HasValue && maxEndMs.Value > 0)
        {
            meeting.DurationSeconds = (int)(maxEndMs.Value / 1000);
            meeting.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            _logger.LogInformation("FIRM: Recalculated duration for meeting {Id} from transcript: {Seconds}s", meetingId, meeting.DurationSeconds);
        }
    }

    public async Task UpdateBotTaskArnAsync(long id, string? taskArn)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(id);
        if (meeting == null) return;
        meeting.BotTaskArn = taskArn;
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// WI #7909: resolve which row a "Join Now" should launch the bot against. A fresh Scheduled
    /// meeting is used as-is. A meeting that already had a bot run — terminal (Failed/Complete), or
    /// reverted to Scheduled after a failed run (lobby_timeout callback / StuckMeetingRecovery set
    /// LastFailureReason) — is cloned into a new Scheduled row so the original keeps its failure
    /// state, duration and artifacts instead of being overwritten by the second run.
    /// The clone takes over the calendar/dedup anchors (CalendarEventId, GraphMeetingId,
    /// NormalizedMeetingUrl): uk_fm_created_by_calendar_event_id and uk_fm_normalized_url_start
    /// don't allow both rows to hold them, and AutoSync should reconcile against the live row.
    /// Returns (null, error) if the meeting isn't joinable (in flight, or a subscriber).
    /// </summary>
    public async Task<(FirmMeeting? meeting, string? error)> PrepareJoinNowAsync(long id, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var original = await db.Meetings.FirstOrDefaultAsync(m => m.Id == id && m.CreatedBy == userId);
        if (original == null) return (null, "Meeting not found");

        var hadPriorRun = !string.IsNullOrEmpty(original.BotTaskArn) || !string.IsNullOrEmpty(original.LastFailureReason);
        if (original.Status == MeetingStatus.Scheduled && !hadPriorRun)
            return (original, null);

        if (original.Status is not (MeetingStatus.Scheduled or MeetingStatus.Failed or MeetingStatus.Complete)
            || !original.IsPrimaryRecorder)
            return (null, $"Meeting is not in a joinable state (current: {original.Status})");

        var clone = new FirmMeeting
        {
            Title = original.Title,
            Platform = original.Platform,
            MeetingUrl = original.MeetingUrl,
            ScheduledAt = original.ScheduledAt,
            StartDatetime = original.StartDatetime,
            CalendarEventId = original.CalendarEventId,
            GraphMeetingId = original.GraphMeetingId,
            Mode = original.Mode,
            CreatedBy = original.CreatedBy,
            CreatorEntraOid = original.CreatorEntraOid,
            Source = original.Source,
            IsPrimaryRecorder = true,
            NormalizedMeetingUrl = original.NormalizedMeetingUrl,
            Status = MeetingStatus.Scheduled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Retrying execution strategy (EnableRetryOnFailure) requires user transactions to run inside it.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();

            // Release the unique anchors first so the clone INSERT can't collide with them.
            original.CalendarEventId = null;
            original.GraphMeetingId = null;
            original.NormalizedMeetingUrl = null;
            if (original.Status == MeetingStatus.Scheduled)
                original.Status = MeetingStatus.Failed; // reverted-after-failure row: record the failed run
            original.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            db.Meetings.Add(clone);
            await db.SaveChangesAsync();

            // Subscribers still waiting on the original now follow the new run.
            var waiting = await db.Meetings
                .Where(m => m.PrimaryMeetingId == original.Id && m.Status == MeetingStatus.Waiting)
                .ToListAsync();
            foreach (var s in waiting)
            {
                s.PrimaryMeetingId = clone.Id;
                s.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();

            await tx.CommitAsync();
        });

        _logger.LogInformation(
            "FIRM: JoinNow on meeting {OriginalId} ({Status}, prior run) — cloned to new meeting {CloneId} for the new bot run",
            original.Id, original.Status, clone.Id);
        return (clone, null);
    }

    // ADO#1450-NOTE: MeetingUrl has no unique index — re-joining the same Teams URL
    // creates a new firm_meetings row each time. This is by design today but may need
    // a uniqueness strategy (per-user? time-window?) if duplicate meetings become an issue.
    public async Task<FirmUser?> GetOrCreateUserAsync(string entraOid, string email, string displayName)
    {
        if (string.IsNullOrEmpty(entraOid))
        {
            _logger.LogError("FIRM: GetOrCreateUserAsync called with empty entraOid — cannot proceed");
            return null;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Primary lookup: EntraOid is the identity key for Entra auth
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);

        if (user == null)
        {
            user = new FirmUser
            {
                Id = Guid.NewGuid(),
                EntraOid = entraOid,
                Email = email,
                DisplayName = displayName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync();
                _logger.LogInformation("FIRM: Provisioned new user {Email} OID={OID}", email, entraOid);
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("Duplicate entry") == true)
            {
                // Race condition: another concurrent request inserted between our SELECT and INSERT.
                // Discard tracked entities and re-fetch by OID.
                _logger.LogWarning("FIRM: Race condition duplicate key for OID={OID} — re-fetching", entraOid);
                await using var db2 = await _dbFactory.CreateDbContextAsync();
                user = await db2.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
                if (user == null)
                {
                    _logger.LogError("FIRM: Cannot resolve user OID={OID} after duplicate key race", entraOid);
                    return null;
                }
                return user;
            }
        }
        else
        {
            user.LastLoginAt = DateTime.UtcNow;
            user.DisplayName = displayName;
            user.Email = email;
            user.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Populate FaitUserId if not already set — best-effort, never throws
        if (string.IsNullOrEmpty(user.FaitUserId))
        {
            try
            {
                var faitId = await ResolveFaitUserIdAsync(entraOid);
                if (!string.IsNullOrEmpty(faitId))
                {
                    user.FaitUserId = faitId;
                    user.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    _logger.LogInformation("FIRM: Linked FaitUserId {FaitId} for user {Email}", faitId, email);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FIRM: Failed to resolve FAIT user ID for {Email} — KB push unavailable until next login", email);
            }
        }

        return user;
    }

    private async Task<string?> ResolveFaitUserIdAsync(string entraOid)
    {
        var faitApiUrl = _config["FIP:FaitApiUrl"]?.TrimEnd('/') ?? "https://fait.dev.fortressam.ai";
        var sharedSecret = _config["Firm:SharedSecret"] ?? "";
        if (string.IsNullOrEmpty(sharedSecret))
        {
            _logger.LogWarning("FIRM: Firm:SharedSecret not configured — cannot resolve FAIT user ID");
            return null;
        }

        using var http = _httpClientFactory.CreateClient();
        var url = $"{faitApiUrl}/api/firm/resolve-user?entraOid={Uri.EscapeDataString(entraOid)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Firm-Secret", sharedSecret);
        var response = await http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("FIRM: resolve-user returned {Status} for entraOid {OID}", response.StatusCode, entraOid);
            return null;
        }

        var body = await response.Content.ReadFromJsonAsync<ResolveFaitUserResponse>();
        return body?.UserId;
    }

    public async Task<(bool success, string? error)> RemoveMeetingAsync(long id, Guid userId)
    {
        var meeting = await GetMeetingAsync(id, userId);
        if (meeting == null)
            return (false, "Meeting not found or access denied");

        if (meeting.Status is MeetingStatus.Pending or MeetingStatus.Joining or MeetingStatus.Recording
            or MeetingStatus.WaitingTranscript or MeetingStatus.Transcribing or MeetingStatus.Summarizing)
            return (false, "Cannot remove a meeting that is currently in progress");

        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM firm_meetings WHERE id = {0}", id);
        return (true, null);
    }

    public async Task UpdateModeAsync(long id, string mode)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(id);
        if (meeting == null) return;
        meeting.Mode = mode;
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<FirmUser?> GetUserAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users.FindAsync(userId);
    }

    public async Task UpdateUserPreferencesAsync(Guid userId, bool autoAddCalendarMeetings, bool autoEmailSummary, bool autoCreateSuggestedEvents)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user == null) return;
        var autoJoinTurnedOff = user.AutoAddCalendarMeetings && !autoAddCalendarMeetings;
        user.AutoAddCalendarMeetings = autoAddCalendarMeetings;
        user.AutoEmailSummary = autoEmailSummary;
        user.AutoCreateSuggestedEvents = autoCreateSuggestedEvents;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // WI #8864: turning auto-join off must cancel the EventBridge schedules already created for
        // upcoming meetings, otherwise they still fire and dispatch the bot. Saved first so the
        // calendar sync stops creating new schedules. Meeting rows stay Scheduled — the user can
        // still join manually.
        if (!autoJoinTurnedOff) return;
        var now = DateTime.UtcNow;
        var scheduledIds = await db.Meetings
            .Where(m => m.CreatedBy == userId && m.Status == MeetingStatus.Scheduled && m.StartDatetime > now)
            .Select(m => m.Id)
            .ToListAsync();
        foreach (var meetingId in scheduledIds)
        {
            try
            {
                await _autoJoinScheduler.DeleteScheduleAsync(meetingId);
            }
            catch (Exception ex)
            {
                // Non-fatal — the preference save has already succeeded.
                _logger.LogWarning(ex, "FIRM: Failed to delete AutoJoin schedule for meeting {Id} after user {UserId} disabled auto-join", meetingId, userId);
            }
        }
        _logger.LogInformation("FIRM: User {UserId} disabled auto-join — cleared {Count} AutoJoin schedule(s)", userId, scheduledIds.Count);
    }

    // NOTE (ADO#17 diagnostic, 2026-09-09): UpsertFromCalendarAsync used to live here as a second,
    // independently-maintained implementation of "create a meeting from a calendar event" — it had
    // zero callers (the live path is CalendarAutoSyncService.PollCoreAsync, which has its own
    // dedup + Joining->Scheduled insert logic). Deleted rather than kept in sync forever; see
    // CalendarAutoSyncService.InsertScheduledMeetingAsync for the one remaining copy of the
    // EF-sentinel workaround this method used to duplicate.

    private record ResolveFaitUserResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("userId")] string UserId);

    /// <summary>
    /// Submits an AWS Batch transcription job for the given meeting's audio (ADO#2179).
    /// Fetches the meeting record to get AudioS3Key and StartedAt, then submits the Batch job.
    /// Returns the Batch job ID. Throws if AudioS3Key is null/empty.
    /// </summary>
    public async Task<string> SubmitTranscriptionJobAsync(long meetingId, bool summarizeOnly = false, DiarizationOverrides? diarization = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.Include(m => m.CreatedByUser).FirstOrDefaultAsync(m => m.Id == meetingId);
        if (meeting == null)
            throw new InvalidOperationException($"Meeting {meetingId} not found");

        // WI #8335: summarize-only reuses the existing transcript, so audio isn't required.
        if (string.IsNullOrEmpty(meeting.AudioS3Key) && !summarizeOnly)
            throw new InvalidOperationException($"Meeting {meetingId} has no AudioS3Key — cannot submit transcription job");

        var audioS3Key = meeting.AudioS3Key ?? "";
        var meetingDate = meeting.StartedAt ?? meeting.ScheduledAt;
        var creatorEntraOid = meeting.CreatorEntraOid ?? meeting.CreatedByUser?.EntraOid;
        var rosterTimeline = meeting.RosterTimeline;
        var activeSpeakerTimeline = meeting.ActiveSpeakerTimeline;
        var jobId = await _batchService.SubmitTranscriptionJobAsync(meetingId, audioS3Key, meetingDate, creatorEntraOid, rosterTimeline, activeSpeakerTimeline,
            meetingStartDatetime: meeting.StartDatetime ?? meeting.StartedAt,
            summarizeOnly: summarizeOnly, transcriptS3Key: meeting.TranscriptS3Key, speakerCorrections: meeting.SpeakerCorrections,
            diarization: diarization);
        _logger.LogInformation("FIRM: SubmitTranscriptionJobAsync submitted Batch job {JobId} for meeting {MeetingId}", jobId, meetingId);
        return jobId;
    }

    /// <summary>
    /// Submits an AWS Batch transcription job for the meeting's audio (ADO#1844).
    /// Replaces the previous vpbot HTTP call — firm-web now submits Batch directly.
    /// Returns (true, null) on success, (false, errorMessage) on failure.
    /// WI #8875: <paramref name="diarization"/> carries optional per-run diarization overrides.
    /// </summary>
    public async Task<(bool success, string? error)> RetranscribeAsync(long meetingId, Guid userId, DiarizationOverrides? diarization = null)
    {
        var meeting = await GetMeetingAsync(meetingId, userId);
        if (meeting == null)
            return (false, "Meeting not found or access denied");

        if (string.IsNullOrEmpty(meeting.AudioS3Key))
            return (false, "No audio recording available for this meeting");

        // WI #8278: a subscriber's transcript/summary are read from its primary, so retranscribing a
        // subscriber must retranscribe the primary — its summary_complete callback then re-fans the
        // new artifacts out to every subscriber (including this one).
        var targetMeetingId = meeting.PrimaryMeetingId ?? meetingId;

        try
        {
            var jobId = await SubmitTranscriptionJobAsync(targetMeetingId, diarization: diarization);

            // Reset meeting status to Transcribing
            await using var db = await _dbFactory.CreateDbContextAsync();
            var dbMeeting = await db.Meetings.FindAsync(targetMeetingId);
            if (dbMeeting != null)
            {
                dbMeeting.Status = MeetingStatus.Transcribing;
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("FIRM: RetranscribeAsync submitted Batch job {JobId} for meeting {MeetingId} (requested via {RequestedId})", jobId, targetMeetingId, meetingId);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: RetranscribeAsync failed for meeting {MeetingId}", meetingId);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// WI #8294: persists the transcriber's suggested_events for a meeting. Replaces any prior
    /// suggestions (a retranscribe is a fresh analysis); every item starts as pending.
    /// </summary>
    public async Task StoreSuggestedEventsAsync(long meetingId, string suggestedEventsJson)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(meetingId);
        if (meeting == null) return;

        var events = SuggestedEvent.ParseList(suggestedEventsJson);
        foreach (var e in events)
        {
            e.Status = SuggestedEvent.StatusPending;
            e.CreatedEventId = null;
        }
        meeting.SuggestedEvents = SuggestedEvent.SerializeList(events);
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Stored {Count} suggested event(s) for meeting {MeetingId}", events.Count, meetingId);
    }

    /// <summary>WI #8294: suggestions for a meeting the user organizes (CreatedBy). Empty if not owner.</summary>
    public async Task<List<SuggestedEvent>> GetSuggestedEventsAsync(long meetingId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var json = await db.Meetings
            .Where(m => m.Id == meetingId && m.CreatedBy == userId)
            .Select(m => m.SuggestedEvents)
            .FirstOrDefaultAsync();
        return SuggestedEvent.ParseList(json);
    }

    /// <summary>
    /// WI #8294: records the organizer's decision on one suggestion (by index). Only the meeting's
    /// organizer may change it. Returns false if the meeting/suggestion isn't found.
    /// </summary>
    public async Task<bool> UpdateSuggestedEventStatusAsync(long meetingId, Guid userId, int index, string status, string? createdEventId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId && m.CreatedBy == userId);
        if (meeting == null) return false;

        var events = SuggestedEvent.ParseList(meeting.SuggestedEvents);
        if (index < 0 || index >= events.Count) return false;

        events[index].Status = status;
        if (createdEventId != null) events[index].CreatedEventId = createdEventId;
        meeting.SuggestedEvents = SuggestedEvent.SerializeList(events);
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    /// <summary>WI #8335: the speaker-corrections overlay JSON for a meeting (resolved from the primary
    /// for subscribers — the transcript it overlays is shared). Null when there are none.</summary>
    public async Task<string?> GetSpeakerCorrectionsAsync(long meetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var artifactMeetingId = await ResolveArtifactMeetingIdAsync(db, meetingId);
        return await db.Meetings
            .Where(m => m.Id == artifactMeetingId)
            .Select(m => m.SpeakerCorrections)
            .FirstOrDefaultAsync();
    }

    /// <summary>
    /// WI #8335: replaces the meeting's active speaker corrections with <paramref name="correctionsJson"/>
    /// (a JSON array), then submits a summarize-only Batch job so the summary reflects them. Archived
    /// corrections (made against a transcript a retranscribe has since replaced) are always kept — user
    /// corrections are never deleted. Callers must have already checked the user owns the meeting row.
    /// Throws ArgumentException for an invalid overlay, InvalidOperationException if the meeting can't
    /// be re-summarized right now. Corrections are committed before the job is submitted, so a Batch
    /// failure never loses the user's work — it's logged and reported by returning false.
    /// </summary>
    public async Task<bool> SaveSpeakerCorrectionsAsync(long meetingId, string correctionsJson)
    {
        List<SpeakerCorrection>? incoming;
        try { incoming = System.Text.Json.JsonSerializer.Deserialize<List<SpeakerCorrection>>(correctionsJson); }
        catch (System.Text.Json.JsonException ex) { throw new ArgumentException($"Corrections are not valid JSON: {ex.Message}"); }
        if (incoming == null) throw new ArgumentException("Corrections must be a JSON array");

        var active = incoming.Where(c => !c.Archived).ToList();
        foreach (var c in active)
        {
            var error = c.Validate();
            if (error != null) throw new ArgumentException(error);
            c.ResolvedName = c.ResolvedName.Trim();
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var artifactMeetingId = await ResolveArtifactMeetingIdAsync(db, meetingId);
        var meeting = await db.Meetings.FindAsync(artifactMeetingId)
            ?? throw new InvalidOperationException($"Meeting {meetingId} not found");
        if (meeting.Status != MeetingStatus.Complete)
            throw new InvalidOperationException("Speaker corrections can only be saved once the meeting is complete");
        if (string.IsNullOrEmpty(meeting.TranscriptS3Key))
            throw new InvalidOperationException("This meeting has no stored transcript to re-summarize");

        var merged = SpeakerCorrection.ParseList(meeting.SpeakerCorrections).Where(c => c.Archived).Concat(active).ToList();
        if (merged.Count > SpeakerCorrection.MaxCorrections)
            throw new ArgumentException($"A meeting can have at most {SpeakerCorrection.MaxCorrections} speaker corrections");

        meeting.SpeakerCorrections = merged.Count > 0 ? SpeakerCorrection.SerializeList(merged) : null;
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Saved {Count} active speaker correction(s) for meeting {MeetingId} (requested via {RequestedId})",
            active.Count, artifactMeetingId, meetingId);

        try
        {
            var jobId = await SubmitTranscriptionJobAsync(artifactMeetingId, summarizeOnly: true);
            _logger.LogInformation("FIRM: Speaker corrections re-summarize Batch job {JobId} submitted for meeting {MeetingId}", jobId, artifactMeetingId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Speaker corrections saved for meeting {MeetingId} but re-summarize submission failed", artifactMeetingId);
            return false;
        }
    }

    /// <summary>
    /// WI #8335: a retranscribe replaced the transcript the corrections were made against — segment
    /// indexes and SPEAKER_XX labels no longer line up, so mark every correction archived (kept, but no
    /// longer applied). Called when transcription_complete rewrites the transcript rows.
    /// </summary>
    public async Task ArchiveSpeakerCorrectionsAsync(long meetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(meetingId);
        if (meeting == null || string.IsNullOrEmpty(meeting.SpeakerCorrections)) return;

        var corrections = SpeakerCorrection.ParseList(meeting.SpeakerCorrections);
        if (corrections.All(c => c.Archived)) return;
        foreach (var c in corrections) c.Archived = true;
        meeting.SpeakerCorrections = SpeakerCorrection.SerializeList(corrections);
        meeting.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Archived {Count} speaker correction(s) for meeting {MeetingId} — transcript was replaced", corrections.Count, meetingId);
    }

    private static async Task<long> ResolveArtifactMeetingIdAsync(FirmDbContext db, long meetingId)
    {
        var primaryId = await db.Meetings
            .Where(m => m.Id == meetingId)
            .Select(m => m.PrimaryMeetingId)
            .FirstOrDefaultAsync();
        return primaryId ?? meetingId;
    }
}
