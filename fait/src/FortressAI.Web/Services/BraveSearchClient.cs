using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace FortressAI.Web.Services;

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

public class BraveSearchClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _apiKey;
    private readonly ILogger<BraveSearchClient> _logger;

    public BraveSearchClient(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<BraveSearchClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _apiKey = config["BraveSearch:ApiKey"] ?? string.Empty;
        _logger = logger;
    }

    public async Task<List<BraveSearchResult>> SearchAsync(string query, int count = 5)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            _logger.LogWarning("Brave Search API key not configured");
            return new List<BraveSearchResult>();
        }

        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={Math.Min(count, 10)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Subscription-Token", _apiKey);

        var http = _httpClientFactory.CreateClient();
        var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BraveSearchResponse>();
        return result?.Web?.Results ?? new List<BraveSearchResult>();
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
