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

    public async Task<FirmUser?> GetOrCreateUserAsync(string entraOid, string email, string displayName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
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
            await db.SaveChangesAsync();
            _logger.LogInformation("FIRM: Provisioned new user {Email}", email);
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

    private record ResolveFaitUserResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("userId")] string UserId);
}
