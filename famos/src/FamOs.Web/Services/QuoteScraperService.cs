using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;

namespace FamOs.Web.Services;

public interface IQuoteScraperService
{
    /// <summary>Get a presigned S3 upload URL for the PDF.</summary>
    Task<(string uploadUrl, string fileKey)> GetUploadLinkAsync(string fileName, string clientReferenceId);

    /// <summary>Upload the PDF bytes to S3 using the presigned URL.</summary>
    Task UploadToS3Async(string uploadUrl, IBrowserFile file);

    /// <summary>Upload pre-buffered PDF bytes to S3 using the presigned URL.</summary>
    Task UploadBytesToS3Async(string uploadUrl, byte[] fileBytes, string fileName);

    /// <summary>Submit the file to Fortress API for processing. Returns projectRequestId.</summary>
    Task<string> SubmitRequestAsync(string fileKey, string clientReferenceId);

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

    public async Task<(string uploadUrl, string fileKey)> GetUploadLinkAsync(string fileName, string clientReferenceId)
    {
        var client = _factory.CreateClient("FortressApi");

        var linkUrl  = $"/clients/{ClientId}/projects/{ProjectId}/uploadLink";
        var linkBody = new
        {
            clientReferenceId,
            files = new[] { new { fileName, sequence = 1 } }
        };
        var linkResp = await client.PostAsJsonAsync(linkUrl, linkBody, Opts);
        linkResp.EnsureSuccessStatusCode();

        var links = await linkResp.Content.ReadFromJsonAsync<List<UploadLinkDto>>(Opts)
            ?? throw new InvalidOperationException("No upload links returned");

        var link = links.First();
        return (link.UploadUrl!, link.FileKey!);
    }

    public async Task UploadToS3Async(string uploadUrl, IBrowserFile file)
    {
        const long maxSize = 10 * 1024 * 1024;
        using var stream = file.OpenReadStream(maxSize);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        var bytes = ms.ToArray();

        using var s3Client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var s3Resp = await s3Client.PutAsync(uploadUrl, fileContent);
        s3Resp.EnsureSuccessStatusCode();

        _logger.LogInformation("[QuoteScraper] Uploaded {File} ({Bytes} bytes)", file.Name, bytes.Length);
    }

    public async Task UploadBytesToS3Async(string uploadUrl, byte[] fileBytes, string fileName)
    {
        using var s3Client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var s3Resp = await s3Client.PutAsync(uploadUrl, fileContent);
        s3Resp.EnsureSuccessStatusCode();
        _logger.LogInformation("[QuoteScraper] Uploaded {File} ({Bytes} bytes)", fileName, fileBytes.Length);
    }

    public async Task<string> SubmitRequestAsync(string fileKey, string clientReferenceId)
    {
        var client = _factory.CreateClient("FortressApi");

        var submitUrl  = $"/clients/{ClientId}/projects/{ProjectId}/requests";
        var submitBody = new
        {
            clientReferenceId,
            fileKeys = new[] { fileKey }
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

        // Treat unknown/null status as still in-progress — don't exit prematurely
        if (reqStatus == "Unknown")
            return null;

        // In-progress statuses — keep polling
        if (reqStatus is "Pending" or "Processing" or "Assembling" or "Queued"
                        or "Submitted" or "Received" or "InProgress" or "In Progress"
                        or "Sleeping")
            return null;

        // Only return terminal result for explicitly success or failure statuses
        return new QuoteScraperResult { Status = reqStatus, RawJson = raw, Results = status?.Results };
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
