using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
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
