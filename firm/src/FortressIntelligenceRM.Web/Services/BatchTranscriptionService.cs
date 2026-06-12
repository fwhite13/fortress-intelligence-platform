using Amazon.Batch;
using Amazon.Batch.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressIntelligenceRM.Web.Services;

public interface IBatchTranscriptionService
{
    Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, DateTime? meetingDate = null, string? creatorEntraOid = null, CancellationToken ct = default);
}

public class BatchTranscriptionService : IBatchTranscriptionService
{
    private readonly IAmazonBatch _batch;
    private readonly IConfiguration _config;
    private readonly IOrgContextService _orgContextService;
    private readonly IUserWikiService _userWikiService;
    private readonly ILogger<BatchTranscriptionService> _logger;
    private string JobQueue => _config["Firm:BatchJobQueue"] ?? "firm-transcription-queue";
    private string JobDefinition => _config["Firm:BatchJobDefinition"] ?? "firm-transcription-job";

    public BatchTranscriptionService(IAmazonBatch batch, IConfiguration config, IOrgContextService orgContextService, IUserWikiService userWikiService, ILogger<BatchTranscriptionService> logger)
    {
        _batch = batch;
        _config = config;
        _orgContextService = orgContextService;
        _userWikiService = userWikiService;
        _logger = logger;
    }

    public async Task<string> SubmitTranscriptionJobAsync(long meetingId, string audioS3Key, DateTime? meetingDate = null, string? creatorEntraOid = null, CancellationToken ct = default)
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
            new Amazon.Batch.Model.KeyValuePair { Name = "BOT_CALLBACK_SECRET", Value = callbackSecret },
            new Amazon.Batch.Model.KeyValuePair { Name = "FIRM_CALLBACK_URL", Value = _config["Firm:CallbackUrl"] ?? "http://firm.fip.internal:8080/api/vp/callback" },
            new Amazon.Batch.Model.KeyValuePair { Name = "MEETING_DATE", Value = meetingDate?.ToString("yyyy-MM-dd") ?? "" },
            new Amazon.Batch.Model.KeyValuePair { Name = "BEDROCK_MODEL_ID", Value = _config["Firm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-6" },
            new Amazon.Batch.Model.KeyValuePair { Name = "PYANNOTE_CACHE", Value = "/app/.cache/huggingface/hub" },
            new Amazon.Batch.Model.KeyValuePair { Name = "BEDROCK_MODEL_ID", Value = _config["Firm:BedrockModelId"] ?? "us.anthropic.claude-sonnet-4-6" },
        };

        if (wikiJson != null)
            envVars.Add(new Amazon.Batch.Model.KeyValuePair { Name = "ORG_WIKI_JSON", Value = wikiJson });

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
