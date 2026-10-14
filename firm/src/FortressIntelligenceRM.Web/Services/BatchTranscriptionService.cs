using Amazon.Batch;
using Amazon.Batch.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressIntelligenceRM.Web.Services;

public interface IBatchTranscriptionService
{
    Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, DateTime? meetingDate = null, string? creatorEntraOid = null, string? rosterTimeline = null, string? activeSpeakerTimeline = null, CancellationToken ct = default);
}

public class BatchTranscriptionService : IBatchTranscriptionService
{
    private readonly IAmazonBatch _batch;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly IOrgContextService _orgContextService;
    private readonly IUserWikiService _userWikiService;
    private readonly BrandingConfig _branding;
    private readonly ILogger<BatchTranscriptionService> _logger;
    private string JobQueue => _config["Firm:BatchJobQueue"] ?? "firm-transcription-queue";
    private string JobDefinition => _config["Firm:BatchJobDefinition"] ?? "firm-transcription-job";
    private string S3Bucket => _config["Firm:S3Bucket"] ?? "firm-recordings-dev";

    public BatchTranscriptionService(IAmazonBatch batch, IAmazonS3 s3, IConfiguration config, IOrgContextService orgContextService, IUserWikiService userWikiService, BrandingConfig branding, ILogger<BatchTranscriptionService> logger)
    {
        _batch = batch;
        _s3 = s3;
        _config = config;
        _orgContextService = orgContextService;
        _userWikiService = userWikiService;
        _branding = branding;
        _logger = logger;
    }

    public async Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, DateTime? meetingDate = null, string? creatorEntraOid = null, string? rosterTimeline = null, string? activeSpeakerTimeline = null, CancellationToken ct = default)
    {
        var callbackSecret = _config["Firm:BotCallbackSecret"] ?? "";
        if (string.IsNullOrEmpty(callbackSecret))
            _logger.LogWarning("FIRM: BotCallbackSecret is not configured — Batch job callback will return 401");

        var tenantId = _config["Firm:GraphTenantId"] ?? "";

        // Build merged wiki JSON (org + personal)
        string? wikiJson = null;
        if (!string.IsNullOrEmpty(tenantId))
        {
            var orgEntries = await _orgContextService.GetContextAsync(tenantId);
            var userWikiEntries = !string.IsNullOrEmpty(creatorEntraOid)
                ? await _userWikiService.GetEntriesAsync(creatorEntraOid, tenantId)
                : new List<OrgContextEntry>();

            // Flatten into a single list of {Term, Description, Source} entries.
            // transcribe.py iterates this as a flat array — the old nested {source, entries[]}
            // wrapper caused e.get("Term") to always return "" (the wrapper object has no Term key).
            var flatEntries = new List<object>();
            foreach (var e in orgEntries)
                flatEntries.Add(new { e.Term, e.Description, Source = "organization" });
            foreach (var e in userWikiEntries)
                flatEntries.Add(new { e.Term, e.Description, Source = "personal" });

            if (flatEntries.Count > 0)
                _logger.LogInformation("FIRM: ORG_WIKI_JSON will contain {Count} entries ({Org} org, {Personal} personal) for meeting {MeetingId}",
                    flatEntries.Count, orgEntries.Count, userWikiEntries.Count, meetingId);

            wikiJson = flatEntries.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(flatEntries)
                : null;
        }

        var envVars = new List<Amazon.Batch.Model.KeyValuePair>
        {
            new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_ID", Value = meetingId.ToString() },
            new Amazon.Batch.Model.KeyValuePair { Name = "AUDIO_S3_KEY", Value = audioS3Key },
            // ADO#6798/Issue 1: audio lives in whichever bucket THIS environment's firm-web is
            // configured with (Firm__S3Bucket), not whatever is baked into the Batch job definition.
            // Mirrors the same override VpBotService already applies when launching the vpbot task.
            new Amazon.Batch.Model.KeyValuePair { Name = "S3_BUCKET", Value = S3Bucket },
            new Amazon.Batch.Model.KeyValuePair { Name = "BOT_CALLBACK_SECRET", Value = callbackSecret },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_CALLBACK_URL", Value = _config["Firm:CallbackUrl"] ?? "http://firm.fip.internal:8080/api/vp/callback" },
            new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_DATE", Value = meetingDate?.ToString("yyyy-MM-dd") ?? "" },
            new Amazon.Batch.Model.KeyValuePair { Name = "BEDROCK_MODEL_ID", Value = _config["Firm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-6" },
            new Amazon.Batch.Model.KeyValuePair { Name = "PYANNOTE_CACHE", Value = "/app/.cache/huggingface/hub" },
            new Amazon.Batch.Model.KeyValuePair { Name = "BEDROCK_MODEL_ID", Value = _config["Firm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-6" },
            new Amazon.Batch.Model.KeyValuePair { Name = "NOTETAKER_NAME", Value = _branding.NotetakerName },
        };

        if (wikiJson != null)
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "ORG_WIKI_JSON", Value = wikiJson });

        if (!string.IsNullOrEmpty(rosterTimeline))
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "ROSTER_TIMELINE_JSON", Value = rosterTimeline });

        // ADO#8230: Batch caps container overrides at 8192 bytes, and a long active speaker timeline
        // blows past that inline. Stage it in S3 next to the audio and pass only the key.
        if (!string.IsNullOrEmpty(activeSpeakerTimeline))
        {
            var activeSpeakerKey = $"firm-audio/{meetingId}/active_speaker.json";
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = S3Bucket,
                Key = activeSpeakerKey,
                ContentBody = activeSpeakerTimeline,
                ContentType = "application/json"
            }, ct);
            _logger.LogInformation("FIRM: Uploaded active speaker timeline for meeting {MeetingId} to s3://{Bucket}/{Key}", meetingId, S3Bucket, activeSpeakerKey);
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "ACTIVE_SPEAKER_S3_KEY", Value = activeSpeakerKey });
        }

        var request = new SubmitJobRequest
        {
            JobName = $"retranscribe-meeting-{meetingId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            JobQueue = JobQueue,
            JobDefinition = JobDefinition,
            ContainerOverrides = new ContainerOverrides
            {
                Environment = envVars
            }
        };

        var response = await _batch.SubmitJobAsync(request, ct);
        _logger.LogInformation("FIRM: Batch retranscribe job {JobId} submitted for meeting {MeetingId}", response.JobId, meetingId);
        return response.JobId;
    }
}
