using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FamOs.Web.Services;

public interface IQuoteScraperService
{
    /// <summary>
    /// Upload a carrier quote PDF, submit to Fortress API, and return the projectRequestId.
    /// Call PollResultAsync to check completion.
    /// </summary>
    Task<string> SubmitQuotePdfAsync(string opportunityRefId, string fileName, byte[] fileData);

    /// <summary>Poll for scraper results. Returns null if still processing.</summary>
    Task<QuoteScraperResult?> PollResultAsync(string projectRequestId);
}

public class QuoteScraperService : IQuoteScraperService
{
    private readonly IHttpClientFactory _factory;
    private readonly IConfiguration     _config;
    private readonly ILogger<QuoteScraperService> _logger;

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    private const string ClientId  = "internal";
    private const string ProjectId = "internal_quote_scraper_cataloger";

    public QuoteScraperService(IHttpClientFactory factory, IConfiguration config,
        ILogger<QuoteScraperService> logger)
    {
        _factory = factory;
        _config  = config;
        _logger  = logger;
    }

    public async Task<string> SubmitQuotePdfAsync(
        string opportunityRefId, string fileName, byte[] fileData)
    {
        var client = _factory.CreateClient("FortressApi");

        // Step 1: Get upload link
        var linkUrl  = $"/clients/{ClientId}/projects/{ProjectId}/uploadLink";
        var linkBody = new
        {
            clientReferenceId = opportunityRefId,
            files = new[] { new { fileName, sequence = 1 } }
        };
        var linkResp = await client.PostAsJsonAsync(linkUrl, linkBody, Opts);
        linkResp.EnsureSuccessStatusCode();

        var links = await linkResp.Content.ReadFromJsonAsync<List<UploadLinkDto>>(Opts)
            ?? throw new InvalidOperationException("No upload links returned");

        var link = links.First();

        // Step 2: Upload to S3 (no auth headers)
        using var s3Client  = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var fileContent     = new ByteArrayContent(fileData);
        fileContent.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var s3Resp = await s3Client.PutAsync(link.UploadUrl, fileContent);
        s3Resp.EnsureSuccessStatusCode();

        _logger.LogInformation("[QuoteScraper] Uploaded {File} ({Bytes} bytes)", fileName, fileData.Length);

        // Step 3: Submit request
        var submitUrl  = $"/clients/{ClientId}/projects/{ProjectId}/requests";
        var submitBody = new
        {
            clientReferenceId = opportunityRefId,
            fileKeys          = new[] { link.FileKey }
        };
        var submitResp = await client.PostAsJsonAsync(submitUrl, submitBody, Opts);
        submitResp.EnsureSuccessStatusCode();

        var submit = await submitResp.Content.ReadFromJsonAsync<SubmitResponseDto>(Opts)
            ?? throw new InvalidOperationException("No submit response");

        if (submit.Success != true)
            throw new InvalidOperationException($"Scraper submission failed: {submit.Reason}");

        _logger.LogInformation("[QuoteScraper] Submitted request {Id}", submit.ProjectRequestId);
        return submit.ProjectRequestId!;
    }

    public async Task<QuoteScraperResult?> PollResultAsync(string projectRequestId)
    {
        var client = _factory.CreateClient("FortressApi");
        var url    = $"/clients/{ClientId}/projects/{ProjectId}/requests/{projectRequestId}";

        var resp = await client.GetAsync(url);
        resp.EnsureSuccessStatusCode();

        var raw    = await resp.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<StatusResponseDto>(raw, Opts);

        var reqStatus = status?.Request?.Status ?? "Unknown";
        // Treat all non-terminal statuses as in-progress
        if (reqStatus is "Pending" or "Processing" or "Assembling" or "Queued"
                        or "Submitted" or "Received" or "InProgress" or "In Progress")
            return null;  // still working

        return new QuoteScraperResult
        {
            Status  = reqStatus,
            RawJson = raw,
            Results = status?.Results
        };
    }

    // ── DTOs ──────────────────────────────────────────────────────────────

    private class UploadLinkDto
    {
        public string? FileName  { get; set; }
        public string? FileKey   { get; set; }
        public string? UploadUrl { get; set; }
    }

    private class SubmitResponseDto
    {
        public bool?   Success          { get; set; }
        public string? ProjectRequestId { get; set; }
        public string? Reason           { get; set; }
    }

    private class StatusResponseDto
    {
        public RequestInfoDto? Request { get; set; }
        public object?         Results { get; set; }
    }

    private class RequestInfoDto
    {
        public string? Status { get; set; }
    }
}

public class QuoteScraperResult
{
    public string  Status  { get; set; } = "";
    public string  RawJson { get; set; } = "";
    public object? Results { get; set; }
}
