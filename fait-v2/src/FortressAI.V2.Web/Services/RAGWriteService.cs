using Amazon;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Npgsql;
using System.Text.Json;

namespace FortressAI.V2.Web.Services;

public class RAGWriteService : IRAGWriteService
{
    private readonly IConfiguration _config;
    private readonly ILogger<RAGWriteService> _logger;
    private readonly AmazonBedrockRuntimeClient _bedrockClient;
    private const string EMBED_MODEL = "amazon.titan-embed-text-v2:0";

    public RAGWriteService(IConfiguration config, ILogger<RAGWriteService> logger)
    {
        _config = config;
        _logger = logger;
        _bedrockClient = new AmazonBedrockRuntimeClient(RegionEndpoint.USEast1);
    }

    public Task QueueExtractionAsync(string conversationId, string messageRangeHint, CancellationToken ct = default)
    {
        // Not needed for memory topic sync — no-op
        return Task.CompletedTask;
    }

    public async Task WriteFactAsync(MemoryChunk chunk, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(chunk.Content)) return;
        try
        {
            var embedding = await GetEmbeddingAsync(chunk.Content, ct);
            await UpsertChunkAsync(chunk, embedding, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RAGWriteService: failed to write chunk for user={UserId} topic={Topic}", chunk.UserId, chunk.TopicSlug);
        }
    }

    public async Task MergeToMemoryFileAsync(string userId, string topicSlug, IEnumerable<string> newFacts, CancellationToken ct = default)
    {
        foreach (var fact in newFacts)
        {
            if (string.IsNullOrWhiteSpace(fact)) continue;
            await WriteFactAsync(new MemoryChunk(
                UserId: userId,
                TopicSlug: topicSlug,
                Content: fact,
                Source: "memory_file",
                CreatedAt: DateTimeOffset.UtcNow
            ), ct);
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

    private async Task UpsertChunkAsync(MemoryChunk chunk, float[] embedding, CancellationToken ct)
    {
        var connStr = _config["MCP_MEMORY_DB"] ?? _config["ConnectionStrings:McpMemoryDb"]
            ?? throw new InvalidOperationException("MCP_MEMORY_DB not configured");

        await using var conn = new NpgsqlConnection(connStr);
        await conn.OpenAsync(ct);

        var vectorStr = "[" + string.Join(",", embedding.Select(f => f.ToString("G"))) + "]";
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO memory_chunks (user_id, topic_slug, source, content, embedding, created_at, updated_at)
            VALUES (@userId, @topicSlug, @source, @content, @embedding::vector, NOW(), NOW())
            ON CONFLICT (user_id, topic_slug, source)
            DO UPDATE SET content = EXCLUDED.content, embedding = EXCLUDED.embedding, updated_at = NOW()",
            conn);
        cmd.Parameters.AddWithValue("userId", chunk.UserId);
        cmd.Parameters.AddWithValue("topicSlug", chunk.TopicSlug ?? "");
        cmd.Parameters.AddWithValue("source", chunk.Source ?? "memory_file");
        cmd.Parameters.AddWithValue("content", chunk.Content);
        cmd.Parameters.AddWithValue("embedding", NpgsqlTypes.NpgsqlDbType.Text, vectorStr);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
