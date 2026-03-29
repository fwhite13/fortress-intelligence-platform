using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Services;

public class FirmMicrosoftTokenService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<FirmMicrosoftTokenService> _logger;
    private readonly HttpClient _httpClient;

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

    public FirmMicrosoftTokenService(
        IDbContextFactory<FirmDbContext> dbFactory,
        ILogger<FirmMicrosoftTokenService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        // FIRM uses Firm:Graph* config keys (mapped from ECS env vars Firm__GraphClientId etc.)
        _clientId = configuration["Firm:GraphClientId"] ?? "";
        _tenantId = (configuration["Firm:GraphTenantId"] ?? "").Trim().TrimEnd('/');
        _clientSecret = configuration["Firm:GraphClientSecret"] ?? "";
        _useStubAuth = configuration.GetValue<bool>("UseStubAuth", false);

        if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_tenantId) || string.IsNullOrEmpty(_clientSecret))
            _logger.LogWarning("[FirmMicrosoftTokenService] Firm:GraphClientId/TenantId/ClientSecret not configured — delegated Graph token refresh will fail");
    }

    /// <summary>
    /// Returns a valid access token for the given FAIT user ID, auto-refreshing if needed.
    /// Returns null if no token exists or refresh fails.
    /// </summary>
    public async Task<string?> GetValidAccessTokenAsync(Guid userId)
    {
        if (_useStubAuth)
        {
            _logger.LogInformation("[FirmMicrosoftTokenService] Stub auth: returning mock token for user {UserId}", userId);
            return "STUB_TOKEN_NOT_FOR_REAL_API_CALLS";
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(userId);
        if (token == null)
        {
            _logger.LogWarning("[FirmMicrosoftTokenService] No Microsoft token found for user {UserId}", userId);
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
            _logger.LogInformation("[FirmMicrosoftTokenService] Refreshing Microsoft token for user {UserId}", userId);
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
            {
                token.RefreshToken = newRefresh.GetString()!;
            }
            token.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            _logger.LogInformation("[FirmMicrosoftTokenService] Token refreshed for user {UserId}, new expiry: {Expiry}", userId, token.ExpiresAt);
            return token.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FirmMicrosoftTokenService] Failed to refresh token for user {UserId}", userId);
            db.UserMicrosoftTokens.Remove(token);
            await db.SaveChangesAsync();
            return null;
        }
    }
}
