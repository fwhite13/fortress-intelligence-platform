using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using FortressAI.Web.Services;
using FortressAI.Web.Services.Mcp;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("mcp/oauth")]
[Authorize]
public class McpOAuthController : ControllerBase
{
    private readonly IMcpRegistryService _registry;
    private readonly IMcpConnectionService _connectionService;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpOAuthController> _logger;

    public McpOAuthController(
        IMcpRegistryService registry,
        IMcpConnectionService connectionService,
        IMemoryCache cache,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<McpOAuthController> logger)
    {
        _registry = registry;
        _connectionService = connectionService;
        _cache = cache;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Initiates OAuth flow for connecting a user to an MCP server.
    /// GET /mcp/oauth/connect/{serverSlug}
    /// </summary>
    [HttpGet("connect/{serverSlug}")]
    public async Task<IActionResult> Connect(string serverSlug)
    {
        var server = await _registry.GetBySlugAsync(serverSlug);
        if (server is null || server.AuthType != "oauth2" || !server.RequiresUserAuth)
            return BadRequest("Server not found or does not support OAuth2 user authentication.");

        var oauthConfig = await _registry.GetDecryptedAuthConfigAsync(server.Id);
        if (oauthConfig is null)
            return BadRequest("OAuth configuration not found for server.");

        var userId = GetCurrentUserId();
        if (userId == Guid.Empty)
            return Unauthorized();

        // Generate random state token
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var cacheKey = $"mcp_oauth_state:{state}";

        _cache.Set(cacheKey, new { UserId = userId, ServerId = server.Id, ReturnUrl = "/settings" },
            TimeSpan.FromMinutes(10));

        // Build redirect_uri
        var redirectUri = GetRedirectUri();

        // Build authorization URL
        var authParams = new Dictionary<string, string?>
        {
            ["client_id"] = oauthConfig.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(" ", oauthConfig.Scopes),
            ["state"] = state,
            ["prompt"] = "select_account"
        };

        var authUrl = QueryHelpers.AddQueryString(oauthConfig.AuthorizationUrl, authParams);
        return Redirect(authUrl);
    }

    /// <summary>
    /// Handles OAuth callback after user authorizes.
    /// GET /mcp/oauth/callback
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery] string? code,
        [FromQuery] string state,
        [FromQuery] string? error)
    {
        if (!string.IsNullOrEmpty(error))
            return Redirect($"/settings?mcp_error={Uri.EscapeDataString(error)}");

        var cacheKey = $"mcp_oauth_state:{state}";
        if (!_cache.TryGetValue(cacheKey, out var stateObj) || stateObj is null)
            return Redirect("/settings?mcp_error=invalid_state");

        // Remove state from cache (one-time use)
        _cache.Remove(cacheKey);

        // Deserialize the anonymous-object state via JSON round-trip
        var stateJson = JsonSerializer.Serialize(stateObj);
        using var stateDoc = JsonDocument.Parse(stateJson);
        var stateRoot = stateDoc.RootElement;

        if (!stateRoot.TryGetProperty("UserId", out var userIdProp) ||
            !stateRoot.TryGetProperty("ServerId", out var serverIdProp))
            return Redirect("/settings?mcp_error=invalid_state");

        var stateUserId = userIdProp.GetGuid();
        var stateServerId = serverIdProp.GetGuid();

        // Validate current user matches state
        var currentUserId = GetCurrentUserId();
        if (currentUserId == Guid.Empty || currentUserId != stateUserId)
            return Redirect("/settings?mcp_error=user_mismatch");

        if (string.IsNullOrEmpty(code))
            return Redirect("/settings?mcp_error=missing_code");

        var server = await _registry.GetByIdAsync(stateServerId);
        if (server is null)
            return Redirect("/settings?mcp_error=server_not_found");

        var oauthConfig = await _registry.GetDecryptedAuthConfigAsync(stateServerId);
        if (oauthConfig is null)
            return Redirect("/settings?mcp_error=config_error");

        try
        {
            var redirectUri = GetRedirectUri();
            var tokenResult = await ExchangeCodeAsync(code, oauthConfig.ClientId, oauthConfig.TokenUrl, redirectUri, stateServerId);

            await _connectionService.SaveTokenAsync(
                currentUserId,
                stateServerId,
                tokenResult.AccessToken,
                tokenResult.RefreshToken,
                tokenResult.ExpiresAt,
                tokenResult.Scopes,
                externalUserId: null,
                externalEmail: null);

            _logger.LogInformation("OAuth token saved for user {UserId} server {ServerId}", currentUserId, stateServerId);
            return Redirect($"/settings?mcp_connected={Uri.EscapeDataString(server.Slug)}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth token exchange failed for user {UserId} server {ServerId}", currentUserId, stateServerId);
            return Redirect($"/settings?mcp_error={Uri.EscapeDataString("token_exchange_failed")}");
        }
    }

    /// <summary>
    /// Exchanges an authorization code for OAuth tokens.
    /// </summary>
    private async Task<OAuthTokenResult> ExchangeCodeAsync(
        string code, string clientId, string tokenUrl, string redirectUri, Guid serverId)
    {
        var clientSecret = await _registry.GetDecryptedClientSecretAsync(serverId) ?? string.Empty;

        var formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri
        };

        var http = _httpClientFactory.CreateClient();
        var response = await http.PostAsync(tokenUrl, new FormUrlEncodedContent(formData));
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OAuth token exchange failed: {response.StatusCode}");

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = json!.GetProperty("access_token").GetString()!;
        string? refreshToken = json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        int expiresIn = json.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
        string? scopes = json.TryGetProperty("scope", out var sc) ? sc.GetString() : null;

        return new OAuthTokenResult(accessToken, refreshToken, DateTime.UtcNow.AddSeconds(expiresIn), scopes);
    }

    private string GetRedirectUri()
    {
        var configured = _config["McpOAuth:RedirectUri"];
        if (!string.IsNullOrEmpty(configured))
            return configured;
        return $"{Request.Scheme}://{Request.Host}/mcp/oauth/callback";
    }

    private Guid GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}
