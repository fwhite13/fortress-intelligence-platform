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
    [AllowAnonymous]
    public async Task<IActionResult> GetUpcomingMeetings()
    {
        // Internal self-call from Meetings.razor via local HttpClient — validate X-Firm-Secret
        var secret = _config["Firm:SharedSecret"] ?? "";
        var providedSecret = Request.Headers["X-Firm-Secret"].FirstOrDefault();
        // Only enforce secret check when a secret is configured (dev environments may not have it)
        if (!string.IsNullOrEmpty(secret) && providedSecret != secret)
        {
            // Also allow authenticated users (browser requests from logged-in sessions)
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                _logger.LogWarning("[CalendarProxy] Rejected unauthenticated request without valid X-Firm-Secret");
                return Forbid();
            }
        }

        var faitUrl = _config["FIP:FaitApiUrl"] ?? "";

        if (string.IsNullOrEmpty(faitUrl))
            return Ok(new List<object>());

        var oid = User.FindFirstValue("oid")
            ?? User.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier")
            ?? "";

        if (string.IsNullOrEmpty(oid))
            return Ok(new List<object>());

        _logger.LogInformation("[CalendarProxy] Fetching upcoming meetings for OID {Oid}", oid);

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var req = new HttpRequestMessage(HttpMethod.Get,
                $"{faitUrl}/api/firm/calendar-events?entraOid={Uri.EscapeDataString(oid)}");
            req.Headers.Add("X-Firm-Secret", secret);
            var resp = await httpClient.SendAsync(req);
            _logger.LogInformation("[CalendarProxy] FAIT calendar-events returned HTTP {Status} for OID {Oid}", (int)resp.StatusCode, oid);
            if (!resp.IsSuccessStatusCode)
                return Ok(new List<object>());

            var body = await resp.Content.ReadAsStringAsync();
            var events = JsonSerializer.Deserialize<List<CalendarEventItem>>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (events == null) return Ok(new List<object>());

            _logger.LogInformation("[CalendarProxy] FAIT returned {Count} calendar events for OID {Oid}", events?.Count ?? 0, oid);

            // Get logged-in user's email for organizer comparison
            // preferred_username in Cognito is often just the username (e.g. "fwhite"), not a full email.
            // Only use it if it looks like an email address.
            var userEmail = User.FindFirstValue(ClaimTypes.Email)
                ?? User.FindFirstValue("email")
                ?? "";

            if (string.IsNullOrEmpty(userEmail))
            {
                var preferredUsername = User.FindFirstValue("preferred_username") ?? "";
                if (preferredUsername.Contains('@'))
                    userEmail = preferredUsername;
            }

            _logger.LogDebug("FIRM Mode check: resolvedUserEmail={UserEmail}", userEmail);

            var enriched = events.Select(e =>
            {
                var isTeams = e.Platform == "teams";
                var isOrganizer = isTeams
                    && !string.IsNullOrEmpty(e.OrganizerEmail)
                    && !string.IsNullOrEmpty(userEmail)
                    && string.Equals(e.OrganizerEmail, userEmail, StringComparison.OrdinalIgnoreCase);
                _logger.LogDebug("FIRM Mode check: userEmail={UserEmail} organizerEmail={OrganizerEmail} isOrganizer={IsOrganizer}",
                    userEmail, e.OrganizerEmail, isOrganizer);
                var modeHint = isOrganizer ? "A" : "B";
                var modeText = isOrganizer
                    ? "You're the host — FIRM captures natively, no bot joins."
                    : "Fortress Notetaker will join to record.";
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

            _logger.LogInformation("[CalendarProxy] Returning {Count} upcoming meetings for OID {Oid}", enriched.Count, oid);

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
    public string? OrganizerEmail { get; set; }
}
