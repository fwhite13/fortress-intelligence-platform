using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.S3;
using Amazon.S3.Model;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FortressIntelligenceRM.Web.Services;

public enum ResummarizeOutcome { Success, Invalid, NotFound, Failed }

public record ResummarizeResult(ResummarizeOutcome Outcome, string? Error = null, int SummaryVersion = 0, string? Summary = null);

/// <summary>
/// WI #7299 — regenerates a meeting summary from a user's plain-English speaker-attribution
/// correction. Reads the transcriber's transcript.json/summary.json from S3, asks Bedrock for a
/// corrected summary, archives the original as summary-v1.json, writes the new summary.json,
/// updates firm_meeting_summaries.summary_text, bumps firm_meetings.summary_version, and records
/// the correction in firm_meeting_corrections.
/// Used by both POST /api/meetings/{id}/re-summarize and the MeetingDetail page.
/// </summary>
public class ResummarizeService
{
    public const int MaxCorrectionLength = 4000;

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<ResummarizeService> _logger;

    private string? ModelId => _config["Bedrock:SummaryModelId"];
    private string BucketName => _config["Firm:S3Bucket"] ?? "firm-recordings-dev";

    public ResummarizeService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IAmazonBedrockRuntime bedrock,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<ResummarizeService> logger)
    {
        _dbFactory = dbFactory;
        _bedrock = bedrock;
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    public async Task<ResummarizeResult> ResummarizeAsync(long meetingId, Guid userId, string corrections, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(corrections))
            return new(ResummarizeOutcome.Invalid, "Correction text is required");
        if (corrections.Length > MaxCorrectionLength)
            return new(ResummarizeOutcome.Invalid, $"Correction must be {MaxCorrectionLength} characters or fewer");
        if (string.IsNullOrEmpty(ModelId))
        {
            _logger.LogError("FIRM: Re-summarize unavailable — Bedrock:SummaryModelId not configured");
            return new(ResummarizeOutcome.Failed, "Summarization model not configured");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId && m.CreatedBy == userId, ct);
        if (meeting == null)
            return new(ResummarizeOutcome.NotFound, "Meeting not found");

        // WI #7033: a subscriber row shares its primary's transcript/summary — correct the source.
        var artifactMeetingId = meeting.PrimaryMeetingId ?? meeting.Id;
        var prefix = $"firm-transcripts/{artifactMeetingId}";

        var transcriptJson = await TryGetObjectAsync($"{prefix}/transcript.json", ct);
        var summaryJson = await TryGetObjectAsync($"{prefix}/summary.json", ct);
        if (transcriptJson == null || summaryJson == null)
            return new(ResummarizeOutcome.NotFound, "Original transcript or summary not found");

        JsonObject summaryDoc;
        try
        {
            summaryDoc = JsonNode.Parse(summaryJson) as JsonObject
                ?? throw new JsonException("summary.json is not a JSON object");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "FIRM: Re-summarize — summary.json unreadable for meeting {Id}", artifactMeetingId);
            return new(ResummarizeOutcome.Failed, "Original summary is unreadable");
        }

        var originalSummaryText = summaryDoc["summaryText"] is JsonValue st && st.TryGetValue<string>(out var stText)
            ? stText
            : summaryJson;
        var transcriptText = FormatTranscript(transcriptJson);

        var prompt = $@"You are correcting speaker attribution in a meeting summary.
USER CORRECTION: {corrections}
ORIGINAL TRANSCRIPT: {transcriptText}
ORIGINAL SUMMARY: {originalSummaryText}
Produce a corrected summary with accurate speaker attribution based on the correction.
Keep the original summary's markdown structure and level of detail; change only what the correction requires.
Return JSON only, no prose or code fences: {{ ""summary"": ""..."", ""speakerMappings"": {{ ""SPEAKER_00"": ""Name"", ... }} }}";

        var (newSummary, speakerMappings) = await InvokeBedrockAsync(prompt, artifactMeetingId, ct);
        if (string.IsNullOrWhiteSpace(newSummary))
            return new(ResummarizeOutcome.Failed, "Summarization failed");

        try
        {
            // Archive the transcriber's original once; later corrections all overwrite summary.json.
            var archiveKey = $"{prefix}/summary-v1.json";
            if (!await ObjectExistsAsync(archiveKey, ct))
                await PutJsonAsync(archiveKey, summaryJson, ct);

            // Preserve the original shape (keyDecisionsJson, actionItemsJson, ...) so every existing
            // summary.json reader keeps working — only summaryText changes.
            summaryDoc["summaryText"] = newSummary;
            if (speakerMappings != null) summaryDoc["speakerMappings"] = speakerMappings;

            var artifactMeeting = artifactMeetingId == meeting.Id
                ? meeting
                : await db.Meetings.FirstAsync(m => m.Id == artifactMeetingId, ct);
            var newVersion = artifactMeeting.SummaryVersion + 1;
            summaryDoc["summaryVersion"] = newVersion;

            await PutJsonAsync($"{prefix}/summary.json", summaryDoc.ToJsonString(), ct);

            // Version bump, summary text and correction row commit together, only after S3 succeeded.
            artifactMeeting.SummaryVersion = newVersion;
            artifactMeeting.UpdatedAt = DateTime.UtcNow;

            var summaryRow = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == artifactMeetingId, ct);
            if (summaryRow == null)
            {
                db.Summaries.Add(new FirmMeetingSummary
                {
                    MeetingId = artifactMeetingId,
                    SummaryText = newSummary,
                    ModelUsed = ModelId,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                summaryRow.SummaryText = newSummary;
                summaryRow.ModelUsed = ModelId;
            }

            db.MeetingCorrections.Add(new FirmMeetingCorrection
            {
                MeetingId = artifactMeetingId,
                Correction = corrections,
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);

            _logger.LogInformation("FIRM: Re-summarized meeting {Id} with speaker correction → summary v{Version}", artifactMeetingId, newVersion);
            return new(ResummarizeOutcome.Success, SummaryVersion: newVersion, Summary: newSummary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Re-summarize save failed for meeting {Id}", artifactMeetingId);
            return new(ResummarizeOutcome.Failed, "Failed to save corrected summary");
        }
    }

    /// <summary>Formats transcriber transcript.json (bare segment array) as "[hh:mm:ss] SPEAKER_00 (Name): text" lines,
    /// keeping both the diarization label and any resolved name so the model can remap them.</summary>
    private static string FormatTranscript(string transcriptJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(transcriptJson);
            var segments = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.TryGetProperty("segments", out var wrapped) ? wrapped : default;
            if (segments.ValueKind != JsonValueKind.Array) return transcriptJson;

            var sb = new StringBuilder();
            foreach (var seg in segments.EnumerateArray())
            {
                var label = GetString(seg, "speakerLabel") ?? GetString(seg, "speaker_label") ?? "Unknown";
                var name = GetString(seg, "speakerName") ?? GetString(seg, "speaker_name");
                var speaker = string.IsNullOrEmpty(name) || name == label ? label : $"{label} ({name})";
                var startMs = seg.TryGetProperty("startTimeMs", out var s) && s.TryGetInt64(out var ms) ? ms : 0L;
                sb.AppendLine($"[{TimeSpan.FromMilliseconds(startMs):hh\\:mm\\:ss}] {speaker}: {GetString(seg, "text")}");
            }
            return sb.ToString();
        }
        catch (JsonException)
        {
            return transcriptJson;
        }
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private async Task<(string? summary, JsonObject? speakerMappings)> InvokeBedrockAsync(string prompt, long meetingId, CancellationToken ct)
    {
        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 8192,
                messages = new[] { new { role = "user", content = prompt } }
            });

            var response = await _bedrock.InvokeModelAsync(new InvokeModelRequest
            {
                ModelId = ModelId,
                ContentType = "application/json",
                Accept = "application/json",
                Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody))
            }, ct);

            var responseJson = await new StreamReader(response.Body).ReadToEndAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            string? text = null;
            if (doc.RootElement.TryGetProperty("content", out var contentArr))
            {
                foreach (var item in contentArr.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var typeEl) && typeEl.GetString() == "text")
                    {
                        text = item.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("FIRM: Re-summarize — Bedrock returned empty text for meeting {MeetingId}", meetingId);
                return (null, null);
            }

            // Tolerate code fences or stray prose around the JSON object.
            text = Regex.Replace(text.Trim(), @"^```json?\s*|```$", "", RegexOptions.Multiline).Trim();
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                _logger.LogWarning("FIRM: Re-summarize — Bedrock response had no JSON object for meeting {MeetingId}", meetingId);
                return (null, null);
            }

            var result = JsonNode.Parse(text[start..(end + 1)]) as JsonObject;
            var summary = result?["summary"] is JsonValue sv && sv.TryGetValue<string>(out var str) ? str : null;
            var mappings = result?["speakerMappings"] as JsonObject;
            result?.Remove("speakerMappings"); // detach so it can be re-parented into summary.json
            return (summary, mappings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Re-summarize — Bedrock call failed for meeting {MeetingId}", meetingId);
            return (null, null);
        }
    }

    private async Task<string?> TryGetObjectAsync(string key, CancellationToken ct)
    {
        try
        {
            using var response = await _s3.GetObjectAsync(BucketName, key, ct);
            using var reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync(ct);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogInformation("FIRM: Re-summarize — S3 object not found: {Key}", key);
            return null;
        }
    }

    private async Task<bool> ObjectExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(BucketName, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private Task PutJsonAsync(string key, string json, CancellationToken ct) =>
        _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            ContentBody = json,
            ContentType = "application/json"
        }, ct);
}
