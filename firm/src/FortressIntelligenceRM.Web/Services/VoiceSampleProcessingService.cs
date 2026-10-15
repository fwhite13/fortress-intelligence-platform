using Amazon.Batch;
using Amazon.Batch.Model;
using Amazon.S3;
using Amazon.S3.Model;

namespace FortressIntelligenceRM.Web.Services;

// WI #8298 — voice enrollment: stores the user's recorded sample in S3 and submits a Batch job
// (JOB_MODE=voice_sample) that extracts the embedding and writes the voice_sample row.
public class VoiceSampleProcessingService
{
    public const long MaxSampleBytes = 10 * 1024 * 1024;

    private readonly IAmazonBatch _batch;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<VoiceSampleProcessingService> _logger;
    private string JobQueue => _config["Firm:BatchJobQueue"] ?? "firm-transcription-queue";
    private string JobDefinition => _config["Firm:BatchJobDefinition"] ?? "firm-transcription-job";
    private string S3Bucket => _config["Firm:S3Bucket"] ?? "firm-recordings-dev";

    public VoiceSampleProcessingService(IAmazonBatch batch, IAmazonS3 s3, IConfiguration config, ILogger<VoiceSampleProcessingService> logger)
    {
        _batch = batch;
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    // Returns the Batch job id. Extraction runs in the background — callers poll the enrollment status.
    public async Task<string> SubmitAsync(string entraOid, string displayName, byte[] audio, CancellationToken ct = default)
    {
        if (audio.Length == 0)
            throw new ArgumentException("Voice sample is empty", nameof(audio));
        if (audio.Length > MaxSampleBytes)
            throw new ArgumentException("Voice sample is too large", nameof(audio));

        var tenantId = _config["Firm:GraphTenantId"] ?? "";
        if (string.IsNullOrEmpty(tenantId))
            throw new InvalidOperationException("Firm:GraphTenantId is not configured");

        var s3Key = $"voice-samples/{entraOid}/{Guid.NewGuid()}.webm";
        using (var body = new MemoryStream(audio))
        {
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = S3Bucket,
                Key = s3Key,
                InputStream = body,
                ContentType = "audio/webm"
            }, ct);
        }
        _logger.LogInformation("FIRM: Voice sample for {EntraOid} uploaded to s3://{Bucket}/{Key} ({Bytes} bytes)", entraOid, S3Bucket, s3Key, audio.Length);

        var envVars = new List<Amazon.Batch.Model.KeyValuePair>
        {
            new Amazon.Batch.Model.KeyValuePair { Name = "JOB_MODE", Value = "voice_sample" },
            new Amazon.Batch.Model.KeyValuePair { Name = "VOICE_SAMPLE_S3_KEY", Value = s3Key },
            new Amazon.Batch.Model.KeyValuePair { Name = "ENTRA_OID", Value = entraOid },
            new Amazon.Batch.Model.KeyValuePair { Name = "TENANT_ID", Value = tenantId },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_SPEAKER_EMBEDDING_NAME", Value = displayName },
            new Amazon.Batch.Model.KeyValuePair { Name = "S3_BUCKET", Value = S3Bucket },
            new Amazon.Batch.Model.KeyValuePair { Name = "PYANNOTE_CACHE", Value = "/app/.cache/huggingface/hub" },
            // FORTRESS_DB_PASS comes from the job definition's Secrets Manager mapping (see BatchTranscriptionService).
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_HOST", Value = _config["FORTRESS_DB_HOST"] ?? "" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_PORT", Value = _config["FORTRESS_DB_PORT"] ?? "3306" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FORTRESS_DB_USER", Value = _config["FORTRESS_DB_USER"] ?? "fortress_mysql" },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_DB_NAME", Value = _config["FIRM_DB_NAME"] ?? "firm_dev" },
        };

        var response = await _batch.SubmitJobAsync(new SubmitJobRequest
        {
            JobName = $"voice-sample-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            JobQueue = JobQueue,
            JobDefinition = JobDefinition,
            ContainerOverrides = new ContainerOverrides { Environment = envVars }
        }, ct);
        _logger.LogInformation("FIRM: Voice sample Batch job {JobId} submitted for {EntraOid}", response.JobId, entraOid);
        return response.JobId;
    }
}
