using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Data;

namespace FortressAI.V2.Web.Services;

public interface IMicrosoftTokenService
{
    bool IsConfigured { get; }
    string GetAuthorizationUrl(string redirectUri, string state);
    Task<string?> GetValidAccessTokenAsync(string entraOid);
    Task<(bool Connected, string? Email, DateTime? ExpiresAt)> GetConnectionStatusAsync(string entraOid);
    Task DisconnectAsync(string entraOid);
}

/// <summary>
/// Per-user Microsoft 365 OAuth token service.
/// Reads delegated Entra tokens from fip_dev.user_microsoft_tokens (written at FIP portal login).
/// Replaces fip-mcp's ms365 tool group with direct token access.
/// Config keys: Azure:ClientId, Azure:TenantId, Azure:ClientSecret
/// </summary>
public class MicrosoftTokenService : IMicrosoftTokenService
{
    private readonly IDbContextFactory<FipPortalDbContext> _fipPortalDbFactory;
    private readonly ILogger<MicrosoftTokenService> _logger;
    private readonly HttpClient _httpClient;

    public bool IsConfigured { get; }
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;

    private static readonly string[] Scopes = new[]
    {
        "https://graph.microsoft.com/Mail.Read",
        "https://graph.microsoft.com/Calendars.Read",
        "https://graph.microsoft.com/User.Read",
        "https://graph.microsoft.com/Tasks.Read",
        "offline_access"
    };

    public MicrosoftTokenService(
        IDbContextFactory<FipPortalDbContext> fipPortalDbFactory,
        ILogger<MicrosoftTokenService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _fipPortalDbFactory = fipPortalDbFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("MicrosoftGraphClient");

        _clientId = configuration["Azure:ClientId"] ?? "";
        _tenantId = (configuration["Azure:TenantId"] ?? "").Trim().TrimEnd('/');
        _clientSecret = configuration["Azure:ClientSecret"] ?? "";
        IsConfigured = !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_tenantId) && !string.IsNullOrEmpty(_clientSecret);

        if (!IsConfigured)
            _logger.LogWarning("Azure:ClientId/TenantId/ClientSecret not configured — Microsoft 365 features disabled");
    }

    public string GetAuthorizationUrl(string redirectUri, string state)
    {
        var scopeString = string.Join(" ", Scopes);
        return $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/authorize" +
               $"?client_id={Uri.EscapeDataString(_clientId)}" +
               $"&response_type=code" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope={Uri.EscapeDataString(scopeString)}" +
               $"&state={Uri.EscapeDataString(state)}" +
               $"&prompt=select_account";
    }

    public async Task<string?> GetValidAccessTokenAsync(string entraOid)
    {
        await using var db = await _fipPortalDbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
        if (token == null)
        {
            _logger.LogWarning("No Microsoft token found for entraOid={EntraOid}", entraOid);
            return null;
        }

        if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return token.AccessToken;

        if (!IsConfigured || string.IsNullOrEmpty(token.RefreshToken))
        {
            _logger.LogWarning("Cannot refresh token for {EntraOid} — missing config or refresh token", entraOid);
            return null;
        }

        try
        {
            _logger.LogInformation("Refreshing Microsoft token for entraOid={EntraOid}", entraOid);
            var tokenEndpoint = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = string.Join(" ", Scopes)
            });

            var response = await _httpClient.PostAsync(tokenEndpoint, content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Token refresh failed: {Status} {Body}", response.StatusCode, body);
                return null;
            }

            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);
            token.AccessToken = tokenResponse.GetProperty("access_token").GetString()!;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.GetProperty("expires_in").GetInt32());
            if (tokenResponse.TryGetProperty("refresh_token", out var newRefresh))
                token.RefreshToken = newRefresh.GetString()!;
            token.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            _logger.LogInformation("Token refreshed for entraOid={EntraOid}, new expiry: {Expiry}", entraOid, token.ExpiresAt);
            return token.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for entraOid={EntraOid}", entraOid);
            return null;
        }
    }

    public async Task<(bool Connected, string? Email, DateTime? ExpiresAt)> GetConnectionStatusAsync(string entraOid)
    {
        await using var db = await _fipPortalDbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
        if (token == null)
            return (false, null, null);
        return (true, token.MicrosoftEmail, token.ExpiresAt);
    }

    public async Task DisconnectAsync(string entraOid)
    {
        await using var db = await _fipPortalDbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
        if (token != null)
        {
            db.UserMicrosoftTokens.Remove(token);
            await db.SaveChangesAsync();
            _logger.LogInformation("Microsoft 365 disconnected for entraOid={EntraOid}", entraOid);
        }
    }
}
