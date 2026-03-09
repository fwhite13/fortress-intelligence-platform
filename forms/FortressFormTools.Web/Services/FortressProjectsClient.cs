using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FortressFormTools.Web.Services;

/// <summary>
/// Implementation of IFortressProjectsClient that calls the Fortress AI API.
/// API flow: get upload links → upload files to S3 → submit request → poll status.
/// </summary>
public class FortressProjectsClient : IFortressProjectsClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FortressProjectsClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FortressProjectsClient(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<FortressProjectsClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    private string ClientId => _config["FortressApi:ClientId"] ?? "internal";
    private string ProjectId => _config["FortressApi:ProjectId"] ?? "internal_quote_scraper_cataloger";

    public async Task<List<UploadLinkResult>> GetUploadLinksAsync(string clientReferenceId, List<string> fileNames)
    {
        var client = _httpClientFactory.CreateClient("FortressApi");
        var url = $"/clients/{ClientId}/projects/{ProjectId}/uploadLink";

        var payload = new
        {
            clientReferenceId,
            files = fileNames.Select((name, i) => new { fileName = name, sequence = i + 1 }).ToList()
        };

        _logger.LogInformation("Requesting upload links for {Count} files, ref={Ref}", fileNames.Count, clientReferenceId);

        var response = await client.PostAsJsonAsync(url, payload, JsonOpts);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var links = JsonSerializer.Deserialize<List<UploadLinkResponse>>(json, JsonOpts);

        return links?.Select(l => new UploadLinkResult
        {
            FileName = l.FileName ?? string.Empty,
            FileKey = l.FileKey ?? string.Empty,
            UploadUrl = l.UploadUrl ?? string.Empty
        }).ToList() ?? new List<UploadLinkResult>();
    }

    public async Task UploadFileAsync(string uploadUrl, byte[] fileData, string contentType)
    {
        // Upload directly to S3 presigned URL — no auth headers needed
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var content = new ByteArrayContent(fileData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        _logger.LogInformation("Uploading {Size} bytes to presigned URL", fileData.Length);

        var response = await httpClient.PutAsync(uploadUrl, content);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string> SubmitRequestAsync(string clientReferenceId, List<string> fileKeys)
    {
        var client = _httpClientFactory.CreateClient("FortressApi");
        var url = $"/clients/{ClientId}/projects/{ProjectId}/requests";

        var payload = new
        {
            clientReferenceId,
            fileKeys
        };

        _logger.LogInformation("Submitting request ref={Ref} with {Count} file keys", clientReferenceId, fileKeys.Count);

        var response = await client.PostAsJsonAsync(url, payload, JsonOpts);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<SubmitResponse>(json, JsonOpts);

        if (result?.Success != true)
            throw new InvalidOperationException($"Submission failed: {result?.Reason ?? "unknown"}");

        return result.ProjectRequestId ?? throw new InvalidOperationException("No projectRequestId returned");
    }

    public async Task<ProjectRequestResult> GetRequestStatusAsync(string projectRequestId)
    {
        var client = _httpClientFactory.CreateClient("FortressApi");
        var url = $"/clients/{ClientId}/projects/{ProjectId}/requests/{projectRequestId}";

        _logger.LogDebug("Polling status for request {RequestId}", projectRequestId);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var statusResponse = JsonSerializer.Deserialize<StatusResponse>(json, JsonOpts);

        return new ProjectRequestResult
        {
            Status = statusResponse?.Request?.Status ?? "Unknown",
            ProjectRequestId = projectRequestId,
            Results = statusResponse?.Results,
            RawJson = json
        };
    }

    // ── Response DTOs ──

    private class UploadLinkResponse
    {
        public string? FileName { get; set; }
        public string? FileKey { get; set; }
        public string? UploadUrl { get; set; }
    }

    private class SubmitResponse
    {
        public bool Success { get; set; }
        public string? ProjectRequestId { get; set; }
        public string? Reason { get; set; }
    }

    private class StatusResponse
    {
        public RequestInfo? Request { get; set; }
        public object? Results { get; set; }
    }

    private class RequestInfo
    {
        public string? Status { get; set; }
    }
}
