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
    public async Task PushTranscriptAsync(long meetingId, Guid userId, string faitUserId)
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
    public async Task PushSummaryAsync(long meetingId, Guid userId, string faitUserId)
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
