using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using FortressIntelligenceRM.Web.Services;
using Markdig;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FortressIntelligenceRM.Web.Controllers;

[ApiController]
[Route("api/meetings")]
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
    private readonly IFirmBotService _firmBotService;
    private readonly TeamsGraphService _teamsGraphService;
    private readonly IOrgContextService _orgContextService;
    private readonly IMindmapService _mindmapService;

    public MeetingsApiController(
        MeetingService meetingService,
        VpBotService vpBotService,
        S3Service s3Service,
        FirmKbService firmKbService,
        IDbContextFactory<FirmDbContext> dbFactory,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<MeetingsApiController> logger,
        IFirmBotService firmBotService,
        TeamsGraphService teamsGraphService,
        IOrgContextService orgContextService,
        IMindmapService mindmapService)
    {
        _meetingService = meetingService;
        _vpBotService = vpBotService;
        _s3Service = s3Service;
        _firmKbService = firmKbService;
        _dbFactory = dbFactory;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _firmBotService = firmBotService;
        _teamsGraphService = teamsGraphService;
        _orgContextService = orgContextService;
        _mindmapService = mindmapService;
    }

[HttpPost("/api/meetings/join")]
    [Authorize]
    public async Task<IActionResult> JoinMeeting([FromBody] JoinRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.MeetingUrl))
            return BadRequest(new { error = "MeetingUrl is required" });

        // Get Entra OID from claims
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("preferred_username")?.Value ?? "";
        var displayName = User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value ?? email;

        if (string.IsNullOrEmpty(entraOid))
            return Unauthorized();

        var firmUser = await _meetingService.GetOrCreateUserAsync(entraOid, email, displayName);
        if (firmUser == null) return StatusCode(500, new { error = "Failed to resolve user" });

        var meeting = await _meetingService.CreateMeetingAsync(
            firmUser.Id, request.MeetingUrl, request.Title,
            request.StartDatetime, request.CalendarEventId);

        // Scheduling branch: if start is > 5 min in the future, don't dispatch bot
        if (request.StartDatetime.HasValue && request.StartDatetime.Value.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5))
        {
            await _meetingService.UpdateStatusAsync(meeting.Id, MeetingStatus.Scheduled);
            return Ok(new { meetingId = meeting.Id, status = "scheduled" });
        }

        // Trigger bot (fire and forget — don't fail the response if ECS isn't configured)
        // TODO (Mode A): When TeamsGraphService is implemented, detect platform here and route to
        // Mode A (native Teams transcript fetch) vs Mode B (VP bot). Mode A completion must call
        // FAIT /api/firm/meeting-complete the same way as Mode B does in VpCallback. See ADO#1232.
        _ = _vpBotService.TriggerBotAsync(meeting.Id, request.MeetingUrl);

        return Ok(new { meetingId = meeting.Id });
    }

    [HttpPost("/api/vp/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> VpCallback([FromBody] VpCallbackPayload payload)
    {
        _logger.LogInformation("FIRM: VpCallback received — meetingId={MeetingId} status={Status}", payload?.MeetingId, payload?.Status);

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
            ["transcribing"] = MeetingStatus.Transcribing,
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

        // Special case: lobby_timeout is a retriable condition — revert to Scheduled
        // so Fred sees a clear "not admitted" state and can retry, rather than Failed
        try
        {
            if (meetingStatus == MeetingStatus.Failed &&
                string.Equals(payload.Reason, "lobby_timeout", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("FIRM: Meeting {Id} bot stuck in lobby — reverting to Scheduled (lobby_timeout)",
                    payload.MeetingId);
                await _meetingService.UpdateStatusAsync(payload.MeetingId, MeetingStatus.Scheduled,
                    "Bot was not admitted to the meeting lobby. Check the Teams meeting lobby settings and ensure 'Lobby bypass' is set to allow the bot.");
            }
            else
            {
                await _meetingService.UpdateStatusAsync(payload.MeetingId, meetingStatus, payload.Error);
            }
        }
        catch (Exception updateEx)
        {
            _logger.LogWarning(updateEx, "FIRM: UpdateStatusAsync failed for meeting {Id}, retrying in 500ms", payload.MeetingId);
            await Task.Delay(500);
            try
            {
                if (meetingStatus == MeetingStatus.Failed &&
                    string.Equals(payload.Reason, "lobby_timeout", StringComparison.OrdinalIgnoreCase))
                {
                    await _meetingService.UpdateStatusAsync(payload.MeetingId, MeetingStatus.Scheduled,
                        "Bot was not admitted to the meeting lobby. Check the Teams meeting lobby settings and ensure 'Lobby bypass' is set to allow the bot.");
                }
                else
                {
                    await _meetingService.UpdateStatusAsync(payload.MeetingId, meetingStatus, payload.Error);
                }
            }
            catch (Exception retryEx)
            {
                _logger.LogError(retryEx, "FIRM: UpdateStatusAsync retry also failed for meeting {Id} — continuing", payload.MeetingId);
                // Do not rethrow — return Ok() to bot so it doesn't retry callback
            }
        }
        _logger.LogInformation("FIRM: VpCallback processed — meetingId={MeetingId} status={Status} → {MeetingStatus}", payload.MeetingId, payload.Status, meetingStatus);

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Update S3 keys if provided
        var meeting = await db.Meetings.FindAsync(payload.MeetingId);
        if (meeting != null)
        {
            if (!string.IsNullOrEmpty(payload.AudioS3Key)) meeting.AudioS3Key = payload.AudioS3Key;
            if (!string.IsNullOrEmpty(payload.TranscriptS3Key)) meeting.TranscriptS3Key = payload.TranscriptS3Key;
            meeting.UpdatedAt = DateTime.UtcNow;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception s3KeySaveEx)
            {
                _logger.LogWarning(s3KeySaveEx, "FIRM: S3 key save failed for meeting {Id} — retrying once", payload.MeetingId);
                await Task.Delay(500);
                try
                {
                    // Retry on same DbContext — reliable for transient connection errors only.
                    // Constraint violations or concurrency conflicts will re-throw on retry.
                    await db.SaveChangesAsync();
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "FIRM: S3 key save retry also failed for meeting {Id} — continuing", payload.MeetingId);
                    // Do not rethrow — return Ok() to bot so it doesn't retry callback
                }
            }
        }

        // ADO#2179: When recording_complete arrives, firm-web submits the Batch transcription job.
        // UpdateStatusAsync above already stamped EndedAt/DurationSeconds (status → Transcribing).
        // AudioS3Key was just persisted above — SubmitTranscriptionJobAsync will read it from DB.
        if (payload.Status.Equals("recording_complete", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var batchJobId = await _meetingService.SubmitTranscriptionJobAsync(payload.MeetingId);
                _logger.LogInformation("FIRM: VpCallback submitted Batch job {JobId} for meeting {MeetingId} on recording_complete", batchJobId, payload.MeetingId);
            }
            catch (Exception batchEx)
            {
                _logger.LogError(batchEx, "FIRM: VpCallback failed to submit Batch job for meeting {MeetingId} — meeting is Transcribing but no Batch job running", payload.MeetingId);
                // Do not rethrow — return Ok() so bot doesn't retry the callback endlessly.
                // The retranscribe endpoint can be used manually to recover.
            }
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
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception participantSaveEx)
            {
                _logger.LogWarning(participantSaveEx, "FIRM: Participant save failed for meeting {Id} — retrying once", payload.MeetingId);
                await Task.Delay(500);
                try
                {
                    // Retry on same DbContext — reliable for transient connection errors only.
                    // Constraint violations or concurrency conflicts will re-throw on retry.
                    await db.SaveChangesAsync();
                }
                catch (Exception retryEx)
                {
                    _logger.LogError(retryEx, "FIRM: Participant save retry also failed for meeting {Id} — continuing", payload.MeetingId);
                    // Do not rethrow — return Ok() to bot so it doesn't retry callback
                }
            }
        }

        // Write transcript on transcription_complete
        if (meetingStatus == MeetingStatus.Summarizing && payload.Segments != null)
        {
            // Clear existing transcript rows for this meeting before inserting new segments
            var existingTranscripts = db.Transcripts.Where(t => t.MeetingId == payload.MeetingId);
            db.Transcripts.RemoveRange(existingTranscripts);

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
                    OpenQuestionsJson = payload.Summary.OpenQuestionsJson,
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
                existingSummary.OpenQuestionsJson = payload.Summary.OpenQuestionsJson;
                existingSummary.ModelUsed = payload.Summary.ModelUsed;
            }
            await db.SaveChangesAsync();

            // Write summary to S3 so DownloadSummary and KB push can find it
            // Key convention mirrors DownloadSummary: TranscriptS3Key with "transcript.json" → "summary.md"
            // Guard: only derive key if "transcript.json" is actually in the key (Replace is a no-op otherwise)
            if (!string.IsNullOrEmpty(payload.Summary.SummaryText) &&
                meeting != null &&
                !string.IsNullOrEmpty(meeting.TranscriptS3Key) &&
                meeting.TranscriptS3Key.Contains("transcript.json"))
            {
                var summaryS3Key = meeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
                try
                {
                    await _s3Service.UploadTextAsync(summaryS3Key, payload.Summary.SummaryText, "text/markdown");
                    _logger.LogInformation("FIRM: Summary written to S3 for meeting {Id}: {Key}", payload.MeetingId, summaryS3Key);
                }
                catch (Exception s3Ex)
                {
                    // Non-fatal: summary is already in DB; S3 write failure should not fail the callback
                    _logger.LogWarning(s3Ex, "FIRM: Failed to write summary to S3 for meeting {Id} (non-fatal, summary is in DB)", payload.MeetingId);
                }
            }
            else if (!string.IsNullOrEmpty(payload.Summary?.SummaryText) && meeting != null)
            {
                _logger.LogWarning("FIRM: Cannot derive summary S3 key — TranscriptS3Key does not contain 'transcript.json': {Key}", meeting.TranscriptS3Key);
            }
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

        // Fire-and-forget: generate mind map after summary is written
        if (meetingStatus == MeetingStatus.Complete)
        {
            _ = _mindmapService.GenerateAsync(payload.MeetingId);
        }

        // Fire-and-forget: send Expo push notification to mobile user if token registered
        if (meetingStatus == MeetingStatus.Complete)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await using var pushDb = await _dbFactory.CreateDbContextAsync();
                    var pushMeeting = await pushDb.Meetings
                        .Include(m => m.CreatedByUser)
                        .FirstOrDefaultAsync(m => m.Id == payload.MeetingId);
                    var pushToken = pushMeeting?.CreatedByUser?.ExpoPushToken;
                    if (string.IsNullOrEmpty(pushToken)) return;

                    var pushTitle = pushMeeting?.Title ?? "Recording complete";
                    var pushBody = new
                    {
                        to = pushToken,
                        title = "FIRM — Recording Ready",
                        body = $"Transcript and summary ready: {pushTitle}",
                        data = new { meetingId = payload.MeetingId }
                    };
                    var httpClient = _httpClientFactory.CreateClient();
                    var req = new HttpRequestMessage(HttpMethod.Post, "https://exp.host/--/api/v2/push/send");
                    req.Content = new StringContent(JsonSerializer.Serialize(pushBody), System.Text.Encoding.UTF8, "application/json");
                    await httpClient.SendAsync(req);
                    _logger.LogInformation("FIRM: Expo push notification sent for meeting {MeetingId}", payload.MeetingId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "FIRM: Expo push notification failed for meeting {MeetingId} (non-fatal)", payload.MeetingId);
                }
            });
        }

        return Ok();
    }

    [HttpGet("/api/vp/org-context")]
    [AllowAnonymous]
    public async Task<IActionResult> VpGetOrgContext()
    {
        var expectedSecret = _config["Firm:BotCallbackSecret"];
        var providedSecret = Request.Headers["X-Bot-Secret"].FirstOrDefault();
        if (string.IsNullOrEmpty(expectedSecret) || providedSecret != expectedSecret)
            return Unauthorized();

        var tenantId = _config["Firm:GraphTenantId"] ?? _config["AzureAd:TenantId"] ?? "";
        if (string.IsNullOrEmpty(tenantId)) return Ok(new { names = Array.Empty<string>() });

        var entries = await _orgContextService.GetContextAsync(tenantId);
        var names = entries.Select(e => $"{e.Term}: {e.Description}").ToList();
        return Ok(new { names });
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
    public async Task<IActionResult> DownloadSummary(long id, [FromQuery] string format = "md")
    {
        var (meeting, error) = await ResolveOwnedMeeting(id);
        if (error != null) return error;

        var slug = Regex.Replace(
            (meeting!.Title ?? $"meeting-{id}").ToLowerInvariant(),
            @"[^a-z0-9]+", "-").Trim('-');

        string markdownText;

        if (!string.IsNullOrEmpty(meeting!.TranscriptS3Key))
        {
            var summaryKey = meeting.TranscriptS3Key.Replace("transcript.json", "summary.md");
            var s3Text = await _s3Service.GetSummaryTextAsync(summaryKey);
            if (!string.IsNullOrEmpty(s3Text))
            {
                markdownText = s3Text;
                if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
                    return BuildPdfResult(markdownText, meeting.Title ?? $"Meeting {id}", $"{slug}-summary.pdf");
                return File(Encoding.UTF8.GetBytes(markdownText), "text/markdown; charset=utf-8", $"{slug}-summary.md");
            }
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == id);
        if (summary == null) return NotFound(new { error = "Summary not available" });

        // Build markdown from stored structured data
        var mdSb = new StringBuilder();
        if (!string.IsNullOrEmpty(summary.SummaryText))
        {
            mdSb.AppendLine(summary.SummaryText);
            mdSb.AppendLine();
        }
        if (!string.IsNullOrEmpty(summary.KeyDecisionsJson))
        {
            try
            {
                var decisions = JsonSerializer.Deserialize<List<string>>(summary.KeyDecisionsJson);
                if (decisions?.Any() == true)
                {
                    mdSb.AppendLine("## Decisions Made");
                    decisions.ForEach(d => mdSb.AppendLine($"- {d}"));
                    mdSb.AppendLine();
                }
            }
            catch { /* Non-fatal */ }
        }
        if (!string.IsNullOrEmpty(summary.ActionItemsJson))
        {
            try
            {
                var items = JsonSerializer.Deserialize<List<ActionItem>>(summary.ActionItemsJson);
                if (items?.Any() == true)
                {
                    mdSb.AppendLine("## Action Items");
                    items.ForEach(i => mdSb.AppendLine($"- **{i.Owner ?? "TBD"}**: {i.Description} _(due: {i.Deadline ?? "TBD"})_"));
                    mdSb.AppendLine();
                }
            }
            catch { /* Non-fatal */ }
        }
        if (!string.IsNullOrEmpty(summary.FollowUpsJson))
        {
            try
            {
                var followUps = JsonSerializer.Deserialize<List<string>>(summary.FollowUpsJson);
                if (followUps?.Any() == true)
                {
                    mdSb.AppendLine("## Follow-ups");
                    followUps.ForEach(f => mdSb.AppendLine($"- {f}"));
                    mdSb.AppendLine();
                }
            }
            catch { /* Non-fatal */ }
        }
        if (!string.IsNullOrEmpty(summary.OpenQuestionsJson))
        {
            try
            {
                var questions = JsonSerializer.Deserialize<List<string>>(summary.OpenQuestionsJson);
                if (questions?.Any() == true)
                {
                    mdSb.AppendLine("## Open Questions");
                    questions.ForEach(q => mdSb.AppendLine($"- {q}"));
                    mdSb.AppendLine();
                }
            }
            catch { /* Non-fatal */ }
        }

        markdownText = mdSb.ToString();

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
            return BuildPdfResult(markdownText, meeting!.Title ?? $"Meeting {id}", $"{slug}-summary.pdf");

        return File(Encoding.UTF8.GetBytes(markdownText), "text/markdown; charset=utf-8", $"{slug}-summary.md");
    }

    /// <summary>
    /// Converts a markdown string to a clean PDF using QuestPDF.
    /// Parses the markdown into block elements (headings, paragraphs, bullets)
    /// and renders them with appropriate typography.
    /// </summary>
    private static FileContentResult BuildPdfResult(string markdown, string title, string filename)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().DisableHtml().Build();
        var doc = Markdig.Markdown.Parse(markdown, pipeline);

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11).FontFamily(Fonts.Arial));

                page.Header().PaddingBottom(8).BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Column(col =>
                {
                    col.Item().Text(title).FontSize(14).Bold().FontColor(Color.FromHex("194C5C"));
                    col.Item().Text($"Generated {DateTime.UtcNow:yyyy-MM-dd}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    foreach (var block in doc)
                    {
                        switch (block)
                        {
                            case Markdig.Syntax.HeadingBlock h:
                            {
                                var text = ExtractInlineText(h.Inline);
                                var (size, bold) = h.Level switch
                                {
                                    1 => (16f, true),
                                    2 => (13f, true),
                                    _ => (11f, true)
                                };
                                col.Item().PaddingTop(h.Level <= 2 ? 14 : 8).PaddingBottom(4)
                                    .Text(text).FontSize(size).Bold().FontColor(Color.FromHex("194C5C"));
                                break;
                            }
                            case Markdig.Syntax.ParagraphBlock p:
                            {
                                col.Item().PaddingBottom(6).Text(txt =>
                                {
                                    RenderInlines(txt, p.Inline);
                                });
                                break;
                            }
                            case Markdig.Syntax.ListBlock list:
                            {
                                foreach (var listItem in list.OfType<Markdig.Syntax.ListItemBlock>())
                                {
                                    foreach (var child in listItem.OfType<Markdig.Syntax.ParagraphBlock>())
                                    {
                                        col.Item().PaddingLeft(16).PaddingBottom(3).Row(row =>
                                        {
                                            row.ConstantItem(12).Text("•").FontColor(Color.FromHex("194C5C"));
                                            row.RelativeItem().Text(txt =>
                                            {
                                                RenderInlines(txt, child.Inline);
                                            });
                                        });
                                    }
                                }
                                col.Item().PaddingBottom(4);
                                break;
                            }
                            case Markdig.Syntax.ThematicBreakBlock:
                                col.Item().PaddingVertical(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                break;
                        }
                    }
                });

                page.Footer().AlignCenter().Text(txt =>
                {
                    txt.Span("Page ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    txt.CurrentPageNumber().FontSize(9).FontColor(Colors.Grey.Darken1);
                    txt.Span(" of ").FontSize(9).FontColor(Colors.Grey.Darken1);
                    txt.TotalPages().FontSize(9).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf();

        return new FileContentResult(pdfBytes, "application/pdf")
        {
            FileDownloadName = filename
        };
    }

    private static string ExtractInlineText(Markdig.Syntax.Inlines.ContainerInline? inlines)
    {
        if (inlines == null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            if (inline is Markdig.Syntax.Inlines.LiteralInline lit) sb.Append(lit.Content.ToString());
            else if (inline is Markdig.Syntax.Inlines.EmphasisInline emp) sb.Append(ExtractInlineText(emp));
            else if (inline is Markdig.Syntax.Inlines.ContainerInline ci) sb.Append(ExtractInlineText(ci));
        }
        return sb.ToString();
    }

    private static void RenderInlines(TextDescriptor txt, Markdig.Syntax.Inlines.ContainerInline? inlines)
    {
        if (inlines == null) return;
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Markdig.Syntax.Inlines.LiteralInline lit:
                    txt.Span(lit.Content.ToString());
                    break;
                case Markdig.Syntax.Inlines.EmphasisInline emp:
                    var empText = ExtractInlineText(emp);
                    if (emp.DelimiterCount == 2) txt.Span(empText).Bold();
                    else txt.Span(empText).Italic();
                    break;
                case Markdig.Syntax.Inlines.LineBreakInline:
                    txt.Span("\n");
                    break;
                case Markdig.Syntax.Inlines.ContainerInline ci:
                    RenderInlines(txt, ci);
                    break;
            }
        }
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
            await _firmKbService.PushTranscriptAsync(id, user.Id.ToString(), user.FaitUserId);
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
            await _firmKbService.PushSummaryAsync(id, user.Id.ToString(), user.FaitUserId);
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
            await _firmKbService.PushDocumentAsync(id, user.Id.ToString(), user.FaitUserId, request.DocType, validScopes);
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

    [HttpPost("{id}/post-to-channel")]
    public async Task<IActionResult> PostToChannel(long id, [FromBody] List<ChannelPostRequest> requests)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == id);
        if (meeting == null) return NotFound();

        var results = new List<object>();
        foreach (var req in requests ?? new List<ChannelPostRequest>())
        {
            try
            {
                string content;
                if (req.DocType == "transcript")
                {
                    var transcriptKey = meeting.TranscriptS3Key ?? "";
                    content = string.IsNullOrEmpty(transcriptKey) ? "" : await _s3Service.GetTranscriptTextAsync(transcriptKey);
                }
                else
                {
                    await using var db2 = await _dbFactory.CreateDbContextAsync();
                    var summary = await db2.Summaries.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(s => s.MeetingId == id);
                    content = summary?.SummaryText ?? "";
                }

                await _firmBotService.PostToChannelAsync(req.TeamId ?? "", req.ChannelId ?? "", content, req.DocType ?? "summary");

                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO firm_meeting_channel_posts (meeting_id, initiated_by, team_id, team_name, channel_id, channel_name, doc_type, success) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, 1)",
                    id, 0L, req.TeamId ?? "", req.TeamName ?? "", req.ChannelId ?? "", req.ChannelName ?? "", req.DocType ?? "summary");

                results.Add(new { teamId = req.TeamId, channelId = req.ChannelId, success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post to channel {ChannelId}", req.ChannelId);
                results.Add(new { teamId = req.TeamId, channelId = req.ChannelId, success = false, error = ex.Message });
            }
        }
        return Ok(results);
    }

    [HttpGet("{id}/channel-post-history")]
    public async Task<IActionResult> GetChannelPostHistory(long id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Database
            .SqlQueryRaw<ChannelPostHistoryRow>(
                "SELECT team_name AS TeamName, channel_name AS ChannelName, doc_type AS DocType, posted_at AS PostedAt, success AS Success FROM firm_meeting_channel_posts WHERE meeting_id = {0} ORDER BY posted_at DESC",
                id)
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("/api/firm/bot-installations")]
    public async Task<IActionResult> GetBotInstallations()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Database
            .SqlQueryRaw<BotInstallRow>("SELECT team_id AS TeamId, channel_id AS ChannelId FROM firm_bot_installations ORDER BY installed_at DESC")
            .ToListAsync();
        return Ok(rows);
    }

    [HttpGet("/api/firm/user-teams-local")]
    public async Task<IActionResult> GetUserTeamsLocal()
    {
        var faitUrl = _config["FIP:FaitApiUrl"] ?? "";
        var secret = _config["Firm:SharedSecret"] ?? "";
        if (string.IsNullOrEmpty(faitUrl)) return Ok(new List<object>());
        try
        {
            var client = _httpClientFactory.CreateClient();
            var oid = User.FindFirstValue("oid")
                ?? User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
                ?? "";
            var req = new HttpRequestMessage(HttpMethod.Get, $"{faitUrl}/api/firm/user-teams?entraOid={oid}");
            req.Headers.Add("X-Firm-Secret", secret);
            var resp = await client.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return Ok(new List<object>());
            var body = await resp.Content.ReadAsStringAsync();
            return Content(body, "application/json");
        }
        catch
        {
            return Ok(new List<object>());
        }
    }

    [HttpPost("{id}/join")]
    [Authorize]
    public async Task<IActionResult> JoinNow(long id)
    {
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

        var email = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("preferred_username")?.Value ?? "";
        var displayName = User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value ?? email;

        var firmUser = await _meetingService.GetOrCreateUserAsync(entraOid, email, displayName);
        if (firmUser == null) return StatusCode(500, new { error = "Failed to resolve user" });

        var meeting = await _meetingService.GetMeetingAsync(id, firmUser.Id);
        if (meeting == null) return NotFound();
        if (meeting.Status != MeetingStatus.Scheduled)
            return Conflict(new { error = "Meeting is not in Scheduled state" });

        // Mode A: Teams — no bot, transition to WaitingTranscript
        if (meeting.Platform == "teams")
        {
            await _meetingService.UpdateStatusAsync(id, MeetingStatus.WaitingTranscript);
            return Ok(new {
                meetingId = id,
                status = "waiting_transcript",
                message = "Mode A meeting — start the Teams meeting when ready. FIRM will capture the transcript automatically."
            });
        }

        // Mode B: Zoom/Meet/other — dispatch bot
        _ = _vpBotService.TriggerBotAsync(id, meeting.MeetingUrl ?? "");
        await _meetingService.UpdateStatusAsync(id, MeetingStatus.Pending);
        return Ok(new { meetingId = id, status = "pending" });
    }

    [HttpPost("/api/vp/stop/{meetingId}")]
    [Authorize]
    public async Task<IActionResult> StopRecording(long meetingId)
    {
        var (meeting, error) = await ResolveOwnedMeeting(meetingId);
        if (error != null) return error;

        if (meeting!.Status != MeetingStatus.Recording)
            return BadRequest(new { error = "Meeting is not currently recording" });

        if (string.IsNullOrEmpty(meeting.BotTaskArn))
            return Ok(new { status = "no_bot", message = "No active bot task found — recording may have already ended" });

        try
        {
            await _vpBotService.StopBotAsync(meeting.BotTaskArn);
            return Ok(new { status = "stop_signal_sent", message = "Stop signal sent to bot. Recording will complete shortly." });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: StopBotAsync failed for meeting {Id} — treating as bot_unreachable", meetingId);
            return Ok(new { status = "bot_unreachable", message = "Bot did not respond. If recording already ended, it will process normally." });
        }
    }

    [HttpDelete("/api/meetings/{id}")]
    [Authorize]
    public async Task<IActionResult> RemoveMeeting(long id)
    {
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

        var email = User.FindFirst(ClaimTypes.Email)?.Value
            ?? User.FindFirst("preferred_username")?.Value ?? "";
        var displayName = User.FindFirst(ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value ?? email;

        var firmUser = await _meetingService.GetOrCreateUserAsync(entraOid, email, displayName);
        if (firmUser == null) return StatusCode(500, new { error = "Failed to resolve user" });

        var meeting = await _meetingService.GetMeetingAsync(id, firmUser.Id);
        if (meeting == null) return NotFound(new { error = "Meeting not found" });

        // Allow removing meetings in any terminal state (Scheduled, Complete, Failed)
        // Reject only if meeting is actively in-progress
        if (meeting.Status is MeetingStatus.Pending or MeetingStatus.Joining or MeetingStatus.Recording
            or MeetingStatus.WaitingTranscript or MeetingStatus.Transcribing or MeetingStatus.Summarizing)
            return Conflict(new { error = "Cannot remove a meeting that is currently in progress" });

        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM firm_meetings WHERE id = {0}", id);
        return NoContent();
    }

    [HttpPost("{id}/reprocess-summary")]
    [Authorize]
    public async Task<IActionResult> ReprocessSummary(long id)
    {
        var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
        if (error != null) return error;

        if (meeting!.Status is not (MeetingStatus.Complete or MeetingStatus.Failed or MeetingStatus.Summarizing))
            return BadRequest(new { error = "Meeting must be in Complete, Failed, or Summarizing state to reprocess" });

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Build transcript text — try S3 first, fall back to DB rows
        string transcriptText;
        if (!string.IsNullOrEmpty(meeting!.TranscriptS3Key))
        {
            var s3Text = await _s3Service.GetTranscriptTextAsync(meeting.TranscriptS3Key);
            if (!string.IsNullOrEmpty(s3Text))
            {
                transcriptText = s3Text;
            }
            else
            {
                transcriptText = await BuildTranscriptFromDbAsync(db, id);
            }
        }
        else
        {
            transcriptText = await BuildTranscriptFromDbAsync(db, id);
        }

        if (string.IsNullOrWhiteSpace(transcriptText))
            return BadRequest(new { error = "No transcript available to summarize" });

        // Inject org context for reprocess summarization
        string? orgWikiContent = null;
        try
        {
            var tenantId = User.FindFirst("tid")?.Value
                ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
                ?? _config["Firm:GraphTenantId"];
            if (!string.IsNullOrEmpty(tenantId))
            {
                var orgEntries = await _orgContextService.GetContextAsync(tenantId);
                orgWikiContent = orgEntries.Count > 0
                    ? string.Join("\n", orgEntries.Select(e => $"{e.Term}: {e.Description}"))
                    : null;
            }
        }
        catch { /* org context is non-critical */ }
        // Call Bedrock summarization
        var summary = await _teamsGraphService.SummarizeAsync(transcriptText, id, orgWikiContent);
        if (summary == null)
            return StatusCode(500, new { error = "Summarization failed" });

        // Upsert summary record
        var existing = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == id);
        if (existing == null)
        {
            db.Summaries.Add(new FirmMeetingSummary
            {
                MeetingId = id,
                SummaryText = summary.SummaryText ?? "",
                ActionItemsJson = summary.ActionItemsJson ?? "[]",
                KeyDecisionsJson = summary.KeyDecisionsJson ?? "[]",
                FollowUpsJson = summary.FollowUpsJson ?? "[]",
                OpenQuestionsJson = summary.OpenQuestionsJson ?? "[]",
                ModelUsed = summary.ModelUsed,
                CreatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.SummaryText = summary.SummaryText ?? "";
            existing.ActionItemsJson = summary.ActionItemsJson ?? "[]";
            existing.KeyDecisionsJson = summary.KeyDecisionsJson ?? "[]";
            existing.FollowUpsJson = summary.FollowUpsJson ?? "[]";
            existing.OpenQuestionsJson = summary.OpenQuestionsJson ?? "[]";
            existing.ModelUsed = summary.ModelUsed;
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: ReprocessSummary save failed for meeting {Id}", id);
            return StatusCode(500, new { error = "Failed to save summary" });
        }

        // Set meeting status to Complete (only runs if save succeeded)
        await _meetingService.UpdateStatusAsync(id, MeetingStatus.Complete);

        _logger.LogInformation("FIRM: ReprocessSummary complete for meeting {Id}", id);
        return Ok(new { meetingId = id, summary = summary.SummaryText });
    }

    /// <summary>
    /// POST /api/meetings/{id}/retranscribe
    /// Triggers the transcription pipeline for a meeting via MeetingService.
    /// </summary>
    [HttpPost("{id}/retranscribe")]
    [Authorize]
    public async Task<IActionResult> Retranscribe(long id)
    {
        var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
        if (error != null) return error;

        var (success, serviceError) = await _meetingService.RetranscribeAsync(id, user!.Id);
        if (!success)
            return serviceError switch
            {
                "Meeting not found or access denied" => NotFound(new { error = serviceError }),
                "No audio recording available for this meeting" => BadRequest(new { error = serviceError }),
                _ => StatusCode(500, new { error = serviceError })
            };

        return Ok(new { status = "retranscribe_triggered", meetingId = id });
    }

    private async Task<string> BuildTranscriptFromDbAsync(FirmDbContext db, long meetingId)
    {
        var segments = await db.Transcripts
            .Where(t => t.MeetingId == meetingId)
            .OrderBy(t => t.StartTimeMs)
            .ToListAsync();
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            var speaker = seg.SpeakerName ?? seg.SpeakerLabel ?? "Unknown";
            var ts = seg.StartTimeMs.HasValue
                ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss")
                : "00:00:00";
            sb.AppendLine($"[{ts}] {speaker}: {seg.Text}");
        }
        return sb.ToString();
    }


    // ── Mind Map Endpoints ────────────────────────────────────────────────────

    [HttpGet("/api/meetings/{id}/mindmap")]
    [Authorize]
    public async Task<IActionResult> GetMindmap(long id)
    {
        var (meeting, error) = await ResolveOwnedMeeting(id);
        if (error != null) return error;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var mindmap = await db.Mindmaps.FirstOrDefaultAsync(m => m.MeetingId == id);
        if (mindmap == null)
            return NotFound(new { error = "Mind map not yet generated" });

        try
        {
            using var doc = JsonDocument.Parse(mindmap.MindmapJson);
            return Ok(new
            {
                meetingId = id,
                createdAt = mindmap.CreatedAt,
                mindmap = doc.RootElement
            });
        }
        catch
        {
            return StatusCode(500, new { error = "Mind map data is corrupt — try regenerating" });
        }
    }

    [HttpPost("/api/meetings/{id}/generate-mindmap")]
    [Authorize]
    public async Task<IActionResult> GenerateMindmap(long id)
    {
        var (meeting, error) = await ResolveOwnedMeeting(id);
        if (error != null) return error;

        if (meeting!.Status != MeetingStatus.Complete)
            return BadRequest(new { error = "Meeting must be complete before generating a mind map" });

        _ = _mindmapService.GenerateAsync(id);
        return Accepted(new { status = "queued", meetingId = id });
    }

    [HttpGet("/api/meetings/{id}/mindmap/export")]
    [Authorize]
    public async Task<IActionResult> ExportMindmap(long id, [FromQuery] string format = "freemind")
    {
        var (meeting, user, error) = await ResolveOwnedMeetingWithUser(id);
        if (error != null) return error;

        if (!string.Equals(format, "freemind", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Supported formats: freemind" });

        var xml = await _mindmapService.ExportFreeMindAsync(id, user!.Id);
        if (xml == null)
            return NotFound(new { error = "No mind map found for this meeting" });

        var slug = meeting!.Title?.ToLowerInvariant().Replace(" ", "-") ?? $"meeting-{id}";
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", "");
        return File(System.Text.Encoding.UTF8.GetBytes(xml), "application/xml", $"{slug}-mindmap.mm");
    }

    // ── Mobile API Endpoints ──────────────────────────────────────────────────

    [HttpGet("/api/firm/me")]
    [Authorize(Policy = "CookieOrBearer")]
    public async Task<IActionResult> GetMe()
    {
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
        if (user == null) return NotFound(new { error = "User not found in FIRM" });

        return Ok(new
        {
            firmUserId = user.Id,
            entraOid = user.EntraOid,
            displayName = user.DisplayName,
            email = user.Email,
            isAdmin = user.IsAdmin
        });
    }

    [HttpPost("/api/firm/register-push-token")]
    [Authorize(Policy = "CookieOrBearer")]
    public async Task<IActionResult> RegisterPushToken([FromBody] RegisterPushTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExpoPushToken))
            return BadRequest(new { error = "ExpoPushToken is required" });

        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
        if (user == null) return NotFound(new { error = "User not found in FIRM" });

        user.ExpoPushToken = request.ExpoPushToken;
        user.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return Ok(new { registered = true });
    }

    [HttpPost("/api/meetings/mobile-upload")]
    [Authorize(Policy = "CookieOrBearer")]
    [RequestSizeLimit(600_000_000)] // 600MB limit
    public async Task<IActionResult> MobileUpload([FromForm] MobileUploadRequest request)
    {
        if (request.Audio == null || request.Audio.Length == 0)
            return BadRequest(new { error = "Audio file is required" });

        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst("preferred_username")?.Value ?? "";
        var displayName = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? User.FindFirst("name")?.Value ?? email;

        if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

        var firmUser = await _meetingService.GetOrCreateUserAsync(entraOid, email, displayName);
        if (firmUser == null) return StatusCode(500, new { error = "Failed to resolve user" });

        // Create meeting record
        var title = !string.IsNullOrWhiteSpace(request.Title)
            ? request.Title
            : $"Recording {(request.RecordedAt ?? DateTime.UtcNow):yyyy-MM-dd HH:mm}";

        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = new FirmMeeting
        {
            Title = title,
            Platform = "mobile",
            Source = "mobile",
            MeetingUrl = null,
            Status = MeetingStatus.Transcribing,
            CreatedBy = firmUser.Id,
            CreatorEntraOid = entraOid,
            StartedAt = request.RecordedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            EndedAt = request.RecordedAt.HasValue && request.DurationSec.HasValue
                ? request.RecordedAt.Value.ToUniversalTime().AddSeconds(request.DurationSec.Value)
                : DateTime.UtcNow,
            DurationSeconds = request.DurationSec,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        // Upload audio to S3
        var ext = System.IO.Path.GetExtension(request.Audio.FileName)?.ToLower() ?? ".m4a";
        var audioKey = $"firm-recordings-dev/{meeting.Id}/audio{ext}";
        try
        {
            await using var audioStream = request.Audio.OpenReadStream();
            await _s3Service.UploadStreamAsync(audioKey, audioStream, request.Audio.ContentType ?? "audio/mp4");
            meeting.AudioS3Key = audioKey;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Mobile upload S3 write failed for meeting {MeetingId}", meeting.Id);
            meeting.Status = MeetingStatus.Failed;
            meeting.ErrorMessage = "Audio upload to storage failed";
            await db.SaveChangesAsync();
            return StatusCode(500, new { error = "Failed to store audio file" });
        }

        // Submit Batch transcription job (same path as VP Bot recording_complete)
        try
        {
            var jobId = await _meetingService.SubmitTranscriptionJobAsync(meeting.Id);
            _logger.LogInformation("FIRM: Mobile upload Batch job {JobId} submitted for meeting {MeetingId}", jobId, meeting.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Mobile upload failed to submit Batch job for meeting {MeetingId}", meeting.Id);
            // Non-fatal for the response — meeting is created, audio is stored, can retry transcription
        }

        return Ok(new
        {
            meetingId = meeting.Id,
            status = "transcribing",
            message = "Recording received. Transcript will be ready shortly."
        });
    }

    [HttpGet("/api/meetings/list")]
    [Authorize(Policy = "CookieOrBearer")]
    public async Task<IActionResult> ListMeetings([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);

        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
        if (user == null) return Ok(new { meetings = Array.Empty<object>(), total = 0, page });

        var query = db.Meetings.Where(m => m.CreatedBy == user.Id);
        var total = await query.CountAsync();
        var meetings = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new
            {
                id = m.Id,
                title = m.Title,
                status = m.Status.ToString().ToLower(),
                source = m.Source,
                platform = m.Platform,
                recordedAt = m.StartedAt,
                durationSec = m.DurationSeconds,
                hasSummary = db.Summaries.Any(s => s.MeetingId == m.Id),
                hasMindmap = db.Mindmaps.Any(mm => mm.MeetingId == m.Id),
                createdAt = m.CreatedAt
            })
            .ToListAsync();

        return Ok(new { meetings, total, page, pageSize });
    }

    public record PushToKbRequest(string DocType, List<string> KbScopes);

    private async Task<(FirmMeeting? meeting, IActionResult? error)> ResolveOwnedMeeting(long id)
    {
        var (meeting, _, error) = await ResolveOwnedMeetingWithUser(id);
        return (meeting, error);
    }

    private async Task<(FirmMeeting? meeting, FirmUser? user, IActionResult? error)> ResolveOwnedMeetingWithUser(long id)
    {
        var entraOid = User.FindFirst("oid")?.Value
            ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        if (string.IsNullOrEmpty(entraOid)) return (null, null, Unauthorized());

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid);
        if (user == null) return (null, null, Unauthorized());

        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == id && m.CreatedBy == user.Id);
        if (meeting == null) return (null, null, NotFound(new { error = "Meeting not found" }));

        return (meeting, user, null);
    }

    public record JoinRequest(
        string MeetingUrl,
        string? Title,
        string? Platform = null,
        bool Force = false,
        DateTime? StartDatetime = null,
        string? CalendarEventId = null);
}

public class ChannelPostRequest
{
    public string? TeamId { get; set; }
    public string? TeamName { get; set; }
    public string? ChannelId { get; set; }
    public string? ChannelName { get; set; }
    public string DocType { get; set; } = "summary";
}

internal class ChannelPostHistoryRow
{
    public string TeamName { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string DocType { get; set; } = "";
    public DateTime PostedAt { get; set; }
    public bool Success { get; set; }
}

internal class BotInstallRow
{
    public string TeamId { get; set; } = "";
    public string ChannelId { get; set; } = "";
}

public class VpCallbackPayload
{
    public long MeetingId { get; set; }
    public string Status { get; set; } = "";
    public string? Reason { get; set; }
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
    [JsonPropertyName("summaryText")]
    public string? SummaryText { get; set; }
    public string? ActionItemsJson { get; set; }
    public string? KeyDecisionsJson { get; set; }
    public string? FollowUpsJson { get; set; }
    public string? OpenQuestionsJson { get; set; }
    public string? ModelUsed { get; set; }
}

public class ActionItem
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("owner")]
    public string? Owner { get; set; }
    [JsonPropertyName("deadline")]
    public string? Deadline { get; set; }
}

public record RegisterPushTokenRequest(string ExpoPushToken);

public class MobileUploadRequest
{
    public IFormFile? Audio { get; set; }
    public string? Title { get; set; }
    public DateTime? RecordedAt { get; set; }
    public int? DurationSec { get; set; }
    public string KbScope { get; set; } = "none";
}
