using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.S3;
using Amazon.S3.Model;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Services;

public class FirmKbService
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonBedrockAgent _bedrockAgent;
    private readonly IConfiguration _config;
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<FirmKbService> _logger;

    private string BucketName => _config["Firm:KbS3Bucket"] ?? "fortress-tools";
    private string PersonalKbId => _config["Firm:PersonalKbId"] ?? "ZCEZCJGHQC";
    private string PersonalDsId => _config["Firm:PersonalKbDsId"] ?? "3X5E9L4HAC";
    private string TeamKbId => _config["Firm:TeamKbId"] ?? "NRGEACKSBJ";
    private string TeamDsId => _config["Firm:TeamKbDsId"] ?? "VYMEB3BA12";

    public FirmKbService(
        IAmazonS3 s3,
        IAmazonBedrockAgent bedrockAgent,
        IConfiguration config,
        IDbContextFactory<FirmDbContext> dbFactory,
        ILogger<FirmKbService> logger)
    {
        _s3 = s3;
        _bedrockAgent = bedrockAgent;
        _config = config;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>Push meeting transcript to the user's personal KB in S3, then trigger Bedrock ingestion.</summary>
    public async Task PushTranscriptAsync(long meetingId, string userId, string faitUserId)
    {
        if (string.IsNullOrWhiteSpace(faitUserId))
        {
            _logger.LogWarning("FirmKbService: faitUserId is null/empty for meeting {MeetingId} — skipping transcript push", meetingId);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var segments = await db.Transcripts
            .Where(t => t.MeetingId == meetingId)
            .OrderBy(t => t.StartTimeMs)
            .ToListAsync();

        if (!segments.Any())
        {
            _logger.LogWarning("FirmKbService: No transcript segments found for meeting {MeetingId}", meetingId);
            return;
        }

        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            var speaker = seg.SpeakerName ?? seg.SpeakerLabel ?? "Unknown";
            var ts = seg.StartTimeMs.HasValue
                ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss")
                : "00:00:00";
            sb.AppendLine($"[{ts}] {speaker}: {seg.Text}");
        }

        var content = sb.ToString();
        var s3Key = $"kb-docs/personal/{faitUserId}/firm-transcript-{meetingId}.txt";

        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                ContentBody = content,
                ContentType = "text/plain"
            });
            _logger.LogInformation("FirmKbService: Uploaded transcript for meeting {MeetingId} to s3://{Bucket}/{Key}", meetingId, BucketName, s3Key);

            await StartPersonalIngestionAsync();

            var meeting = await db.Meetings.FindAsync(meetingId);
            if (meeting != null)
            {
                meeting.TranscriptKbPushed = true;
                meeting.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FirmKbService: Failed to push transcript for meeting {MeetingId}", meetingId);
            throw;
        }
    }

    /// <summary>Push meeting summary to the user's personal KB in S3, then trigger Bedrock ingestion.</summary>
    public async Task PushSummaryAsync(long meetingId, string userId, string faitUserId)
    {
        if (string.IsNullOrWhiteSpace(faitUserId))
        {
            _logger.LogWarning("FirmKbService: faitUserId is null/empty for meeting {MeetingId} — skipping summary push", meetingId);
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == meetingId);
        if (summary == null)
        {
            _logger.LogWarning("FirmKbService: No summary found for meeting {MeetingId}", meetingId);
            return;
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(summary.SummaryText))
        {
            sb.AppendLine("## Overview");
            sb.AppendLine(summary.SummaryText);
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(summary.KeyDecisionsJson))
        {
            var decisions = TryDeserializeList(summary.KeyDecisionsJson);
            if (decisions.Any())
            {
                sb.AppendLine("## Key Decisions");
                decisions.ForEach(d => sb.AppendLine($"- {d}"));
                sb.AppendLine();
            }
        }
        if (!string.IsNullOrEmpty(summary.ActionItemsJson))
        {
            sb.AppendLine("## Action Items");
            try
            {
                var items = JsonSerializer.Deserialize<List<ActionItemDto>>(summary.ActionItemsJson) ?? new();
                items.ForEach(i => sb.AppendLine($"- [{i.Owner ?? "?"}] {i.Description}"));
            }
            catch { }
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(summary.FollowUpsJson))
        {
            var followUps = TryDeserializeList(summary.FollowUpsJson);
            if (followUps.Any())
            {
                sb.AppendLine("## Follow-ups");
                followUps.ForEach(f => sb.AppendLine($"- {f}"));
            }
        }

        var content = sb.ToString();
        var s3Key = $"kb-docs/personal/{faitUserId}/firm-summary-{meetingId}.md";

        try
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                ContentBody = content,
                ContentType = "text/markdown"
            });
            _logger.LogInformation("FirmKbService: Uploaded summary for meeting {MeetingId} to s3://{Bucket}/{Key}", meetingId, BucketName, s3Key);

            await StartPersonalIngestionAsync();

            var meeting = await db.Meetings.FindAsync(meetingId);
            if (meeting != null)
            {
                meeting.SummaryKbPushed = true;
                meeting.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FirmKbService: Failed to push summary for meeting {MeetingId}", meetingId);
            throw;
        }
    }

    /// <summary>
    /// Push meeting content (transcript or summary) to one or more KB scopes.
    /// Checks for existing push record BEFORE uploading to S3 — dedup is mandatory.
    /// </summary>
    public async Task PushDocumentAsync(long meetingId, string userId, string faitUserId, string docType, IEnumerable<string> kbScopes)
    {
        var scopeList = kbScopes.Distinct().ToList();
        if (!scopeList.Any()) return;

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Build the document content once
        string content;
        string contentType;
        string fileExtension;
        if (docType == "transcript")
        {
            (content, contentType, fileExtension) = await BuildTranscriptContentAsync(db, meetingId);
        }
        else if (docType == "summary")
        {
            (content, contentType, fileExtension) = await BuildSummaryContentAsync(db, meetingId);
        }
        else
        {
            throw new ArgumentException($"Unknown docType: {docType}");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("FirmKbService: No content for {DocType} of meeting {MeetingId}", docType, meetingId);
            return;
        }

        foreach (var scope in scopeList)
        {
            // DEDUP CHECK FIRST — before any S3 upload
            var existing = await db.FirmMeetingKbPushes
                .FirstOrDefaultAsync(p => p.MeetingId == meetingId && p.DocType == docType && p.KbScope == scope);
            if (existing != null)
            {
                _logger.LogInformation("FirmKbService: {DocType} for meeting {MeetingId} already in {Scope} KB — skipping", docType, meetingId, scope);
                continue;
            }

            string kbId, dsId, s3Prefix;
            if (scope == "personal")
            {
                kbId = PersonalKbId;
                dsId = PersonalDsId;
                s3Prefix = $"kb-docs/personal/{faitUserId}";
            }
            else if (scope == "team")
            {
                kbId = TeamKbId;
                dsId = TeamDsId;
                s3Prefix = "kb-docs/team/firm";
            }
            else
            {
                _logger.LogWarning("FirmKbService: Unknown KB scope {Scope} — skipping", scope);
                continue;
            }

            var s3Key = $"{s3Prefix}/firm-{docType}-{meetingId}.{fileExtension}";

            try
            {
                await _s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = BucketName,
                    Key = s3Key,
                    ContentBody = content,
                    ContentType = contentType
                });

                // For personal KB: write metadata.json for KB isolation
                if (scope == "personal" && !string.IsNullOrEmpty(faitUserId))
                {
                    var metadata = new { metadataAttributes = new Dictionary<string, object> { ["ownerId"] = faitUserId } };
                    var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata,
                        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    await _s3.PutObjectAsync(new PutObjectRequest
                    {
                        BucketName = BucketName,
                        Key = $"{s3Key}.metadata.json",
                        ContentBody = metadataJson,
                        ContentType = "application/json"
                    });
                }

                // Trigger Bedrock ingestion
                await StartIngestionAsync(kbId, dsId);

                // Record the push in firm_meeting_kb_pushes
                db.FirmMeetingKbPushes.Add(new FirmMeetingKbPush
                {
                    MeetingId = meetingId,
                    DocType = docType,
                    KbScope = scope,
                    KbId = kbId,
                    PushedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                _logger.LogInformation("FirmKbService: Pushed {DocType} for meeting {MeetingId} to {Scope} KB ({KbId})", docType, meetingId, scope, kbId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FirmKbService: Failed to push {DocType} to {Scope} KB for meeting {MeetingId}", docType, scope, meetingId);
                throw;
            }
        }
    }

    /// <summary>Returns which KB scopes a document has already been pushed to.</summary>
    public async Task<HashSet<string>> GetPushedScopesAsync(long meetingId, string docType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var pushed = await db.FirmMeetingKbPushes
            .Where(p => p.MeetingId == meetingId && p.DocType == docType)
            .Select(p => p.KbScope)
            .ToListAsync();
        return pushed.ToHashSet();
    }

    private async Task<(string content, string contentType, string extension)> BuildTranscriptContentAsync(FirmDbContext db, long meetingId)
    {
        var segments = await db.Transcripts
            .Where(t => t.MeetingId == meetingId)
            .OrderBy(t => t.StartTimeMs)
            .ToListAsync();
        if (!segments.Any()) return ("", "", "");
        var sb = new StringBuilder();
        foreach (var seg in segments)
        {
            var speaker = seg.SpeakerName ?? seg.SpeakerLabel ?? "Unknown";
            var ts = seg.StartTimeMs.HasValue
                ? TimeSpan.FromMilliseconds(seg.StartTimeMs.Value).ToString(@"hh\:mm\:ss")
                : "00:00:00";
            sb.AppendLine($"[{ts}] {speaker}: {seg.Text}");
        }
        return (sb.ToString(), "text/plain", "txt");
    }

    private async Task<(string content, string contentType, string extension)> BuildSummaryContentAsync(FirmDbContext db, long meetingId)
    {
        var summary = await db.Summaries.FirstOrDefaultAsync(s => s.MeetingId == meetingId);
        if (summary == null) return ("", "", "");
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(summary.SummaryText))
        {
            sb.AppendLine("## Overview");
            sb.AppendLine(summary.SummaryText);
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(summary.KeyDecisionsJson))
        {
            var decisions = TryDeserializeList(summary.KeyDecisionsJson);
            if (decisions.Any())
            {
                sb.AppendLine("## Key Decisions");
                decisions.ForEach(d => sb.AppendLine($"- {d}"));
                sb.AppendLine();
            }
        }
        if (!string.IsNullOrEmpty(summary.ActionItemsJson))
        {
            sb.AppendLine("## Action Items");
            try
            {
                var items = JsonSerializer.Deserialize<List<ActionItemDto>>(summary.ActionItemsJson) ?? new();
                items.ForEach(i => sb.AppendLine($"- [{i.Owner ?? "?"}] {i.Description}"));
            }
            catch { }
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(summary.FollowUpsJson))
        {
            var followUps = TryDeserializeList(summary.FollowUpsJson);
            if (followUps.Any())
            {
                sb.AppendLine("## Follow-ups");
                followUps.ForEach(f => sb.AppendLine($"- {f}"));
            }
        }
        return (sb.ToString(), "text/markdown", "md");
    }

    private async Task StartIngestionAsync(string kbId, string dsId)
    {
        try
        {
            await _bedrockAgent.StartIngestionJobAsync(new StartIngestionJobRequest
            {
                KnowledgeBaseId = kbId,
                DataSourceId = dsId
            });
        }
        catch (Amazon.BedrockAgent.Model.ConflictException)
        {
            _logger.LogInformation("FirmKbService: Ingestion already in progress for KB {KbId} — will sync on next run", kbId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FirmKbService: Failed to start ingestion for KB {KbId} (non-fatal)", kbId);
        }
    }

    private async Task StartPersonalIngestionAsync()
    {
        try
        {
            var response = await _bedrockAgent.StartIngestionJobAsync(new StartIngestionJobRequest
            {
                KnowledgeBaseId = PersonalKbId,
                DataSourceId = PersonalDsId
            });
            _logger.LogInformation("FirmKbService: Started personal KB ingestion job {JobId}", response.IngestionJob?.IngestionJobId);
        }
        catch (Amazon.BedrockAgent.Model.ConflictException)
        {
            _logger.LogInformation("FirmKbService: Personal KB ingestion already in progress — will sync on next scheduled run");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FirmKbService: Failed to start KB ingestion — document will sync on next scheduled ingestion");
        }
    }

    private static List<string> TryDeserializeList(string json)
    {
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch { return new(); }
    }

    private class ActionItemDto
    {
        public string? Description { get; set; }
        public string? Owner { get; set; }
    }
}
