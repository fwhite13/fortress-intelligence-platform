using Amazon.Batch;
using Amazon.Batch.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressIntelligenceRM.Web.Services;

public interface IBatchTranscriptionService
{
    Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, CancellationToken ct = default);
}

public class BatchTranscriptionService : IBatchTranscriptionService
{
    private readonly IAmazonBatch _batch;
    private readonly IConfiguration _config;
    private readonly ILogger<BatchTranscriptionService> _logger;
    private const string JobQueue = "firm-transcription-queue";
    private const string JobDefinition = "firm-transcription-job";

    public BatchTranscriptionService(IAmazonBatch batch, IConfiguration config, ILogger<BatchTranscriptionService> logger)
    {
        _batch = batch;
        _config = config;
        _logger = logger;
    }

    public async Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, CancellationToken ct = default)
    {
        var callbackSecret = _config["Firm:BotCallbackSecret"] ?? "";
        if (string.IsNullOrEmpty(callbackSecret))
            _logger.LogWarning("FIRM: BotCallbackSecret is not configured — Batch job callback will return 401");

        var request = new SubmitJobRequest
        {
            JobName = $"retranscribe-meeting-{meetingId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
            JobQueue = JobQueue,
            JobDefinition = JobDefinition,
            ContainerOverrides = new ContainerOverrides
            {
                Environment =
                [
                    new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_ID", Value = meetingId.ToString() },
                    new Amazon.Batch.Model.KeyValuePair { Name = "AUDIO_S3_KEY", Value = audioS3Key },
                    new Amazon.Batch.Model.KeyValuePair { Name = "BOT_CALLBACK_SECRET", Value = callbackSecret },
                    new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_CALLBACK_URL", Value = _config["Firm:CallbackUrl"] ?? "https://firm.dev.fortressam.ai/api/vp/callback" },
                ]
            }
        };

        var response = await _batch.SubmitJobAsync(request, ct);
        _logger.LogInformation("FIRM: Batch retranscribe job {JobId} submitted for meeting {MeetingId}", response.JobId, meetingId);
        return response.JobId;
    }
}
