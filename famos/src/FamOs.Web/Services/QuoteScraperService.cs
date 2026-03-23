using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;

namespace FamOs.Web.Services;

public interface IQuoteScraperService
{
    /// <summary>Get a presigned S3 upload URL for the PDF.</summary>
    Task<(string uploadUrl, string fileKey)> GetUploadLinkAsync(string fileName, string clientReferenceId);

    /// <summary>Upload pre-buffered PDF bytes to S3 using the presigned URL.</summary>
    Task UploadBytesToS3Async(string uploadUrl, byte[] fileBytes, string fileName, Guid submissionId, Guid opportunityId, string carrierName);

    /// <summary>Submit the file to Fortress API for processing. Returns projectRequestId.</summary>
    Task<string> SubmitRequestAsync(string fileKey, string clientReferenceId, Guid submissionId);

    /// <summary>Poll for scraper results. Returns null if still processing.</summary>
    Task<QuoteScraperResult?> PollResultAsync(string projectRequestId, Guid submissionId, int cycleNumber, DateTime pollStart);
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

    public async Task UploadBytesToS3Async(string uploadUrl, byte[] fileBytes, string fileName, Guid submissionId, Guid opportunityId, string carrierName)
    {
        _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} opp={OppId} carrier={Carrier} file={File} bytes={Bytes}",
            "UPLOAD_START", submissionId, opportunityId, carrierName, fileName, fileBytes.Length);

        using var s3Client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        var s3Resp = await s3Client.PutAsync(uploadUrl, fileContent);
        s3Resp.EnsureSuccessStatusCode();

        _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} file={File} bytes={Bytes}",
            "UPLOAD_S3_OK", submissionId, fileName, fileBytes.Length);
    }

    public async Task<string> SubmitRequestAsync(string fileKey, string clientReferenceId, Guid submissionId)
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

        _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} requestId={RequestId} fileKey={FileKey} refId={RefId}",
            "SUBMIT_OK", submissionId, submit.ProjectRequestId, fileKey, clientReferenceId);
        return submit.ProjectRequestId!;
    }

    public async Task<QuoteScraperResult?> PollResultAsync(string projectRequestId, Guid submissionId, int cycleNumber, DateTime pollStart)
    {
        var client = _factory.CreateClient("FortressApi");
        var url    = $"/clients/{ClientId}/projects/{ProjectId}/requests/{projectRequestId}";

        HttpResponseMessage resp;
        const int maxRetries = 3;
        for (int attempt = 1; ; attempt++)
        {
            resp = await client.GetAsync(url);

            if (resp.IsSuccessStatusCode)
                break;

            // Only retry 502/503/504 — all other errors fail immediately
            var statusCode = (int)resp.StatusCode;
            if (statusCode < 500 || statusCode is not (502 or 503 or 504))
            {
                resp.EnsureSuccessStatusCode();
            }

            // Gateway errors (502, 503, 504) — retry with backoff
            if (attempt >= maxRetries)
            {
                _logger.LogError("[QuoteScraper] {Step} sub={SubId} requestId={RequestId} attempt={Attempt} status={Status}",
                    "POLL_RETRY_EXHAUSTED", submissionId, projectRequestId, attempt, statusCode);
                resp.EnsureSuccessStatusCode(); // throws after all retries exhausted
                break;
            }

            _logger.LogWarning("[QuoteScraper] {Step} sub={SubId} requestId={RequestId} attempt={Attempt} status={Status} retryInSec={RetryInSec}",
                "POLL_RETRY", submissionId, projectRequestId, attempt, statusCode, 3);

            await Task.Delay(3000);
        }

        var raw    = await resp.Content.ReadAsStringAsync();
        var status = JsonSerializer.Deserialize<StatusResponseDto>(raw, Opts);

        var reqStatus = status?.Request?.Status ?? "Unknown";
        var elapsed   = DateTime.UtcNow - pollStart;
        var rawTrunc  = raw.Length > 300 ? raw[..300] + "..." : raw;

        _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} requestId={RequestId} cycle={Cycle} status={Status} elapsed={ElapsedSec}s rawResponse={Raw}",
            "POLL_CYCLE", submissionId, projectRequestId, cycleNumber, reqStatus, (int)elapsed.TotalSeconds, rawTrunc);

        // If status is Completed but results are missing/empty, treat as still processing
        if (reqStatus is "Completed" or "Complete")
        {
            // If no results object at all, keep polling
            if (status?.Results == null)
                return null;

            // JsonElement check: {} or [] or null/undefined JsonElement = no real results yet
            if (status.Results is JsonElement el &&
                (el.ValueKind == JsonValueKind.Null ||
                 el.ValueKind == JsonValueKind.Undefined ||
                 (el.ValueKind == JsonValueKind.Object && !el.EnumerateObject().Any()) ||
                 (el.ValueKind == JsonValueKind.Array  && el.GetArrayLength() == 0)))
                return null;

            var pageCount = status?.Request?.PageCount ?? -1;
            _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} requestId={RequestId} finalStatus={Status} totalCycles={Cycles} elapsedSec={Elapsed} pageCount={PageCount}",
                "TERMINAL", submissionId, projectRequestId, reqStatus, cycleNumber, (int)elapsed.TotalSeconds, pageCount);
            return new QuoteScraperResult
            {
                Status          = reqStatus,
                RawJson         = raw,
                Results         = status?.Results,
                IsUploadFailure = pageCount == 0,
                PageCount       = pageCount,
            };
        }

        // For failure statuses (including Timeout/TimedOut) — return immediately
        if (reqStatus is "Failed" or "Failure" or "failure" or "Error" or "Errored" or "error" or "failed"
                      or "Timeout" or "TimedOut")
        {
            var pageCount = status?.Request?.PageCount ?? -1;
            _logger.LogInformation("[QuoteScraper] {Step} sub={SubId} requestId={RequestId} finalStatus={Status} totalCycles={Cycles} elapsedSec={Elapsed} pageCount={PageCount}",
                "TERMINAL", submissionId, projectRequestId, reqStatus, cycleNumber, (int)elapsed.TotalSeconds, pageCount);
            return new QuoteScraperResult
            {
                Status          = reqStatus,
                RawJson         = raw,
                Results         = status?.Results,
                IsUploadFailure = false,
                PageCount       = pageCount,
            };
        }

        // All other statuses: keep polling
        return null;
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
        public string? Status    { get; set; }
        public int     PageCount { get; set; }
    }
}

public class QuoteScraperResult
{
    public string  Status          { get; set; } = "";
    public string  RawJson         { get; set; } = "";
    public object? Results         { get; set; }
    public bool    IsUploadFailure { get; set; }
    public int     PageCount       { get; set; }
}
