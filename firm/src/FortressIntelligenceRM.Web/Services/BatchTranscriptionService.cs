using Amazon.Batch;
using Amazon.Batch.Model;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressIntelligenceRM.Web.Services;

/// <summary>
/// WI #8875: per-run pyannote diarization overrides from the Advanced Retranscribe modal. A null field
/// is not injected, so the transcriber falls back to its task-definition/env defaults (WI #8874).
/// </summary>
public record DiarizationOverrides(float? ClusteringThreshold = null, float? MinDurationOff = null, int? MinClusterSize = null)
{
    public const float MinThreshold = 0f, MaxThreshold = 1f;
    public const float MinDurationOffFloor = 0f, MinDurationOffCeiling = 2f;
    public const int MinClusterSizeFloor = 1, MinClusterSizeCeiling = 30;
    public const float DefaultClusteringThreshold = 0.60f;
    public const float DefaultMinDurationOff = 0.5f;

    public bool IsEmpty => ClusteringThreshold == null && MinDurationOff == null && MinClusterSize == null;

    /// <summary>Mirrors transcribe.py's computed min_cluster_size: max(4, round(duration_minutes / num_speakers)).</summary>
    public static int? ComputeMinClusterSize(int? durationSeconds, int speakerCount) =>
        durationSeconds is int d && d > 0 && speakerCount > 0
            ? Math.Max(4, (int)Math.Round(d / 60.0 / speakerCount, MidpointRounding.ToEven))
            : null;

    /// <summary>Returns an error message if any provided value is out of range, else null.</summary>
    public string? Validate()
    {
        if (ClusteringThreshold is float t && (float.IsNaN(t) || t < MinThreshold || t > MaxThreshold))
            return $"Clustering threshold must be between {MinThreshold} and {MaxThreshold}";
        if (MinDurationOff is float o && (float.IsNaN(o) || o < MinDurationOffFloor || o > MinDurationOffCeiling))
            return $"Min duration off must be between {MinDurationOffFloor} and {MinDurationOffCeiling} seconds";
        if (MinClusterSize is int c && (c < MinClusterSizeFloor || c > MinClusterSizeCeiling))
            return $"Min cluster size must be between {MinClusterSizeFloor} and {MinClusterSizeCeiling}";
        return null;
    }
}

public interface IBatchTranscriptionService
{
    Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, DateTime? meetingDate = null, string? creatorEntraOid = null, string? rosterTimeline = null, string? activeSpeakerTimeline = null, CancellationToken ct = default, DateTime? meetingStartDatetime = null, bool summarizeOnly = false, string? transcriptS3Key = null, string? speakerCorrections = null, DiarizationOverrides? diarization = null);
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

    public async Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, DateTime? meetingDate = null, string? creatorEntraOid = null, string? rosterTimeline = null, string? activeSpeakerTimeline = null, CancellationToken ct = default, DateTime? meetingStartDatetime = null, bool summarizeOnly = false, string? transcriptS3Key = null, string? speakerCorrections = null, DiarizationOverrides? diarization = null)
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
            // WI #8294: anchor for relative dates ("next Tuesday") in scheduling detection
            new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_START_DATETIME", Value = (meetingStartDatetime ?? meetingDate)?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "" },
            new Amazon.Batch.Model.KeyValuePair { Name = "BEDROCK_MODEL_ID", Value = _config["Firm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-6" },
            new Amazon.Batch.Model.KeyValuePair { Name = "PYANNOTE_CACHE", Value = "/app/.cache/huggingface/hub" },
            new Amazon.Batch.Model.KeyValuePair { Name = "BEDROCK_MODEL_ID", Value = _config["Firm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-6" },
            new Amazon.Batch.Model.KeyValuePair { Name = "NOTETAKER_NAME", Value = _branding.NotetakerName },
            // ADO#8295: firm_meetings has no per-meeting tenant column — FIRM is single-tenant per
            // deployment, so the configured Graph tenant scopes the voiceprint DB lookups.
            new Amazon.Batch.Model.KeyValuePair { Name = "TENANT_ID", Value = tenantId },
            // Voiceprint DB location (non-secret). FORTRESS_DB_PASS must come from the job definition's
            // Secrets Manager mapping — never pass it as a container override (visible in DescribeJobs).
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_HOST", Value = _config["FORTRESS_DB_HOST"] ?? "" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_PORT", Value = _config["FORTRESS_DB_PORT"] ?? "3306" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_USER", Value = _config["FORTRESS_DB_USER"] ?? "fortress_mysql" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_DB_NAME", Value = _config["FIRM_DB_NAME"] ?? "firm_dev" },
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

        // WI #8335: summarize-only reruns just the summarization pass on the existing transcript with
        // the user's speaker corrections overlaid — no Whisper/diarization. The overlay is staged in S3
        // (same 8192-byte override cap as the active speaker timeline above).
        if (summarizeOnly)
        {
            if (string.IsNullOrEmpty(transcriptS3Key))
                throw new InvalidOperationException($"Meeting {meetingId} has no TranscriptS3Key — cannot submit summarize-only job");
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "SUMMARIZE_ONLY", Value = "true" });
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "TRANSCRIPT_S3_KEY", Value = transcriptS3Key });
            if (!string.IsNullOrEmpty(speakerCorrections))
            {
                var correctionsKey = $"firm-transcripts/{meetingId}/speaker_corrections.json";
                await _s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = S3Bucket,
                    Key = correctionsKey,
                    ContentBody = speakerCorrections,
                    ContentType = "application/json"
                }, ct);
                _logger.LogInformation("FIRM: Uploaded speaker corrections for meeting {MeetingId} to s3://{Bucket}/{Key}", meetingId, S3Bucket, correctionsKey);
                envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "SPEAKER_CORRECTIONS_S3_KEY", Value = correctionsKey });
            }
        }

        // WI #8875: per-run diarization overrides — only the fields the user changed are injected.
        if (diarization != null && !summarizeOnly)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (diarization.ClusteringThreshold is float threshold)
                envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "DIARIZE_CLUSTERING_THRESHOLD", Value = threshold.ToString(inv) });
            if (diarization.MinDurationOff is float minDurationOff)
                envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "DIARIZE_MIN_DURATION_OFF", Value = minDurationOff.ToString(inv) });
            if (diarization.MinClusterSize is int minClusterSize)
                envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "DIARIZE_MIN_CLUSTER_SIZE_OVERRIDE", Value = minClusterSize.ToString(inv) });
            if (!diarization.IsEmpty)
                _logger.LogInformation("FIRM: Diarization overrides for meeting {MeetingId}: threshold={Threshold} min_duration_off={MinDurationOff} min_cluster_size={MinClusterSize}",
                    meetingId, diarization.ClusteringThreshold, diarization.MinDurationOff, diarization.MinClusterSize);
        }

        var request = new SubmitJobRequest
        {
            JobName = $"{(summarizeOnly ? "resummarize" : "retranscribe")}-meeting-{meetingId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            JobQueue = JobQueue,
            JobDefinition = JobDefinition,
            ContainerOverrides = new ContainerOverrides
            {
                Environment = envVars
            }
        };

        var response = await _batch.SubmitJobAsync(request, ct);
        _logger.LogInformation("FIRM: Batch {Mode} job {JobId} submitted for meeting {MeetingId}", summarizeOnly ? "summarize-only" : "retranscribe", response.JobId, meetingId);
        return response.JobId;
    }
}
