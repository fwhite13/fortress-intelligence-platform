using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace FortressAI.V2.Web.Services;

public class RAGReadService : IRAGReadService
{
    private readonly IConfiguration _config;
    private readonly ILogger<RAGReadService> _logger;
    private readonly AmazonBedrockRuntimeClient _bedrockClient;
    private const string EMBED_MODEL = "amazon.titan-embed-text-v2:0";

    public RAGReadService(IConfiguration config, ILogger<RAGReadService> logger)
    {
        _config = config;
        _logger = logger;
        _bedrockClient = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);
    }

    public Task WarmupAsync(string userId, CancellationToken ct = default)
    {
        // Warmup stub — no-op for now (OQ-15-2)
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        string userId, string query, int topK = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(query))
            return Array.Empty<MemorySearchResult>();

        try
        {
            var embedding = await GetEmbeddingAsync(query, ct);
            return await QuerySimilarChunksAsync(userId, embedding, topK, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAGReadService: SearchAsync failed for user={UserId}", userId);
            return Array.Empty<MemorySearchResult>();
        }
    }

    private async Task<float[]> GetEmbeddingAsync(string text, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { inputText = text });
        var request = new InvokeModelRequest
        {
            ModelId = EMBED_MODEL,
            ContentType = "application/json",
            Accept = "application/json",
            Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body))
        };
        var response = await _bedrockClient.InvokeModelAsync(request, ct);
        using var reader = new StreamReader(response.Body);
        var json = await reader.ReadToEndAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var embeddingArray = doc.RootElement.GetProperty("embedding");
        return embeddingArray.EnumerateArray().Select(e => e.GetSingle()).ToArray();
    }

    private async Task<IReadOnlyList<MemorySearchResult>> QuerySimilarChunksAsync(
        string userId, float[] embedding, int topK, CancellationToken ct)
    {
        var connStr = _config["MCP_MEMORY_DB"] ?? _config["ConnectionStrings:McpMemoryDb"]
            ?? throw new InvalidOperationException("MCP_MEMORY_DB not configured");

        var vectorStr = "[" + string.Join(",", embedding.Select(f => f.ToString("G"))) + "]";

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(@"
            SELECT topic_slug, source, content,
                   1 - (embedding <=> @embedding::vector) AS similarity
            FROM memory_chunks
            WHERE user_id = @userId
            ORDER BY embedding <=> @embedding::vector
            LIMIT @topK",
            conn);
        cmd.Parameters.AddWithValue("userId", userId);
        cmd.Parameters.AddWithValue("embedding", NpgsqlDbType.Text, vectorStr);
        cmd.Parameters.AddWithValue("topK", topK);

        var results = new List<MemorySearchResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new MemorySearchResult(
                TopicSlug: reader.GetString(0),
                Source: reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Content: reader.GetString(2),
                Similarity: reader.GetDouble(3)
            ));
        }
        return results;
    }
}
