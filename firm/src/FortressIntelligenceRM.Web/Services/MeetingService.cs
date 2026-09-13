using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

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
        return await db.Meetings
            .Where(m => m.CreatedBy == userId)
            .OrderByDescending(m => m.StartDatetime ?? m.CreatedAt)
            .ToListAsync();
    }

    public async Task<FirmMeeting?> GetMeetingAsync(long id, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == id && m.CreatedBy == userId);
        if (meeting == null) return null;

        // WI #7033/#7035: a subscriber's own Participants/Transcripts/Summary rows are never
        // duplicated on fan-out — only AudioS3Key/TranscriptS3Key are copied onto the subscriber row
        // (see FanOutCompletionAsync). Resolve the effective source-of-truth meeting id for these
        // relational artifacts here so a subscriber's detail page renders the shared content.
        var artifactMeetingId = meeting.PrimaryMeetingId ?? meeting.Id;
        meeting.Participants = await db.Participants.Where(p => p.MeetingId == artifactMeetingId).ToListAsync();
        meeting.Transcripts = await db.Transcripts.Where(t => t.MeetingId == artifactMeetingId).OrderBy(t => t.StartTimeMs).ToListAsync();
        meeting.Summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == artifactMeetingId);

        return meeting;
    }

    public async Task<FirmMeeting> CreateMeetingAsync(Guid userId, string meetingUrl, string? title, DateTime? startDatetime = null, string? calendarEventId = null, string? platform = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = new FirmMeeting
        {
            Title = title ?? $"Meeting — {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            MeetingUrl = meetingUrl,
            Platform = platform ?? DerivePlatformFromUrl(meetingUrl),
            Status = MeetingStatus.Joining,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StartDatetime = startDatetime,
            CalendarEventId = calendarEventId,
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Created meeting {Id} for user {UserId}", meeting.Id, userId);
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
        // subscriber rows are updated directly by FanOutCompletionAsync/PromoteOrFailSubscribersAsync
        // below, not via a recursive UpdateStatusAsync call. Wrapped in try/catch so a fan-out/
        // promotion failure isn't mistaken by VpCallback's caller for "the status update itself
        // failed" and retried — the primary's own status transition above already committed.
        try
        {
            if (meeting.IsPrimaryRecorder && status == MeetingStatus.Complete)
            {
                await FanOutCompletionAsync(meeting.Id);
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
    /// WI #7035 Part A: when a primary meeting completes, copies its shared artifacts onto every
    /// linked subscriber and marks them Complete too. Returns the subscriber ids so the caller (the
    /// VpCallback controller) can fire the same per-user completion notifications it fires for the
    /// primary. Transcript segments/Summary rows are intentionally NOT duplicated — GetMeetingAsync
    /// resolves those from the primary via PrimaryMeetingId instead.
    /// </summary>
    public async Task<List<long>> FanOutCompletionAsync(long primaryMeetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var primary = await db.Meetings.FindAsync(primaryMeetingId);
        if (primary == null || !primary.IsPrimaryRecorder) return new List<long>();

        var subscribers = await db.Meetings
            .Where(m => m.PrimaryMeetingId == primaryMeetingId && m.Status != MeetingStatus.Failed)
            .ToListAsync();
        if (subscribers.Count == 0) return new List<long>();

        var now = DateTime.UtcNow;
        foreach (var sub in subscribers)
        {
            sub.AudioS3Key = primary.AudioS3Key;
            sub.TranscriptS3Key = primary.TranscriptS3Key;
            sub.Status = MeetingStatus.Complete;
            sub.StartedAt ??= primary.StartedAt;
            sub.EndedAt = primary.EndedAt;
            sub.DurationSeconds = primary.DurationSeconds;
            sub.UpdatedAt = now;
        }
        await db.SaveChangesAsync();

        _logger.LogInformation("FIRM: Fanned out completion from primary {Id} to {Count} subscriber(s)", primaryMeetingId, subscribers.Count);
        return subscribers.Select(s => s.Id).ToList();
    }

    /// <summary>
    /// WI #7035 Part B: when a primary meeting fails, promotes the earliest-created Waiting
    /// subscriber to primary (re-triggering the join) if still within the join window
    /// (start_datetime + 30 min), otherwise fails all remaining subscribers with a reason.
    /// </summary>
    public async Task PromoteOrFailSubscribersAsync(long primaryMeetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var primary = await db.Meetings.FindAsync(primaryMeetingId);
        if (primary == null || !primary.IsPrimaryRecorder) return;

        var subscribers = await db.Meetings
            .Where(m => m.PrimaryMeetingId == primaryMeetingId && m.Status != MeetingStatus.Failed)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();
        if (subscribers.Count == 0) return;

        var withinJoinWindow = primary.StartDatetime.HasValue
            && primary.StartDatetime.Value.AddMinutes(30) > DateTime.UtcNow;

        if (!withinJoinWindow)
        {
            foreach (var sub in subscribers)
            {
                sub.Status = MeetingStatus.Failed;
                sub.ErrorMessage = "Primary recorder failed and meeting window has passed.";
                sub.LastFailureReason = "primary_failed_window_passed";
                sub.EndedAt ??= DateTime.UtcNow;
                sub.UpdatedAt = DateTime.UtcNow;
            }
            await db.SaveChangesAsync();
            _logger.LogInformation("FIRM: Primary {Id} failed past join window — {Count} subscriber(s) set to Failed", primaryMeetingId, subscribers.Count);
            return;
        }

        var candidate = subscribers.FirstOrDefault(m => m.Status == MeetingStatus.Waiting);
        if (candidate == null)
        {
            _logger.LogInformation("FIRM: Primary {Id} failed within join window but no Waiting subscriber to promote", primaryMeetingId);
            return;
        }

        candidate.IsPrimaryRecorder = true;
        candidate.PrimaryMeetingId = null;
        candidate.NormalizedMeetingUrl = primary.NormalizedMeetingUrl;
        candidate.Status = MeetingStatus.Scheduled;
        candidate.UpdatedAt = DateTime.UtcNow;

        foreach (var other in subscribers.Where(m => m.Id != candidate.Id))
        {
            other.PrimaryMeetingId = candidate.Id;
            other.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Promoted subscriber {CandidateId} to primary after primary {PrimaryId} failed within join window ({Count} subscriber(s) re-pointed)",
            candidate.Id, primaryMeetingId, subscribers.Count - 1);

        try
        {
            await _autoJoinScheduler.CreateScheduleAsync(candidate.Id, candidate.MeetingUrl ?? primary.MeetingUrl ?? "", candidate.StartDatetime ?? primary.StartDatetime ?? DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to create AutoJoin schedule for promoted meeting {Id}", candidate.Id);
        }
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

    public async Task UpdateUserPreferencesAsync(Guid userId, bool autoAddCalendarMeetings, bool autoEmailSummary)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user == null) return;
        user.AutoAddCalendarMeetings = autoAddCalendarMeetings;
        user.AutoEmailSummary = autoEmailSummary;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
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
    public async Task<string> SubmitTranscriptionJobAsync(long meetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.Include(m => m.CreatedByUser).FirstOrDefaultAsync(m => m.Id == meetingId);
        if (meeting == null)
            throw new InvalidOperationException($"Meeting {meetingId} not found");

        if (string.IsNullOrEmpty(meeting.AudioS3Key))
            throw new InvalidOperationException($"Meeting {meetingId} has no AudioS3Key — cannot submit transcription job");

        var audioS3Key = meeting.AudioS3Key;
        var meetingDate = meeting.StartedAt ?? meeting.ScheduledAt;
        var creatorEntraOid = meeting.CreatorEntraOid ?? meeting.CreatedByUser?.EntraOid;
        var jobId = await _batchService.SubmitTranscriptionJobAsync(meetingId, audioS3Key, meetingDate, creatorEntraOid);
        _logger.LogInformation("FIRM: SubmitTranscriptionJobAsync submitted Batch job {JobId} for meeting {MeetingId}", jobId, meetingId);
        return jobId;
    }

    /// <summary>
    /// Submits an AWS Batch transcription job for the meeting's audio (ADO#1844).
    /// Replaces the previous vpbot HTTP call — firm-web now submits Batch directly.
    /// Returns (true, null) on success, (false, errorMessage) on failure.
    /// </summary>
    public async Task<(bool success, string? error)> RetranscribeAsync(long meetingId, Guid userId)
    {
        var meeting = await GetMeetingAsync(meetingId, userId);
        if (meeting == null)
            return (false, "Meeting not found or access denied");

        if (string.IsNullOrEmpty(meeting.AudioS3Key))
            return (false, "No audio recording available for this meeting");

        try
        {
            var jobId = await SubmitTranscriptionJobAsync(meetingId);

            // Reset meeting status to Transcribing
            await using var db = await _dbFactory.CreateDbContextAsync();
            var dbMeeting = await db.Meetings.FindAsync(meetingId);
            if (dbMeeting != null)
            {
                dbMeeting.Status = MeetingStatus.Transcribing;
                await db.SaveChangesAsync();
            }

            _logger.LogInformation("FIRM: RetranscribeAsync submitted Batch job {JobId} for meeting {MeetingId}", jobId, meetingId);
            return (true, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: RetranscribeAsync failed for meeting {MeetingId}", meetingId);
            return (false, ex.Message);
        }
    }
}
