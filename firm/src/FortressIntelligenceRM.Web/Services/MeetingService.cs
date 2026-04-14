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

    public MeetingService(IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config, ILogger<MeetingService> logger, IHttpClientFactory httpClientFactory)
    {
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
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

    public async Task<FirmMeeting> CreateMeetingAsync(Guid userId, string meetingUrl, string? title, DateTime? startDatetime = null, string? calendarEventId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = new FirmMeeting
        {
            Title = title ?? $"Meeting — {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            MeetingUrl = meetingUrl,
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
        if (status is MeetingStatus.Complete or MeetingStatus.Failed)
        {
            meeting.EndedAt ??= DateTime.UtcNow;
            if (meeting.StartedAt != null)
                meeting.DurationSeconds = (int)(meeting.EndedAt.Value - meeting.StartedAt.Value).TotalSeconds;
        }
        await db.SaveChangesAsync();
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
    /// Triggers vpbot to re-download audio from S3 and run the full transcribe+summarize pipeline.
    /// Returns (true, null) on success, (false, errorMessage) on failure.
    /// </summary>
    public async Task<(bool success, string? error)> RetranscribeAsync(long meetingId, Guid userId)
    {
        var meeting = await GetMeetingAsync(meetingId, userId);
        if (meeting == null)
            return (false, "Meeting not found or access denied");

        if (string.IsNullOrEmpty(meeting.AudioS3Key))
            return (false, "No audio recording available for this meeting");

        var vpbotUrl = _config["Firm:VpBotUrl"] ?? _config["FIRM_VPBOT_URL"];
        if (string.IsNullOrEmpty(vpbotUrl))
            return (false, "VpBot URL not configured");

        var botSecret = _config["Firm:BotCallbackSecret"] ?? "";

        try
        {
            using var http = _httpClientFactory.CreateClient();
            var payload = new
            {
                firmMeetingId = meetingId,
                audioS3Key = meeting.AudioS3Key
            };
            var content = new StringContent(
                System.Text.Json.JsonSerializer.Serialize(payload),
                System.Text.Encoding.UTF8,
                "application/json");

            using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var request = new HttpRequestMessage(HttpMethod.Post, $"{vpbotUrl}/api/meetings/retranscribe")
            {
                Content = content
            };
            request.Headers.Add("X-Bot-Secret", botSecret);

            // ResponseHeadersRead returns as soon as headers arrive — don't wait for body
            HttpResponseMessage response;
            bool acceptedOrTimeout = false;
            try
            {
                response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, acceptCts.Token);
                if (!response.IsSuccessStatusCode)
                {
                    string body;
                    try { body = await response.Content.ReadAsStringAsync(); } catch { body = "(unreadable)"; }
                    return (false, $"VpBot error {(int)response.StatusCode}: {body}");
                }
                acceptedOrTimeout = true;
            }
            catch (OperationCanceledException)
            {
                // vpbot didn't respond within 10s — it may still be processing
                // Treat as accepted (fire-and-forget is working as designed)
                _logger.LogWarning("FIRM: vpbot retranscribe accept timeout (10s) for meeting {MeetingId} — treating as accepted", meetingId);
                acceptedOrTimeout = true;
            }

            if (acceptedOrTimeout)
            {
                // Reset meeting status to allow callback pipeline
                await using var db = await _dbFactory.CreateDbContextAsync();
                var dbMeeting = await db.Meetings.FindAsync(meetingId);
                if (dbMeeting != null)
                {
                    dbMeeting.Status = MeetingStatus.Transcribing;
                    await db.SaveChangesAsync();
                }

                _logger.LogInformation("FIRM: RetranscribeAsync triggered for meeting {MeetingId}", meetingId);
                return (true, null);
            }

            // Should never reach here, but satisfy compiler
            return (false, "Unexpected state");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: RetranscribeAsync failed for meeting {MeetingId}", meetingId);
            return (false, ex.Message);
        }
    }
}
