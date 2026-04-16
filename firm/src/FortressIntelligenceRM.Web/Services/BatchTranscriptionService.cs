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
    private readonly IOrgContextService _orgContextService;
    private readonly ILogger<BatchTranscriptionService> _logger;
    private const string JobQueue = "firm-transcription-queue";
    private const string JobDefinition = "firm-transcription-job";

    public BatchTranscriptionService(IAmazonBatch batch, IConfiguration config, IOrgContextService orgContextService, ILogger<BatchTranscriptionService> logger)
    {
        _batch = batch;
        _config = config;
        _orgContextService = orgContextService;
        _logger = logger;
    }

    public async Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, CancellationToken ct = default)
    {
        var callbackSecret = _config["Firm:BotCallbackSecret"] ?? "";
        if (string.IsNullOrEmpty(callbackSecret))
            _logger.LogWarning("FIRM: BotCallbackSecret is not configured — Batch job callback will return 401");

        string? orgWikiJson = null;
        var tenantId = _config["Firm:GraphTenantId"] ?? "";
        if (!string.IsNullOrEmpty(tenantId))
        {
            var orgEntries = await _orgContextService.GetContextAsync(tenantId);
            orgWikiJson = orgEntries.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(orgEntries)
                : null;
        }

        var envVars = new List<Amazon.Batch.Model.KeyValuePair>
        {
            new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_ID", Value = meetingId.ToString() },
            new Amazon.Batch.Model.KeyValuePair { Name = "AUDIO_S3_KEY", Value = audioS3Key },
            new Amazon.Batch.Model.KeyValuePair { Name = "BOT_CALLBACK_SECRET", Value = callbackSecret },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_CALLBACK_URL", Value = _config["Firm:CallbackUrl"] ?? "http://firm.fip.internal:8080/api/vp/callback" },
        };

        if (orgWikiJson != null)
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "ORG_WIKI_JSON", Value = orgWikiJson });

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
