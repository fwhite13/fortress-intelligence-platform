using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.S3;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace FortressIntelligenceRM.Web.Services;

public interface IMindmapService
{
    /// <summary>Generate a mind map for the given meeting. Non-fatal — logs and returns null on failure.</summary>
    Task<FirmMeetingMindmap?> GenerateAsync(long meetingId, bool forceRegenerate = false, CancellationToken ct = default);
    /// <summary>Export the stored mind map as a FreeMind .mm XML string. Returns null if no mindmap exists.</summary>
    Task<string?> ExportFreeMindAsync(long meetingId, Guid userId);
}

public class MindmapService : IMindmapService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IAmazonBedrockRuntime _bedrock;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<MindmapService> _logger;

    private string ModelId => _config.GetValue<string>("Bedrock:SummaryModelId", "anthropic.claude-3-sonnet-20240229-v1:0")!;
    private string BucketName => _config["Firm:S3Bucket"] ?? "firm-recordings-dev";

    public MindmapService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IAmazonBedrockRuntime bedrock,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<MindmapService> logger)
    {
        _dbFactory = dbFactory;
        _bedrock = bedrock;
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    public async Task<FirmMeetingMindmap?> GenerateAsync(long meetingId, bool forceRegenerate = false, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Return existing mindmap without hitting Bedrock unless forced
            if (!forceRegenerate)
            {
                var cached = await db.Mindmaps.FirstOrDefaultAsync(m => m.MeetingId == meetingId, ct);
                if (cached != null)
                {
                    _logger.LogInformation("MindmapService: Returning cached mind map for meeting {MeetingId}", meetingId);
                    return cached;
                }
            }

            var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == meetingId, ct);
            if (summary == null)
            {
                _logger.LogWarning("MindmapService: No summary found for meeting {MeetingId} — cannot generate mind map", meetingId);
                return null;
            }

            var meeting = await db.Meetings.FindAsync(new object[] { meetingId }, ct);
            var title = meeting?.Title ?? $"Meeting {meetingId}";

            var prompt = BuildPrompt(title, summary);
            var mindmapJson = await InvokeBedrockAsync(prompt, meetingId, ct);
            if (mindmapJson == null) return null;

            // Validate it's parseable JSON
            try { JsonDocument.Parse(mindmapJson); }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "MindmapService: Bedrock returned invalid JSON for meeting {MeetingId}", meetingId);
                return null;
            }

            // Upsert into DB
            var existing = await db.Mindmaps.FirstOrDefaultAsync(m => m.MeetingId == meetingId, ct);
            if (existing != null)
            {
                existing.MindmapJson = mindmapJson;
                existing.ModelUsed = ModelId;
                existing.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                existing = new FirmMeetingMindmap
                {
                    MeetingId = meetingId,
                    MindmapJson = mindmapJson,
                    ModelUsed = ModelId,
                    CreatedAt = DateTime.UtcNow
                };
                db.Mindmaps.Add(existing);
            }
            await db.SaveChangesAsync(ct);

            // Mirror to S3 (non-fatal)
            _ = MirrorToS3Async(meetingId, mindmapJson);

            _logger.LogInformation("MindmapService: Mind map generated for meeting {MeetingId}", meetingId);
            return existing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MindmapService: Failed to generate mind map for meeting {MeetingId}", meetingId);
            return null;
        }
    }

    public async Task<string?> ExportFreeMindAsync(long meetingId, Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Verify ownership
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId && m.CreatedBy == userId);
        if (meeting == null) return null;

        var mindmap = await db.Mindmaps.FirstOrDefaultAsync(m => m.MeetingId == meetingId);
        if (mindmap == null) return null;

        try
        {
            using var doc = JsonDocument.Parse(mindmap.MindmapJson);
            var root = doc.RootElement;
            var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Meeting" : "Meeting";

            var sb = new StringBuilder();
            using var xmlWriter = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false });
            xmlWriter.WriteStartDocument();
            xmlWriter.WriteStartElement("map");
            xmlWriter.WriteAttributeString("version", "1.0.1");

            xmlWriter.WriteStartElement("node");
            xmlWriter.WriteAttributeString("TEXT", title);

            if (root.TryGetProperty("nodes", out var nodesArr))
                WriteFreeMindNodes(xmlWriter, nodesArr);

            xmlWriter.WriteEndElement(); // root node
            xmlWriter.WriteEndElement(); // map
            xmlWriter.WriteEndDocument();
            xmlWriter.Flush();

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MindmapService: FreeMind export failed for meeting {MeetingId}", meetingId);
            return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildPrompt(string title, FirmMeetingSummary summary)
    {
        return $@"You are generating a mind map from a meeting summary.
Output ONLY valid JSON matching the schema below. No markdown fences, no prose, no explanation — raw JSON only.

Schema:
{{
  ""title"": ""string — meeting title or short topic (max 6 words)"",
  ""nodes"": [
    {{
      ""id"": ""string — unique short id (e.g. n1, n2)"",
      ""label"": ""string — concise node text (max 8 words)"",
      ""children"": [ ...same structure, recursive... ]
    }}
  ]
}}

Rules:
- Root nodes (max 6): major themes such as Key Decisions, Action Items, Discussion Topics, Open Questions, Follow-ups, and dominant subject areas.
- Each root node: 2–6 children.
- Max depth: 3 levels total.
- Labels must be short — these render as visual nodes, not paragraphs.
- Do not include empty children arrays — omit the ""children"" key if a node has no children.

Meeting Title: {title}

Summary:
{summary.SummaryText ?? "(no summary text)"}

Action Items: {summary.ActionItemsJson ?? "[]"}
Key Decisions: {summary.KeyDecisionsJson ?? "[]"}
Follow-ups: {summary.FollowUpsJson ?? "[]"}
Open Questions: {summary.OpenQuestionsJson ?? "[]"}";
    }

    private async Task<string?> InvokeBedrockAsync(string prompt, long meetingId, CancellationToken ct)
    {
        try
        {
            var requestBody = JsonSerializer.Serialize(new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 2048,
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
                _logger.LogWarning("MindmapService: Bedrock returned empty text for meeting {MeetingId}", meetingId);
                return null;
            }

            // Strip any accidental markdown fences
            return Regex.Replace(text.Trim(), @"^```json?\s*|```$", "", RegexOptions.Multiline).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MindmapService: Bedrock call failed for meeting {MeetingId}", meetingId);
            return null;
        }
    }

    private static void WriteFreeMindNodes(XmlWriter w, JsonElement nodes)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            var label = node.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "";
            w.WriteStartElement("node");
            w.WriteAttributeString("TEXT", label);
            if (node.TryGetProperty("children", out var children))
                WriteFreeMindNodes(w, children);
            w.WriteEndElement();
        }
    }

    private async Task MirrorToS3Async(long meetingId, string mindmapJson)
    {
        try
        {
            var key = $"firm-mindmaps/{meetingId}/mindmap.json";
            var bytes = Encoding.UTF8.GetBytes(mindmapJson);
            await _s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = BucketName,
                Key = key,
                InputStream = new MemoryStream(bytes),
                ContentType = "application/json"
            });
            _logger.LogInformation("MindmapService: Mirrored mindmap to S3 for meeting {MeetingId}", meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MindmapService: S3 mirror failed for meeting {MeetingId} (non-fatal)", meetingId);
        }
    }
}
