using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;

namespace FortressNexus.Web.Services.Discovery;

public class BedrockKnowledgeBaseService : IKnowledgeBaseService
{
    private readonly IAmazonBedrockAgentRuntime _client;
    private readonly IConfiguration _config;
    private readonly ILogger<BedrockKnowledgeBaseService> _logger;

    // TODO: Replace with actual FORGE KB ID once confirmed by Fred
    private const string FallbackKbId = "TODO_FORGE_KB_ID";

    public BedrockKnowledgeBaseService(
        IAmazonBedrockAgentRuntime client,
        IConfiguration config,
        ILogger<BedrockKnowledgeBaseService> logger)
    {
        _client = client;
        _config = config;
        _logger = logger;
    }

    public async Task<IEnumerable<KbPassage>> RetrieveAsync(string query, int maxResults = 5,
        CancellationToken ct = default)
    {
        try
        {
            var kbId = _config["Nexus:DiscoveryKnowledgeBaseId"] ?? FallbackKbId;

            _logger.LogInformation("[KB_RETRIEVE] Query: {Query}, KB: {KbId}", query, kbId);

            var request = new RetrieveRequest
            {
                KnowledgeBaseId = kbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = maxResults
                    }
                }
            };

            var response = await _client.RetrieveAsync(request, ct);

            var passages = response.RetrievalResults
                .Select(r => new KbPassage(
                    r.Content.Text,
                    r.Location?.S3Location?.Uri ?? "unknown",
                    r.Score))
                .ToList();

            _logger.LogInformation("[KB_RETRIEVE] Retrieved {Count} passages", passages.Count);
            return passages;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KB_RETRIEVE] KB retrieval failed for query {Query} — degrading to empty context", query);
            return Enumerable.Empty<KbPassage>();
        }
    }
}
