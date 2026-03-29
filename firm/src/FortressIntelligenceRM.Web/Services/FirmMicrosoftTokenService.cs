using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Services;

public class FirmMicrosoftTokenService : IFirmMicrosoftTokenService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<FirmMicrosoftTokenService> _logger;
    private readonly HttpClient _httpClient;

    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;
    private readonly bool _useStubAuth;

    public bool IsConfigured => !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_tenantId) && !string.IsNullOrEmpty(_clientSecret);

    private static readonly string[] Scopes = new[]
    {
        "https://graph.microsoft.com/Mail.Read",
        "https://graph.microsoft.com/Calendars.Read",
        "https://graph.microsoft.com/User.Read",
        "https://graph.microsoft.com/Tasks.Read",
        "offline_access"
    };

    public FirmMicrosoftTokenService(
        IDbContextFactory<FirmDbContext> dbFactory,
        ILogger<FirmMicrosoftTokenService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        _clientId = configuration["Firm:GraphClientId"] ?? "";
        _tenantId = (configuration["Firm:GraphTenantId"] ?? "").Trim().TrimEnd('/');
        _clientSecret = configuration["Firm:GraphClientSecret"] ?? "";
        _useStubAuth = configuration.GetValue<bool>("UseStubAuth", false);

        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_clientSecret))
            _logger.LogWarning("[FirmMicrosoftTokenService] Firm:GraphClientId/TenantId/ClientSecret not configured — delegated Graph token refresh will fail");
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

    public bool HasToken(string firmUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.UserMicrosoftTokens.Any(t => t.UserId == firmUserId);
    }

    public async Task<string?> GetValidAccessTokenAsync(string firmUserId)
    {
        if (_useStubAuth)
        {
            _logger.LogInformation("[FirmMicrosoftTokenService] Stub auth: returning mock token for user {UserId}", firmUserId);
            return "STUB_TOKEN_NOT_FOR_REAL_API_CALLS";
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(firmUserId);
        if (token == null)
        {
            _logger.LogWarning("[FirmMicrosoftTokenService] No Microsoft token found for user {UserId}", firmUserId);
            return null;
        }

        if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return token.AccessToken;

        // Refresh
        try
        {
            _logger.LogInformation("[FirmMicrosoftTokenService] Refreshing Microsoft token for user {UserId}", firmUserId);
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
                _logger.LogError("[FirmMicrosoftTokenService] Token refresh failed: {Status} {Body}", response.StatusCode, body);
                db.UserMicrosoftTokens.Remove(token);
                await db.SaveChangesAsync();
                return null;
            }

            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);
            token.AccessToken = tokenResponse.GetProperty("access_token").GetString()!;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.GetProperty("expires_in").GetInt32());
            if (tokenResponse.TryGetProperty("refresh_token", out var newRefresh))
                token.RefreshToken = newRefresh.GetString()!;
            token.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            _logger.LogInformation("[FirmMicrosoftTokenService] Token refreshed for user {UserId}, new expiry: {Expiry}", firmUserId, token.ExpiresAt);
            return token.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FirmMicrosoftTokenService] Failed to refresh token for user {UserId}", firmUserId);
            db.UserMicrosoftTokens.Remove(token);
            await db.SaveChangesAsync();
            return null;
        }
    }

    public async Task<UserMicrosoftToken> ExchangeCodeAsync(string firmUserId, string code, string redirectUri)
    {
        _logger.LogInformation("[FirmMicrosoftTokenService] Exchanging auth code for user {UserId}", firmUserId);

        var tokenEndpoint = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["grant_type"] = "authorization_code",
            ["scope"] = string.Join(" ", Scopes)
        });

        var response = await _httpClient.PostAsync(tokenEndpoint, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("[FirmMicrosoftTokenService] Token exchange failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Token exchange failed: {response.StatusCode}");
        }

        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);
        var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var refreshToken = tokenResponse.GetProperty("refresh_token").GetString()!;
        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

        string? email = null;
        try
        {
            var graphRequest = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me?$select=mail,userPrincipalName");
            graphRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var graphResponse = await _httpClient.SendAsync(graphRequest);
            if (graphResponse.IsSuccessStatusCode)
            {
                var userJson = JsonSerializer.Deserialize<JsonElement>(await graphResponse.Content.ReadAsStringAsync());
                email = userJson.TryGetProperty("mail", out var mailProp) && mailProp.ValueKind != JsonValueKind.Null
                    ? mailProp.GetString()
                    : userJson.TryGetProperty("userPrincipalName", out var upnProp) ? upnProp.GetString() : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[FirmMicrosoftTokenService] Failed to get user email from Graph");
        }

        var token = new UserMicrosoftToken
        {
            UserId = firmUserId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            MicrosoftEmail = email,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.UserMicrosoftTokens.FindAsync(firmUserId);
        if (existing != null)
        {
            existing.AccessToken = token.AccessToken;
            existing.RefreshToken = token.RefreshToken;
            existing.ExpiresAt = token.ExpiresAt;
            existing.MicrosoftEmail = token.MicrosoftEmail;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.UserMicrosoftTokens.Add(token);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("[FirmMicrosoftTokenService] MS tokens stored for user {UserId}, email: {Email}, expires: {Expiry}", firmUserId, email, expiresAt);
        return existing ?? token;
    }

    public async Task RevokeTokenAsync(string firmUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(firmUserId);
        if (token != null)
        {
            db.UserMicrosoftTokens.Remove(token);
            await db.SaveChangesAsync();
            _logger.LogInformation("[FirmMicrosoftTokenService] Token revoked for user {UserId}", firmUserId);
        }
    }
}
