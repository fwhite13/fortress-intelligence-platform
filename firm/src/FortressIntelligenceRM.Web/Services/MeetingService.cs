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

    public MeetingService(IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config, ILogger<MeetingService> logger, IHttpClientFactory httpClientFactory, IBatchTranscriptionService batchService)
    {
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _batchService = batchService;
    }

    public async Task<List<FirmMeeting>> GetMeetingsAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings
            .Where(m => m.CreatedBy == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
    }

    public async Task<FirmMeeting?> GetMeetingAsync(long id, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meetings
            .Include(m => m.Participants)
            .Include(m => m.Transcripts.OrderBy(t => t.StartTimeMs))
            .Include(m => m.Summary)
            .FirstOrDefaultAsync(m => m.Id == id && m.CreatedBy == userId);
    }

    public async Task<FirmMeeting> CreateMeetingAsync(Guid userId, string meetingUrl, string? title, DateTime? startDatetime = null, string? calendarEventId = null, string? platform = null, MeetingStatus initialStatus = MeetingStatus.Joining)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = new FirmMeeting
        {
            Title = title ?? $"Meeting — {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            MeetingUrl = meetingUrl,
            Platform = platform ?? DerivePlatformFromUrl(meetingUrl),
            Status = initialStatus,
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

    public async Task UpdateStatusAsync(long id, MeetingStatus status, string? errorMessage = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FindAsync(id);
        if (meeting == null) return;
        meeting.Status = status;
        meeting.UpdatedAt = DateTime.UtcNow;
        if (errorMessage != null) meeting.ErrorMessage = errorMessage;
        if (status == MeetingStatus.Recording && meeting.StartedAt == null)
            meeting.StartedAt = DateTime.UtcNow;
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

    /// <summary>
    /// Upserts a meeting from calendar detection. Keyed on calendar_event_id to prevent duplicates.
    /// If meeting already exists for this user + calendar_event_id, returns existing meeting.
    /// </summary>
    public async Task<FirmMeeting> UpsertFromCalendarAsync(Guid userId, CalendarMeetingDto dto)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var existing = await db.Meetings.FirstOrDefaultAsync(m =>
            m.CreatedBy == userId &&
            m.CalendarEventId == dto.CalendarEventId);

        if (existing != null)
            return existing;

        if (!DateTime.TryParse(dto.StartDateTime, null, System.Globalization.DateTimeStyles.RoundtripKind, out var startDt))
            startDt = DateTime.UtcNow;

        var meeting = new FirmMeeting
        {
            Title = dto.Subject,
            MeetingUrl = dto.JoinUrl,
            Status = MeetingStatus.Scheduled,
            Platform = dto.Platform,
            Mode = dto.Mode,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            StartDatetime = startDt,
            CalendarEventId = dto.CalendarEventId,
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Calendar upsert created meeting {Id} Mode {Mode} for user {UserId}", meeting.Id, dto.Mode, userId);
        return meeting;
    }

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
