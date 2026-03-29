using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Models;
using FortressIntelligenceRM.Web.Data;

namespace FortressIntelligenceRM.Web.Services;

public class MicrosoftTokenService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<MicrosoftTokenService> _logger;
    private readonly HttpClient _httpClient;

    public bool IsConfigured { get; }
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;
    private readonly bool _useStubAuth;

    private static readonly string[] Scopes = new[]
    {
        "https://graph.microsoft.com/Mail.Read",
        "https://graph.microsoft.com/Calendars.Read",
        "https://graph.microsoft.com/User.Read",
        "https://graph.microsoft.com/Tasks.Read",
        "offline_access"
    };

    public MicrosoftTokenService(IDbContextFactory<FirmDbContext> dbFactory, ILogger<MicrosoftTokenService> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        _clientId = configuration["Azure:ClientId"] ?? "";
        _tenantId = (configuration["Azure:TenantId"] ?? "").Trim().TrimEnd('/');
        _clientSecret = configuration["Azure:ClientSecret"] ?? "";
        _useStubAuth = configuration.GetValue<bool>("UseStubAuth", false);
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

    public async Task<UserMicrosoftToken> ExchangeCodeAsync(Guid userId, string code, string redirectUri)
    {
        _logger.LogInformation("Exchanging auth code for user {UserId}", userId);

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
            _logger.LogError("Token exchange failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Token exchange failed: {response.StatusCode}");
        }

        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);

        var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var refreshToken = tokenResponse.GetProperty("refresh_token").GetString()!;
        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

        // Get user email from Graph
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
            _logger.LogWarning(ex, "Failed to get user email from Graph");
        }

        var token = new UserMicrosoftToken
        {
            UserId = userId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            MicrosoftEmail = email,
            UpdatedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.UserMicrosoftTokens.FindAsync(userId);
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
        _logger.LogInformation("MS tokens stored for user {UserId}, email: {Email}, expires: {Expiry}", userId, email, expiresAt);
        return token;
    }

    public async Task<string?> GetValidAccessTokenAsync(Guid userId)
    {
        // Stub mode: return fake token for dev
        if (_useStubAuth)
        {
            _logger.LogInformation("Stub auth: returning mock token for user {UserId}", userId);
            return "STUB_TOKEN_NOT_FOR_REAL_API_CALLS";
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(userId);
        if (token == null)
        {
            _logger.LogWarning("No Microsoft token found for user {UserId}", userId);
            return null;
        }

        // If token is still valid (with 5 min buffer), return it
        if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
        {
            return token.AccessToken;
        }

        // Refresh the token
        try
        {
            _logger.LogInformation("Refreshing Microsoft token for user {UserId}", userId);
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
                db.UserMicrosoftTokens.Remove(token);
                await db.SaveChangesAsync();
                return null;
            }

            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);
            token.AccessToken = tokenResponse.GetProperty("access_token").GetString()!;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(tokenResponse.GetProperty("expires_in").GetInt32());
            if (tokenResponse.TryGetProperty("refresh_token", out var newRefresh))
            {
                token.RefreshToken = newRefresh.GetString()!;
            }
            token.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            _logger.LogInformation("Token refreshed for user {UserId}, new expiry: {Expiry}", userId, token.ExpiresAt);
            return token.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for user {UserId}", userId);
            db.UserMicrosoftTokens.Remove(token);
            await db.SaveChangesAsync();
            return null;
        }
    }

    public async Task<(bool Connected, string? Email, DateTime? ExpiresAt)> GetConnectionStatusAsync(Guid userId)
    {
        if (_useStubAuth)
        {
            return (true, "fred@fortressam.ai (stub)", DateTime.UtcNow.AddDays(365));
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(userId);
        if (token == null)
            return (false, null, null);
        return (true, token.MicrosoftEmail, token.ExpiresAt);
    }

    public async Task DisconnectAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(userId);
        if (token != null)
        {
            db.UserMicrosoftTokens.Remove(token);
            await db.SaveChangesAsync();
            _logger.LogInformation("Microsoft 365 disconnected for user {UserId}", userId);
        }
    }
}
