using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FortressAI.V2.Web.Services;

public class ForgeKbService : IForgeKbService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFipTokenProvider _tokenProvider;
    private readonly IConfiguration _config;
    private readonly ILogger<ForgeKbService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ForgeKbService(
        IHttpClientFactory httpClientFactory,
        IFipTokenProvider tokenProvider,
        IConfiguration config,
        ILogger<ForgeKbService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _config = config;
        _logger = logger;
    }

    // ── IForgeKbService ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<KbInfo>> ListKbsAsync(string entraOid, CancellationToken ct = default)
    {
        var result = await CallToolAsync("list_kbs", new { entra_oid = entraOid }, ct);
        if (result == null) return Array.Empty<KbInfo>();

        try
        {
            var items = JsonSerializer.Deserialize<List<KbInfoDto>>(result, JsonOpts) ?? new();
            return items.Select(x => new KbInfo(x.KbId, x.KbType, x.Description, x.Writable)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize list_kbs response: {Raw}", result);
            return Array.Empty<KbInfo>();
        }
    }

    public async Task<IReadOnlyList<KbSearchResult>> SearchKbAsync(string kbId, string query, int topK = 5, CancellationToken ct = default)
    {
        var result = await CallToolAsync("search_kb", new { kb_id = kbId, query, top_k = topK }, ct);
        if (result == null) return Array.Empty<KbSearchResult>();

        try
        {
            var items = JsonSerializer.Deserialize<List<KbSearchResultDto>>(result, JsonOpts) ?? new();
            return items.Select(x => new KbSearchResult(x.Content, x.Metadata ?? new object(), x.RelevanceScore)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize search_kb response: {Raw}", result);
            return Array.Empty<KbSearchResult>();
        }
    }

    public async Task<string> AddToKbAsync(string kbId, string content, Dictionary<string, string> metadata, CancellationToken ct = default)
    {
        var result = await CallToolAsync("add_to_kb", new { kb_id = kbId, content, metadata }, ct);
        return result ?? string.Empty;
    }

    public async Task<KbMetadata> GetKbMetadataAsync(string kbId, CancellationToken ct = default)
    {
        var result = await CallToolAsync("get_kb_metadata", new { kb_id = kbId }, ct);
        if (result == null) return new KbMetadata(kbId, "unknown", 0, DateTime.UtcNow, string.Empty);

        try
        {
            var dto = JsonSerializer.Deserialize<KbMetadataDto>(result, JsonOpts);
            if (dto == null) return new KbMetadata(kbId, "unknown", 0, DateTime.UtcNow, string.Empty);
            return new KbMetadata(dto.KbId, dto.KbType, dto.DocumentCount, dto.LastUpdated, dto.DataSourceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize get_kb_metadata response: {Raw}", result);
            return new KbMetadata(kbId, "unknown", 0, DateTime.UtcNow, string.Empty);
        }
    }

    // ── MCP JSON-RPC transport ────────────────────────────────────────────

    private async Task<string?> CallToolAsync(string toolName, object arguments, CancellationToken ct)
    {
        var endpointUrl = _config["FipMcp:EndpointUrl"]?.TrimEnd('/')
            ?? throw new InvalidOperationException("FipMcp:EndpointUrl not configured");

        var token = await _tokenProvider.GetAccessTokenAsync();

        var rpcId = Guid.NewGuid().ToString();
        var body = new
        {
            jsonrpc = "2.0",
            id = rpcId,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments
            }
        };

        var json = JsonSerializer.Serialize(body);
        var client = _httpClientFactory.CreateClient("FipMcpClient");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpointUrl}/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTTP call to fip-mcp failed for tool {Tool}", toolName);
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("fip-mcp returned {Status} for tool {Tool}", response.StatusCode, toolName);
            return null;
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        return ExtractToolResult(responseJson, toolName);
    }

    private string? ExtractToolResult(string responseJson, string toolName)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // MCP standard: result.content[0].text
            if (root.TryGetProperty("result", out var result) &&
                result.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.Array &&
                content.GetArrayLength() > 0)
            {
                var first = content[0];
                if (first.TryGetProperty("text", out var text))
                    return text.GetString();
            }

            // If response has "error", log it
            if (root.TryGetProperty("error", out var error))
            {
                _logger.LogWarning("fip-mcp tool {Tool} returned error: {Error}", toolName, error.ToString());
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse fip-mcp response for tool {Tool}", toolName);
            return null;
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    private sealed class KbInfoDto
    {
        public string KbId { get; set; } = string.Empty;
        public string KbType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool Writable { get; set; }
    }

    private sealed class KbSearchResultDto
    {
        public string Content { get; set; } = string.Empty;
        public object? Metadata { get; set; }
        public double RelevanceScore { get; set; }
    }

    private sealed class KbMetadataDto
    {
        public string KbId { get; set; } = string.Empty;
        public string KbType { get; set; } = string.Empty;
        public int DocumentCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public string DataSourceId { get; set; } = string.Empty;
    }
}
