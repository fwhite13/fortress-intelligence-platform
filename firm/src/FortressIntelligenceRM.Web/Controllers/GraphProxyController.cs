using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Controllers;

[ApiController]
[Route("api/firm")]
[Authorize]
public class GraphProxyController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GraphProxyController> _logger;

    private static string? _cachedToken;
    private static DateTime _tokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

    public GraphProxyController(
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<GraphProxyController> logger)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpGet("graph/teams")]
    public async Task<IActionResult> GetJoinedTeams()
    {
        var clientId = _config["Firm:GraphClientId"] ?? "";
        if (string.IsNullOrEmpty(clientId))
            return Ok(new List<object>());

        try
        {
            var token = await GetAppTokenAsync();
            if (token == null) return Ok(new List<object>());

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync("https://graph.microsoft.com/v1.0/me/joinedTeams");
            if (!response.IsSuccessStatusCode)
                return Ok(new List<object>());

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var teams = doc.RootElement.GetProperty("value").EnumerateArray()
                .Select(t => new
                {
                    id = t.GetProperty("id").GetString(),
                    displayName = t.GetProperty("displayName").GetString()
                })
                .ToList<object>();
            return Ok(teams);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Graph teams call failed, returning empty list");
            return Ok(new List<object>());
        }
    }

    [HttpGet("graph/teams/{teamId}/channels")]
    public async Task<IActionResult> GetTeamChannels(string teamId)
    {
        var clientId = _config["Firm:GraphClientId"] ?? "";
        if (string.IsNullOrEmpty(clientId))
            return Ok(new List<object>());

        try
        {
            var token = await GetAppTokenAsync();
            if (token == null) return Ok(new List<object>());

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"https://graph.microsoft.com/v1.0/teams/{teamId}/channels");
            if (!response.IsSuccessStatusCode)
                return Ok(new List<object>());

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var channels = doc.RootElement.GetProperty("value").EnumerateArray()
                .Select(c => new
                {
                    id = c.GetProperty("id").GetString(),
                    displayName = c.GetProperty("displayName").GetString()
                })
                .ToList<object>();
            return Ok(channels);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Graph channels call failed for team {TeamId}, returning empty list", teamId);
            return Ok(new List<object>());
        }
    }

    /// <summary>
    /// GET /api/firm/calendar/upcoming-meetings
    /// Proxies to FAIT calendar-events endpoint for the current user.
    /// Returns slim DTO with modeHint added by FIRM based on platform.
    /// </summary>
    [HttpGet("calendar/upcoming-meetings")]
    public async Task<IActionResult> GetUpcomingMeetings()
    {
        var faitUrl = _config["FIP:FaitApiUrl"] ?? "";
        var secret = _config["Firm:SharedSecret"] ?? "";

        if (string.IsNullOrEmpty(faitUrl))
            return Ok(new List<object>());

        var oid = User.FindFirstValue("oid")
            ?? User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? "";

        if (string.IsNullOrEmpty(oid))
            return Ok(new List<object>());

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{faitUrl}/api/firm/calendar-events?entraOid={Uri.EscapeDataString(oid)}");
            req.Headers.Add("X-Firm-Secret", secret);
            var resp = await httpClient.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
                return Ok(new List<object>());

            var body = await resp.Content.ReadAsStringAsync();
            var events = JsonSerializer.Deserialize<List<CalendarEventItem>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (events == null) return Ok(new List<object>());

            var enriched = events.Select(e =>
            {
                var modeHint = e.Platform == "teams" ? "A" : "B";
                var modeText = e.Platform switch
                {
                    "teams" => "FIRM will capture the transcript directly.",
                    "zoom" => "Fortress Notetaker will join to record.",
                    "meet" => "Fortress Notetaker will join to record.",
                    _ => "Fortress Notetaker will join to record."
                };
                return new
                {
                    e.CalendarEventId,
                    e.Subject,
                    e.StartDateTime,
                    e.EndDateTime,
                    e.JoinUrl,
                    e.Platform,
                    ModeHint = modeHint,
                    ModeText = modeText,
                };
            }).ToList();

            return Ok(enriched);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Calendar] Failed to fetch upcoming meetings for OID {Oid}", oid);
            return Ok(new List<object>());
        }
    }

    private async Task<string?> GetAppTokenAsync()
    {
        if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-2))
            return _cachedToken;

        await _tokenLock.WaitAsync();
        try
        {
            if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry.AddMinutes(-2))
                return _cachedToken;

            var clientId = _config["Firm:GraphClientId"] ?? "";
            var clientSecret = _config["Firm:GraphClientSecret"] ?? "";
            var tenantId = _config["Firm:GraphTenantId"] ?? "";

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
                return null;

            var client = _httpClientFactory.CreateClient();
            var tokenUrl = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "https://graph.microsoft.com/.default"
            });

            var resp = await client.PostAsync(tokenUrl, body);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var token = doc.RootElement.GetProperty("access_token").GetString();
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            _cachedToken = token;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
            return token;
        }
        catch
        {
            return null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}

internal class CalendarEventItem
{
    public string CalendarEventId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string StartDateTime { get; set; } = "";
    public string EndDateTime { get; set; } = "";
    public string JoinUrl { get; set; } = "";
    public string Platform { get; set; } = "";
}
