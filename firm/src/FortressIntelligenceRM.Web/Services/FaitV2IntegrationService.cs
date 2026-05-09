using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressIntelligenceRM.Web.Services;

public interface IFaitV2IntegrationService
{
    Task SendTranscriptAsync(string entraOid, string meetingId, string transcript, string? title = null, CancellationToken ct = default);
}

public class FaitV2IntegrationService : IFaitV2IntegrationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<FaitV2IntegrationService> _logger;

    public FaitV2IntegrationService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<FaitV2IntegrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task SendTranscriptAsync(string entraOid, string meetingId, string transcript, string? title = null, CancellationToken ct = default)
    {
        var baseUrl = _config["FaitV2:BaseUrl"];
        var secret = _config["FaitV2:SharedSecret"];
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(secret))
        {
            _logger.LogWarning("FaitV2 integration not configured — skipping inject for meeting {MeetingId}", meetingId);
            return;
        }

        var http = _httpClientFactory.CreateClient("FaitV2Client");

        var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/assistant/inject")
        {
            Content = JsonContent.Create(new
            {
                entraOid,
                content = transcript,
                sourceType = "firm",
                sourceId = meetingId,
                title = title ?? $"Meeting {meetingId}"
            })
        };
        request.Headers.Add("X-Firm-Secret", secret);

        try
        {
            var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                _logger.LogWarning("FaitV2 inject returned {Status} for meeting {MeetingId}", response.StatusCode, meetingId);
            else
                _logger.LogInformation("FaitV2 inject succeeded for meeting {MeetingId}", meetingId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FaitV2 inject failed for meeting {MeetingId}", meetingId);
        }
    }
}
