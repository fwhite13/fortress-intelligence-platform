using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using FortressIntelligenceRM.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Controllers;

[ApiController]
public class MeetingsApiController : ControllerBase
{
    private readonly MeetingService _meetingService;
    private readonly VpBotService _vpBotService;
    private readonly S3Service _s3Service;
    private readonly FirmKbService _firmKbService;
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MeetingsApiController> _logger;

    public MeetingsApiController(
        MeetingService meetingService,
        VpBotService vpBotService,
        S3Service s3Service,
        FirmKbService firmKbService,
        IDbContextFactory<FirmDbContext> dbFactory,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<MeetingsApiController> logger)
    {
        _meetingService = meetingService;
        _vpBotService = vpBotService;
        _s3Service = s3Service;
        _firmKbService = firmKbService;
        _dbFactory = dbFactory;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private Guid? GetCurrentUserId()
    {
        var oidClaim = User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (oidClaim == null) return null;
        // Try to find the user by entra OID
        return null; // Will be resolved via MeetingService
    }

    [HttpPost("/api/meetings/join")]
    [Authorize]
    public async Task<IActionResult> JoinMeeting([FromBody] JoinRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MeetingUrl))
            return BadRequest(new { error = "MeetingUrl is required" });

        // Get Entra OID from claims
        var entraOid = User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("preferred_username")?.Value ?? "";
        var displayName = User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value ?? email;

        if (string.IsNullOrEmpty(entraOid))
            return Unauthorized();

        var firmUser = await _meetingService.GetOrCreateUserAsync(entraOid, email, displayName);
        if (firmUser == null) return StatusCode(500, new { error = "Failed to resolve user" });

        var meeting = await _meetingService.CreateMeetingAsync(firmUser.Id, request.MeetingUrl, request.Title);

        // Trigger bot (fire and forget — don't fail the response if ECS isn't configured)
        _ = _vpBotService.TriggerBotAsync(meeting.Id, request.MeetingUrl);

        return Ok(new { meetingId = meeting.Id });
    }

    [HttpPost("/api/vp/callback")]
    public async Task<IActionResult> VpCallback([FromBody] VpCallbackPayload payload)
    {
        // Validate shared secret — fail-closed: missing config blocks all requests
        var expectedSecret = _config["Firm:BotCallbackSecret"];
        var providedSecret = Request.Headers["X-Bot-Secret"].FirstOrDefault();
        if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
        {
            _logger.LogWarning("FIRM: VP callback rejected — invalid or missing X-Bot-Secret");
            return Unauthorized();
        }

        _logger.LogInformation("FIRM: VP callback received for meeting {Id}, status {Status}",
            payload.MeetingId, payload.Status);

        var statusMap = new Dictionary<string, MeetingStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["recording"] = MeetingStatus.Recording,
            ["recording_complete"] = MeetingStatus.Transcribing,
            ["transcription_complete"] = MeetingStatus.Summarizing,
            ["summary_complete"] = MeetingStatus.Complete,
            ["failed"] = MeetingStatus.Failed,
            ["recording_failed"] = MeetingStatus.Failed
        };

        if (!statusMap.TryGetValue(payload.Status, out var meetingStatus))
        {
            _logger.LogWarning("FIRM: Unknown callback status: {Status}", payload.Status);
            return Ok();
        }

        await _meetingService.UpdateStatusAsync(payload.MeetingId, meetingStatus, payload.Error);

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Update S3 keys if provided
        var meeting = await db.Meetings.FindAsync(payload.MeetingId);
        if (meeting != null)
        {
            if (!string.IsNullOrEmpty(payload.AudioS3Key)) meeting.AudioS3Key = payload.AudioS3Key;
            if (!string.IsNullOrEmpty(payload.TranscriptS3Key)) meeting.TranscriptS3Key = payload.TranscriptS3Key;
            meeting.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        // Write participants on recording status
        if (meetingStatus == MeetingStatus.Recording && payload.Participants != null)
        {
            // Remove old participants for this meeting
            var existing = db.Participants.Where(p => p.MeetingId == payload.MeetingId);
            db.Participants.RemoveRange(existing);
            foreach (var p in payload.Participants)
            {
                db.Participants.Add(new FirmMeetingParticipant
                {
                    MeetingId = payload.MeetingId,
                    DisplayName = p.DisplayName ?? "Unknown",
                    Email = p.Email,
                    JoinedAt = p.JoinedAt
                });
            }
            await db.SaveChangesAsync();
        }

        // Write transcript on transcription_complete
        if (meetingStatus == MeetingStatus.Summarizing && payload.Segments != null)
        {
            foreach (var seg in payload.Segments)
            {
                db.Transcripts.Add(new FirmMeetingTranscript
                {
                    MeetingId = payload.MeetingId,
                    SpeakerLabel = seg.SpeakerLabel,
                    SpeakerName = seg.SpeakerName,
                    Text = seg.Text ?? "",
                    StartTimeMs = seg.StartTimeMs,
                    EndTimeMs = seg.EndTimeMs,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        // Write summary on summary_complete
        if (meetingStatus == MeetingStatus.Complete && payload.Summary != null)
        {
            var existingSummary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == payload.MeetingId);
            if (existingSummary == null)
            {
                db.Summaries.Add(new FirmMeetingSummary
                {
                    MeetingId = payload.MeetingId,
                    SummaryText = payload.Summary.SummaryText,
                    ActionItemsJson = payload.Summary.ActionItemsJson,
                    KeyDecisionsJson = payload.Summary.KeyDecisionsJson,
                    FollowUpsJson = payload.Summary.FollowUpsJson,
                    ModelUsed = payload.Summary.ModelUsed,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                existingSummary.SummaryText = payload.Summary.SummaryText;
                existingSummary.ActionItemsJson = payload.Summary.ActionItemsJson;
                existingSummary.KeyDecisionsJson = payload.Summary.KeyDecisionsJson;
                existingSummary.FollowUpsJson = payload.Summary.FollowUpsJson;
                existingSummary.ModelUsed = payload.Summary.ModelUsed;
            }
            await db.SaveChangesAsync();
        }

        // Fire-and-forget: notify FAIT so users with auto-add enabled get content pushed to KB
        if (meetingStatus == MeetingStatus.Complete)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Resolve the entra OID from the meeting's creator
                    await using var notifyDb = await _dbFactory.CreateDbContextAsync();
                    var completedMeeting = await notifyDb.Meetings
                        .Include(m => m.CreatedByUser)
                        .FirstOrDefaultAsync(m => m.Id == payload.MeetingId);
                    if (completedMeeting?.CreatedByUser == null) return;

                    var faitApiUrl = _config["FIP:FaitApiUrl"] ?? "https://fait.dev.fortressam.ai";
                    var sharedSecret = _config["Firm:SharedSecret"] ?? "";

                    // Assemble transcript text
                    var segments = await notifyDb.Transcripts
                        .Where(t => t.MeetingId == payload.MeetingId)
                        .OrderBy(t => t.StartTimeMs)
                        .ToListAsync();
                    var transcriptSb = new StringBuilder();
                    foreach (var seg in segments)
                    {
                        var speaker = seg.SpeakerName ?? seg.SpeakerLabel ?? "Unknown";
                        var ts = seg.StartTimeMs.HasValue
                            ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss")
                            : "00:00:00";
                        transcriptSb.AppendLine($"[{ts}] {speaker}: {seg.Text}");
                    }

                    // Assemble summary text
                    var summaryRecord = await notifyDb.Summaries.FirstOrDefaultAsync(s => s.MeetingId == payload.MeetingId);
                    var summaryText = summaryRecord?.SummaryText ?? "";

                    var notifyBody = JsonSerializer.Serialize(new
                    {
                        entraOid = completedMeeting.CreatedByUser.EntraOid,
                        meetingId = payload.MeetingId,
                        transcriptText = transcriptSb.ToString(),
                        summaryText = summaryText
                    });

                    var httpClient = _httpClientFactory.CreateClient();
                    var req = new HttpRequestMessage(HttpMethod.Post, $"{faitApiUrl}/api/firm/meeting-complete");
                    req.Content = new StringContent(notifyBody, System.Text.Encoding.UTF8, "application/json");
                    if (!string.IsNullOrEmpty(sharedSecret))
                        req.Headers.Add("X-Firm-Secret", sharedSecret);
                    var response = await httpClient.SendAsync(req);
                    _logger.LogInformation("FIRM: FAIT meeting-complete notification sent, status: {Status}", response.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FIRM: Failed to notify FAIT of meeting completion (non-fatal)");
                }
            });
        }

        return Ok();
    }

    [HttpGet("/api/meetings/{id}/transcript/download")]
    [Authorize]
    public async Task<IActionResult> DownloadTranscript(long id)
    {
        var (meeting, error) = await ResolveOwnedMeeting(id);
        if (error != null) return error;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Try S3 key first, fall back to DB rows
        if (!string.IsNullOrEmpty(meeting!.TranscriptS3Key))
        {
            var text = await _s3Service.GetTranscriptTextAsync(meeting.TranscriptS3Key);
            if (!string.IsNullOrEmpty(text))
                return File(Encoding.UTF8.GetBytes(text), "text/plain", $"transcript-{id}.txt");
        }

        var segments = await db.Transcripts
            .Where(t => t.MeetingId == id)
            .OrderBy(t => t.StartTimeMs)
            .ToListAsync();

        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            var speaker = seg.SpeakerName ?? seg.SpeakerLabel ?? "Unknown";
            var ts = seg.StartTimeMs.HasValue ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss") : "00:00:00";
            sb.AppendLine($"[{ts}] {speaker}: {seg.Text}");
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/plain", $"transcript-{id}.txt");
    }

    [HttpGet("/api/meetings/{id}/summary/download")]
    [Authorize]
    public async Task<IActionResult> DownloadSummary(long id)
    {
        var (meeting, error) = await ResolveOwnedMeeting(id);
        if (error != null) return error;

        if (!string.IsNullOrEmpty(meeting!.TranscriptS3Key))
        {
            // Try S3 summary key (same prefix, different filename)
            var summaryKey = meeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
            var text = await _s3Service.GetSummaryTextAsync(summaryKey);
            if (!string.IsNullOrEmpty(text))
                return File(Encoding.UTF8.GetBytes(text), "text/plain", $"summary-{id}.txt");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == id);
        if (summary == null) return NotFound(new { error = "Summary not available" });

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(summary.SummaryText)) sb.AppendLine(summary.SummaryText).AppendLine();
        if (!string.IsNullOrEmpty(summary.KeyDecisionsJson))
        {
            sb.AppendLine("KEY DECISIONS:");
            var decisions = JsonSerializer.Deserialize<List<string>>(summary.KeyDecisionsJson);
            decisions?.ForEach(d => sb.AppendLine($"  - {d}"));
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(summary.ActionItemsJson))
        {
            sb.AppendLine("ACTION ITEMS:");
            var items = JsonSerializer.Deserialize<List<ActionItem>>(summary.ActionItemsJson);
            items?.ForEach(i => sb.AppendLine($"  - [{i.Owner}] {i.Description}"));
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(summary.FollowUpsJson))
        {
            sb.AppendLine("FOLLOW-UPS:");
            var followUps = JsonSerializer.Deserialize<List<string>>(summary.FollowUpsJson);
            followUps?.ForEach(f => sb.AppendLine($"  - {f}"));
        }
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/plain", $"summary-{id}.txt");
    }

    [HttpGet("/api/meetings/{id}/audio")]
    [Authorize]
    public async Task<IActionResult> GetAudio(long id)
    {
        var (meeting, error) = await ResolveOwnedMeeting(id);
        if (error != null) return error;
        if (string.IsNullOrEmpty(meeting!.AudioS3Key))
            return NotFound(new { error = "Audio not available" });

        var url = await _s3Service.GeneratePresignedUrlAsync(meeting.AudioS3Key, expiryHours: 1);
        return Redirect(url);
    }

    [Obsolete("Use /push-to-kb instead")]
    [HttpPost("/api/meetings/{id}/push-transcript-to-kb")]
    [Authorize]
    public async Task<IActionResult> PushTranscriptToKb(long id)
    {
        var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
        if (error != null) return error;

        if (meeting!.Status != MeetingStatus.Complete)
            return BadRequest(new { error = "Meeting is not complete" });

        if (string.IsNullOrEmpty(user!.FaitUserId))
            return BadRequest(new { error = "FAIT user ID not linked. Please log out and back in." });

        try
        {
            await _firmKbService.PushTranscriptAsync(id, user.Id, user.FaitUserId);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to push transcript for meeting {Id}", id);
            return StatusCode(500, new { error = "Failed to push transcript to KB" });
        }
    }

    [Obsolete("Use /push-to-kb instead")]
    [HttpPost("/api/meetings/{id}/push-summary-to-kb")]
    [Authorize]
    public async Task<IActionResult> PushSummaryToKb(long id)
    {
        var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
        if (error != null) return error;

        if (meeting!.Status != MeetingStatus.Complete)
            return BadRequest(new { error = "Meeting is not complete" });

        if (string.IsNullOrEmpty(user!.FaitUserId))
            return BadRequest(new { error = "FAIT user ID not linked. Please log out and back in." });

        try
        {
            await _firmKbService.PushSummaryAsync(id, user.Id, user.FaitUserId);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to push summary for meeting {Id}", id);
            return StatusCode(500, new { error = "Failed to push summary to KB" });
        }
    }

    [HttpPost("/api/meetings/{id}/push-to-kb")]
    [Authorize]
    public async Task<IActionResult> PushToKb(long id, [FromBody] PushToKbRequest request)
    {
        var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
        if (error != null) return error;

        if (meeting!.Status != MeetingStatus.Complete)
            return BadRequest(new { error = "Meeting is not complete" });

        if (string.IsNullOrEmpty(user!.FaitUserId))
            return BadRequest(new { error = "FAIT user ID not linked. Please sign out and back in." });

        if (string.IsNullOrEmpty(request.DocType) || !new[] { "transcript", "summary" }.Contains(request.DocType))
            return BadRequest(new { error = "docType must be 'transcript' or 'summary'" });

        if (request.KbScopes == null || !request.KbScopes.Any())
            return BadRequest(new { error = "At least one KB scope required" });

        var validScopes = request.KbScopes.Where(s => new[] { "personal", "team" }.Contains(s)).ToList();
        if (!validScopes.Any())
            return BadRequest(new { error = "Valid scopes: 'personal', 'team'" });

        try
        {
            await _firmKbService.PushDocumentAsync(id, user.Id, user.FaitUserId, request.DocType, validScopes);
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to push {DocType} to KB for meeting {Id}", request.DocType, id);
            return StatusCode(500, new { error = "Failed to push to KB" });
        }
    }

    [HttpGet("/api/meetings/{id}/kb-status")]
    [Authorize]
    public async Task<IActionResult> GetKbStatus(long id)
    {
        var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
        if (error != null) return error;

        var transcriptScopes = await _firmKbService.GetPushedScopesAsync(id, "transcript");
        var summaryScopes    = await _firmKbService.GetPushedScopesAsync(id, "summary");

        return Ok(new {
            transcript = transcriptScopes,
            summary    = summaryScopes,
        });
    }

    public record PushToKbRequest(string DocType, List<string> KbScopes);

    private async Task<(FirmMeeting? meeting, IActionResult? error)> ResolveOwnedMeeting(long id)
    {
        var (meeting, _, error) = await ResolveOwnedMeetingWithUser(id);
        return (meeting, error);
    }

    private async Task<(FirmMeeting? meeting, FirmUser? user, IActionResult? error)> ResolveOwnedMeetingWithUser(long id)
    {
        var entraOid = User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(entraOid)) return (null, null, Unauthorized());

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
        if (user == null) return (null, null, Unauthorized());

        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == id && m.CreatedBy == user.Id);
        if (meeting == null) return (null, null, NotFound(new { error = "Meeting not found" }));

        return (meeting, user, null);
    }

    public record JoinRequest(string MeetingUrl, string? Title);
}

public class VpCallbackPayload
{
    public long MeetingId { get; set; }
    public string Status { get; set; } = "";
    public List<ParticipantPayload>? Participants { get; set; }
    public string? AudioS3Key { get; set; }
    public string? TranscriptS3Key { get; set; }
    public string? Error { get; set; }
    public List<TranscriptSegmentPayload>? Segments { get; set; }
    public SummaryPayload? Summary { get; set; }
}

public class ParticipantPayload
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public DateTime? JoinedAt { get; set; }
}

public class TranscriptSegmentPayload
{
    public string? SpeakerLabel { get; set; }
    public string? SpeakerName { get; set; }
    public string? Text { get; set; }
    public long? StartTimeMs { get; set; }
    public long? EndTimeMs { get; set; }
}

public class SummaryPayload
{
    public string? SummaryText { get; set; }
    public string? ActionItemsJson { get; set; }
    public string? KeyDecisionsJson { get; set; }
    public string? FollowUpsJson { get; set; }
    public string? ModelUsed { get; set; }
}

public class ActionItem
{
    public string? Description { get; set; }
    public string? Owner { get; set; }
}
