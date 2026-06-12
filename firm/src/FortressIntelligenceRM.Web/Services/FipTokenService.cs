using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Data;

namespace FortressIntelligenceRM.Web.Services;

public class FipTokenService
{
    private readonly IDbContextFactory<FipDbContext> _dbFactory;
    private readonly ILogger<FipTokenService> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;

    public FipTokenService(IDbContextFactory<FipDbContext> dbFactory, ILogger<FipTokenService> logger, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        // FIP portal credentials must be used for refresh — refresh tokens are bound to the portal's Entra app.
        // FIP:ClientId/Secret are set to the FIP portal's app registration; fall back to AzureAd: for backward compat.
        _clientId = config["FIP:ClientId"] ?? config["AzureAd:ClientId"] ?? "";
        _clientSecret = config["FIP:ClientSecret"] ?? config["AzureAd:ClientSecret"] ?? "";
        _tenantId = (config["AzureAd:TenantId"] ?? "").Trim().TrimEnd('/');
    }

    public async Task<string?> GetValidAccessTokenAsync(string entraOid)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
        if (token == null)
        {
            _logger.LogWarning("[FipTokenService] No token found for OID {Oid}", entraOid);
            return null;
        }

        if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
            return token.AccessToken;

        // Refresh
        try
        {
            var tokenEndpoint = $"https://login.microsoftonline.com/{_tenantId}/oauth2/v2.0/token";
            using var http = new HttpClient();
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["client_secret"] = _clientSecret,
                ["refresh_token"] = token.RefreshToken,
                ["grant_type"] = "refresh_token",
                ["scope"] = "https://graph.microsoft.com/Calendars.Read offline_access"
            });
            var response = await http.PostAsync(tokenEndpoint, content);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[FipTokenService] Refresh failed for OID {Oid}: {Status} {Body}", entraOid, response.StatusCode, body);
                db.UserMicrosoftTokens.Remove(token);
                await db.SaveChangesAsync();
                return null;
            }
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            token.AccessToken = json.GetProperty("access_token").GetString()!;
            token.ExpiresAt = DateTime.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32());
            if (json.TryGetProperty("refresh_token", out var newRefresh))
                token.RefreshToken = newRefresh.GetString()!;
            token.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return token.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[FipTokenService] Exception refreshing token for OID {Oid}", entraOid);
            return null;
        }
    }
}
