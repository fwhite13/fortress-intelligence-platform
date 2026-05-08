using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FortressAI.V2.Web.Services;

public interface IBraveSearchService
{
    Task<List<BraveSearchResult>> SearchAsync(string query, int count = 5, CancellationToken ct = default);
    string FormatResults(List<BraveSearchResult> results);
    bool IsConfigured { get; }
}

public class BraveSearchResult
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class BraveWebResults
{
    [JsonPropertyName("results")]
    public List<BraveSearchResult> Results { get; set; } = new();
}

public class BraveSearchResponse
{
    [JsonPropertyName("web")]
    public BraveWebResults? Web { get; set; }
}

/// <summary>
/// Direct Brave Search API client.
/// Replaces fip-mcp's web-search tool group with a direct API key call.
/// Config key: BraveSearch:ApiKey
/// </summary>
public class BraveSearchService : IBraveSearchService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<BraveSearchService> _logger;

    public bool IsConfigured => !string.IsNullOrEmpty(_apiKey);

    public BraveSearchService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<BraveSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = config["BraveSearch:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<BraveSearchResult>> SearchAsync(string query, int count = 5, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Brave Search API key not configured — returning empty results");
            return new List<BraveSearchResult>();
        }

        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={Math.Min(count, 10)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Subscription-Token", _apiKey);

        try
        {
            var http = _httpClientFactory.CreateClient("BraveSearchClient");
            var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<BraveSearchResponse>(cancellationToken: ct);
            return result?.Web?.Results ?? new List<BraveSearchResult>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brave Search failed for query '{Query}'", query);
            return new List<BraveSearchResult>();
        }
    }

    public string FormatResults(List<BraveSearchResult> results)
    {
        if (!results.Any()) return "No results found.";

        var sb = new System.Text.StringBuilder();
        int num = 1;
        foreach (var r in results.Take(5))
        {
            sb.AppendLine($"{num}. {r.Title}");
            sb.AppendLine($"   URL: {r.Url}");
            if (!string.IsNullOrEmpty(r.Description))
                sb.AppendLine($"   {r.Description}");
            sb.AppendLine();
            num++;
        }
        return sb.ToString().TrimEnd();
    }
}
