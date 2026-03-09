using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using FortressAI.Shared.Models;
using FortressAI.Web.Services.Mcp;

namespace FortressAI.Web.Services;

/// <summary>
/// Handles OAuth token refresh for expired user MCP tokens.
/// Called by McpConnectionService.GetAccessTokenAsync when a token is expired but has a refresh token.
/// </summary>
public class McpTokenRefreshService
{
    private readonly IMcpRegistryService _registry;
    private readonly IDataProtector _dataProtector;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpTokenRefreshService> _logger;

    public McpTokenRefreshService(
        IMcpRegistryService registry,
        IDataProtectionProvider dataProtectionProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<McpTokenRefreshService> logger)
    {
        _registry = registry;
        _dataProtector = dataProtectionProvider.CreateProtector("McpTokens.v1");
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to refresh an expired user token.
    /// Returns the new OAuthTokenResult, or null if refresh fails (caller should let tool call fail gracefully).
    /// </summary>
    /// <param name="server">The MCP server the token belongs to.</param>
    /// <param name="encryptedRefreshToken">The encrypted refresh token from UserMcpToken.RefreshToken.</param>
    public async Task<OAuthTokenResult?> RefreshTokenAsync(McpServer server, string encryptedRefreshToken)
    {
        var oauthConfig = await _registry.GetDecryptedAuthConfigAsync(server.Id);
        if (oauthConfig is null)
        {
            _logger.LogWarning("No OAuth config found for server {ServerId} during token refresh", server.Id);
            return null;
        }

        string refreshToken;
        try
        {
            refreshToken = _dataProtector.Unprotect(encryptedRefreshToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt refresh token for server {ServerId}", server.Id);
            return null;
        }

        var clientSecret = await _registry.GetDecryptedClientSecretAsync(server.Id) ?? string.Empty;

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = oauthConfig.ClientId,
            ["client_secret"] = clientSecret,
        };

        try
        {
            var http = _httpClientFactory.CreateClient();
            var response = await http.PostAsync(oauthConfig.TokenUrl, new FormUrlEncodedContent(formData));
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Token refresh failed for server {ServerId}: {StatusCode}", server.Id, response.StatusCode);
                return null; // Fail gracefully
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return new OAuthTokenResult(
                json!.GetProperty("access_token").GetString()!,
                json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
                DateTime.UtcNow.AddSeconds(json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600),
                json.TryGetProperty("scope", out var sc) ? sc.GetString() : null
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception during token refresh for server {ServerId}", server.Id);
            return null; // Fail gracefully
        }
    }
}
