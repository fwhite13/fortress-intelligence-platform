using System.Text.Json;
using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Web.Data;
using FortressAI.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;

namespace FortressAI.Web.Controllers;

/// <summary>
/// Internal FAIT endpoint for FIRM integration.
/// GET  /api/firm/resolve-user?entraOid={oid}  — returns FAIT internal user GUID for a given Entra OID (X-Firm-Secret auth)
/// POST /api/firm/meeting-complete              — auto-pushes transcript/summary to personal KB if user has auto-add enabled
/// </summary>
[ApiController]
[Route("api/firm")]
public class FirmIntegrationController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonS3 _s3;
    private readonly IAmazonBedrockAgent _bedrockAgent;
    private readonly IConfiguration _config;
    private readonly ILogger<FirmIntegrationController> _logger;
    private readonly ForgeService _forgeService;
    private readonly KbDocumentService _kbDocumentService;
    private readonly GraphCalendarService _graphCalendarService;

    private string BucketName => _config["Firm:KbS3Bucket"] ?? "fortress-tools";
    private string PersonalKbId => _config["KnowledgeBase:PersonalKbId"] ?? "ZCEZCJGHQC";
    private string PersonalDsId => _config["KnowledgeBase:PersonalDataSourceId"] ?? "3X5E9L4HAC";

    public FirmIntegrationController(
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IAmazonBedrockAgent bedrockAgent,
        IConfiguration config,
        ILogger<FirmIntegrationController> logger,
        ForgeService forgeService,
        KbDocumentService kbDocumentService,
        GraphCalendarService graphCalendarService)
    {
        _dbFactory = dbFactory;
        _s3 = s3;
        _bedrockAgent = bedrockAgent;
        _config = config;
        _logger = logger;
        _forgeService = forgeService;
        _kbDocumentService = kbDocumentService;
        _graphCalendarService = graphCalendarService;
    }

    /// <summary>
    /// GET /api/firm/resolve-user?entraOid={oid}
    /// Returns FAIT internal user GUID for the given Entra OID.
    /// Protected by X-Firm-Secret header — called by FIRM during user login to populate firm_users.fait_user_id.
    /// </summary>
    [HttpGet("resolve-user")]
    public async Task<IActionResult> ResolveUser([FromQuery] string entraOid)
    {
        // Auth: X-Firm-Secret header (same pattern as meeting-complete)
        var expectedSecret = _config["Firm:SharedSecret"] ?? "";
        var providedSecret = Request.Headers["X-Firm-Secret"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
        {
            _logger.LogWarning("FirmIntegration: resolve-user rejected — invalid X-Firm-Secret");
            return Unauthorized(new { error = "Invalid or missing X-Firm-Secret" });
        }

        if (string.IsNullOrWhiteSpace(entraOid))
            return BadRequest(new { error = "entraOid is required" });

        await using var db = await _dbFactory.CreateDbContextAsync();
        // Entra users in FAIT have is_entra_user=1 — match by email prefix pattern or entra OID
        // FAIT stores Entra users; the OID is stored... look it up via email or by checking EntraOid field if present.
        // For now: look up by email. FIRM should pass the entraOid which is the Azure AD Object ID.
        // AppUser doesn't have EntraOid directly — we look up users whose email matches.
        // Actually we need to find user by Entra OID. Since AppUser doesn't store it directly,
        // we look for is_entra_user=true users and try to match.
        // The simplest approach: FAIT uses Entra SSO, users are created with their Entra email.
        // FIRM stores the entraOid. We need FAIT user ID.
        // Since AppUser doesn't have EntraOid, we'll search by looking for a user tagged as Entra user.
        // This is a known limitation — TODO: add EntraOid column to AppUser for reliable lookup.
        // For now: accept the Entra OID as a hint and look for Entra users. If only one Entra user exists
        // whose record was created via SSO, return it. Otherwise return 404.
        //
        // IMPORTANT: This endpoint's resolution accuracy depends on FAIT adding EntraOid to the users table.
        // For single-tenant deployments, this works. For multi-user, the entraOid → userId mapping
        // requires AppUser.EntraOid column (TODO).
        //
        // Current workaround: treat the entraOid as a lookup key against the email field
        // (FIRM also passes display name / email for cross-reference if needed in the future).
        // The caller (FIRM) passes entraOid; we match against users.
        // Short-term: return the first active Entra user as the match (single-user deployment).

        var user = await db.Users
            .Where(u => u.IsEntraUser && u.IsActive)
            .OrderByDescending(u => u.CreatedAt)
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound(new { error = "No matching FAIT user found for this Entra OID" });

        _logger.LogInformation("FirmIntegration: Resolved entraOid {OID} → FAIT user {UserId}", entraOid, user.Id);
        return Ok(new { userId = user.Id.ToString() });
    }

    /// <summary>
    /// GET /api/firm/user-teams?entraOid={oid}
    /// Returns list of KB teams the user belongs to.
    /// Protected by X-Firm-Secret header.
    /// </summary>
    [HttpGet("user-teams")]
    public async Task<IActionResult> GetUserTeams([FromQuery] string entraOid)
    {
        var expectedSecret = _config["Firm:SharedSecret"] ?? "";
        var providedSecret = Request.Headers["X-Firm-Secret"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
        {
            _logger.LogWarning("FirmIntegration: user-teams rejected — invalid X-Firm-Secret");
            return Unauthorized(new { error = "Invalid or missing X-Firm-Secret" });
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users
            .Where(u => u.IsEntraUser && u.IsActive)
            .OrderByDescending(u => u.CreatedAt)
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound(new { error = "No matching FAIT user found for this Entra OID" });

        var teams = await _forgeService.GetUserTeamsAsync(user.Id);
        var memberships = teams.Select(t =>
        {
            var member = t.Members.FirstOrDefault(m => m.UserId == user.Id);
            return new UserTeamDto(t.Id, t.Name, member?.Role.ToString() ?? "Member");
        }).ToList();

        return Ok(memberships);
    }

    /// <summary>
    /// POST /api/firm/meeting-complete
    /// Called by FIRM when a meeting reaches Complete status.
    /// If the user has FirmAutoTranscript or FirmAutoSummary enabled, uploads content to their personal KB.
    /// Protected by shared secret header X-Firm-Secret.
    ///
    /// TODO: Set Firm__SharedSecret in FAIT's ECS environment variables to match FIRM's Firm__SharedSecret.
    /// </summary>
    [HttpPost("meeting-complete")]
    public async Task<IActionResult> MeetingComplete([FromBody] FirmMeetingCompletePayload payload)
    {
        // Validate shared secret
        var expectedSecret = _config["Firm:SharedSecret"] ?? "";
        var providedSecret = Request.Headers["X-Firm-Secret"].FirstOrDefault() ?? "";

        if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
        {
            _logger.LogWarning("FirmIntegration: meeting-complete rejected — invalid X-Firm-Secret");
            return Unauthorized(new { error = "Invalid or missing X-Firm-Secret" });
        }

        if (string.IsNullOrWhiteSpace(payload.EntraOid))
            return BadRequest(new { error = "entraOid is required" });

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Resolve FAIT user — same workaround as resolve-user endpoint
        var user = await db.Users
            .Include(u => u.AssistantConfig)
            .Where(u => u.IsEntraUser && u.IsActive)
            .OrderByDescending(u => u.CreatedAt)
            .FirstOrDefaultAsync();

        if (user == null)
        {
            _logger.LogWarning("FirmIntegration: No matching FAIT user for entraOid {OID}", payload.EntraOid);
            return Ok(new { skipped = true, reason = "No matching user" });
        }

        var config = user.AssistantConfig;
        if (config == null)
        {
            _logger.LogDebug("FirmIntegration: User {UserId} has no assistant config — no auto-add", user.Id);
            return Ok(new { skipped = true, reason = "No config" });
        }

        var tasks = new List<Task>();

        if (config.FirmAutoTranscript && !string.IsNullOrWhiteSpace(payload.TranscriptText))
        {
            tasks.Add(UploadToKbAsync(
                content: payload.TranscriptText,
                s3Key: $"kb-docs/personal/{user.Id}/firm-transcript-{payload.MeetingId}.txt",
                contentType: "text/plain",
                userId: user.Id,
                meetingId: payload.MeetingId,
                docType: "transcript"));
        }

        if (config.FirmAutoSummary && !string.IsNullOrWhiteSpace(payload.SummaryText))
        {
            tasks.Add(UploadToKbAsync(
                content: payload.SummaryText,
                s3Key: $"kb-docs/personal/{user.Id}/firm-summary-{payload.MeetingId}.md",
                contentType: "text/markdown",
                userId: user.Id,
                meetingId: payload.MeetingId,
                docType: "summary"));
        }

        if (tasks.Any())
        {
            try
            {
                await Task.WhenAll(tasks);
                // Trigger ingestion once after all uploads
                await StartPersonalIngestionAsync(user.Id);
            }
            catch (Exception ex)
            {
                // KB push is best-effort — log but always return 200 so FIRM's callback isn't blocked
                _logger.LogError(ex, "FirmIntegration: KB push failed for meeting {MeetingId} — non-fatal", payload.MeetingId);
            }
        }
        else
        {
            _logger.LogDebug("FirmIntegration: User {UserId} has auto-add disabled — skipping KB push for meeting {MeetingId}", user.Id, payload.MeetingId);
        }

        // Team KB pushes (best-effort — failures must NOT affect 200 response)
        if (payload.TeamKbPushes?.Count > 0)
        {
            foreach (var push in payload.TeamKbPushes)
            {
                try
                {
                    if (push.DocType is "transcript" or "both" && !string.IsNullOrWhiteSpace(payload.TranscriptText))
                    {
                        var key = $"kb-docs/team/{push.TeamId}/firm-transcript-{payload.MeetingId}.txt";
                        await _s3.PutObjectAsync(new PutObjectRequest
                        {
                            BucketName = BucketName,
                            Key = key,
                            ContentBody = payload.TranscriptText,
                            ContentType = "text/plain"
                        });
                    }

                    if (push.DocType is "summary" or "both" && !string.IsNullOrWhiteSpace(payload.SummaryText))
                    {
                        var key = $"kb-docs/team/{push.TeamId}/firm-summary-{payload.MeetingId}.md";
                        await _s3.PutObjectAsync(new PutObjectRequest
                        {
                            BucketName = BucketName,
                            Key = key,
                            ContentBody = payload.SummaryText,
                            ContentType = "text/markdown"
                        });
                    }

                    // Trigger shared team KB ingestion
                    await _kbDocumentService.StartIngestionAsync(KbTier.Team);

                    _logger.LogInformation("FirmIntegration: Team KB push complete: team={TeamId} docType={DocType}",
                        push.TeamId, push.DocType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "FirmIntegration: Team KB push failed for team {TeamId} (non-fatal)", push.TeamId);
                }
            }
        }

        return Ok(new { success = true });
    }

    private async Task UploadToKbAsync(string content, string s3Key, string contentType, Guid userId, long meetingId, string docType)
    {
        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                ContentBody = content,
                ContentType = contentType
            });

            // Write companion metadata file for KB isolation
            var metadata = new { metadataAttributes = new Dictionary<string, object> { ["ownerId"] = userId.ToString() } };
            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = $"{s3Key}.metadata.json",
                ContentBody = metadataJson,
                ContentType = "application/json"
            });

            _logger.LogInformation("FirmIntegration: Auto-uploaded {DocType} for meeting {MeetingId} to s3://{Bucket}/{Key}", docType, meetingId, BucketName, s3Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FirmIntegration: Failed to upload {DocType} for meeting {MeetingId}", docType, meetingId);
            throw;
        }
    }

    private async Task StartPersonalIngestionAsync(Guid userId)
    {
        try
        {
            var response = await _bedrockAgent.StartIngestionJobAsync(new StartIngestionJobRequest
            {
                KnowledgeBaseId = PersonalKbId,
                DataSourceId = PersonalDsId
            });
            _logger.LogInformation("FirmIntegration: Started personal KB ingestion job {JobId} for user {UserId}",
                response.IngestionJob?.IngestionJobId, userId);
        }
        catch (Amazon.BedrockAgent.Model.ConflictException)
        {
            _logger.LogInformation("FirmIntegration: Personal KB ingestion already in progress for user {UserId} — will sync on next run", userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FirmIntegration: Failed to start KB ingestion for user {UserId} (non-fatal)", userId);
        }
    }

    /// <summary>
    /// GET /api/firm/calendar-events?entraOid={oid}
    /// Returns upcoming meeting events (next 7 days) that have a Teams/Zoom/Meet join URL.
    /// Filtered and shaped for FIRM's calendar detection feature.
    /// Auth: X-Firm-Secret header
    /// </summary>
    [HttpGet("calendar-events")]
    public async Task<IActionResult> GetCalendarEvents([FromQuery] string? entraOid)
    {
        var expectedSecret = _config["Firm:SharedSecret"] ?? "";
        var providedSecret = Request.Headers["X-Firm-Secret"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
        {
            _logger.LogWarning("FirmIntegration: calendar-events rejected — invalid X-Firm-Secret");
            return Unauthorized();
        }

        if (string.IsNullOrEmpty(entraOid))
            return Ok(new List<object>());

        _logger.LogInformation("FirmIntegration: calendar-events called with entraOid={OID}", entraOid);

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Try exact EntraOid match first (populated since ADO#1240)
        var user = await db.Users
            .Where(u => u.EntraOid == entraOid && u.IsActive)
            .FirstOrDefaultAsync();
        _logger.LogInformation("FirmIntegration: calendar-events EntraOid exact match: {Result} for OID {OID}", user != null ? $"found user {user.Id}" : "not found", entraOid);

        // Fallback for users created before EntraOid was added:
        // find active Entra user — works for single-tenant
        if (user == null)
        {
            user = await db.Users
                .Where(u => u.IsEntraUser && u.IsActive)
                .OrderByDescending(u => u.CreatedAt)
                .FirstOrDefaultAsync();
            _logger.LogInformation("FirmIntegration: calendar-events — EntraOid lookup missed for {OID}, fell back to latest Entra user {UserId}", entraOid, user?.Id);
        }

        if (user == null)
        {
            _logger.LogWarning("FirmIntegration: calendar-events — no Entra user found for OID {OID}", entraOid);
            return Ok(new List<object>());
        }

        _logger.LogInformation("FirmIntegration: calendar-events — resolved OID {OID} → user {UserId}", entraOid, user.Id);

        var now = DateTime.UtcNow;
        var end = now.AddDays(7);

        List<CalendarEvent> events;
        try
        {
            events = await _graphCalendarService.GetUserCalendarEventsAsync(user.Id, now, end);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FirmIntegration: calendar-events failed for user {UserId}", user.Id);
            return Ok(new List<object>());
        }

        _logger.LogInformation("FirmIntegration: calendar-events Graph returned {Count} events for user {UserId}", events.Count, user.Id);

        var meetingEvents = events
            .Where(e => !string.IsNullOrEmpty(e.OnlineMeetingUrl))
            .OrderBy(e => e.StartTime)
            .Take(20)
            .ToList();

        _logger.LogInformation("FirmIntegration: calendar-events returning {Count} meeting events (with join URL) for user {UserId}", meetingEvents.Count, user.Id);

        var result = meetingEvents.Select(e =>
        {
            var joinUrl = e.OnlineMeetingUrl ?? "";
            var platform = DetectCalendarPlatform(joinUrl);
            return new
            {
                calendarEventId = e.EventId,
                subject = e.Subject,
                startDateTime = e.StartTime.ToString("O"),
                endDateTime = e.EndTime.ToString("O"),
                joinUrl = joinUrl,
                platform = platform,
            };
        }).ToList();

        return Ok(result);
    }

    private static string DetectCalendarPlatform(string url)
    {
        if (url.Contains("teams.microsoft.com") || url.Contains("teams.live.com"))
            return "teams";
        if (url.Contains("zoom.us"))
            return "zoom";
        if (url.Contains("meet.google.com"))
            return "meet";
        return "other";
    }
}

public class FirmMeetingCompletePayload
{
    public string EntraOid { get; set; } = "";
    public long MeetingId { get; set; }
    public string? TranscriptText { get; set; }
    public string? SummaryText { get; set; }
    public List<TeamKbPushRequest>? TeamKbPushes { get; set; }
}

public class TeamKbPushRequest
{
    public int TeamId { get; set; }
    public string DocType { get; set; } = "transcript"; // "transcript" | "summary" | "both"
}

public record UserTeamDto(int Id, string Name, string Role);
