using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class DevOpsTokenService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<DevOpsTokenService> _logger;
    private readonly HttpClient _httpClient;

    public bool IsConfigured { get; }
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tenantId;

    private static readonly string[] Scopes = new[]
    {
        "vso.work",
        "vso.code",
        "vso.build_execute",
        "offline_access"
    };

    public DevOpsTokenService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<DevOpsTokenService> logger,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();

        _clientId = configuration["AzureDevOps:ClientId"] ?? "";
        _tenantId = configuration["AzureDevOps:TenantId"] ?? "";
        _clientSecret = configuration["AzureDevOps:ClientSecret"] ?? "";
        IsConfigured = !string.IsNullOrEmpty(_clientId)
                    && !string.IsNullOrEmpty(_tenantId)
                    && !string.IsNullOrEmpty(_clientSecret);

        if (!IsConfigured)
            _logger.LogWarning("AzureDevOps:ClientId/TenantId/ClientSecret not configured — Azure DevOps features disabled");
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

    public async Task<UserDevOpsToken> ExchangeCodeAsync(Guid userId, string code, string redirectUri)
    {
        _logger.LogInformation("Exchanging DevOps auth code for user {UserId}", userId);

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
            _logger.LogError("DevOps token exchange failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"DevOps token exchange failed: {response.StatusCode}");
        }

        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(body);

        var accessToken = tokenResponse.GetProperty("access_token").GetString()!;
        var refreshToken = tokenResponse.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
        var expiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

        // Fetch DevOps profile (display name + email)
        string? email = null;
        string? displayName = null;
        try
        {
            var profileRequest = new HttpRequestMessage(
                HttpMethod.Get,
                "https://app.vssps.visualstudio.com/_apis/profile/me?api-version=7.1");
            profileRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var profileResponse = await _httpClient.SendAsync(profileRequest);
            if (profileResponse.IsSuccessStatusCode)
            {
                var profileJson = JsonSerializer.Deserialize<JsonElement>(
                    await profileResponse.Content.ReadAsStringAsync());

                displayName = profileJson.TryGetProperty("displayName", out var dn)
                    && dn.ValueKind != JsonValueKind.Null
                    ? dn.GetString()
                    : null;

                email = profileJson.TryGetProperty("emailAddress", out var em)
                    && em.ValueKind != JsonValueKind.Null
                    ? em.GetString()
                    : null;
            }
            else
            {
                _logger.LogWarning("DevOps profile API returned {Status}", profileResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch DevOps profile for user {UserId}", userId);
        }

        var token = new UserDevOpsToken
        {
            UserId = userId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            Email = email,
            DisplayName = displayName,
            ConnectedAt = DateTime.UtcNow
        };

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.UserDevOpsTokens.FindAsync(userId);
        if (existing != null)
        {
            existing.AccessToken = token.AccessToken;
            existing.RefreshToken = token.RefreshToken;
            existing.ExpiresAt = token.ExpiresAt;
            existing.Email = token.Email;
            existing.DisplayName = token.DisplayName;
            existing.ConnectedAt = DateTime.UtcNow;
        }
        else
        {
            db.UserDevOpsTokens.Add(token);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("DevOps tokens stored for user {UserId}, displayName: {DisplayName}, email: {Email}, expires: {Expiry}",
            userId, displayName, email, expiresAt);
        return token;
    }

    public async Task<UserDevOpsToken?> GetTokenAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserDevOpsTokens.FindAsync(userId);
    }

    public async Task DeleteTokenAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var token = await db.UserDevOpsTokens.FindAsync(userId);
        if (token != null)
        {
            db.UserDevOpsTokens.Remove(token);
            await db.SaveChangesAsync();
            _logger.LogInformation("Azure DevOps disconnected for user {UserId}", userId);
        }
    }

    public async Task<bool> IsConnectedAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserDevOpsTokens.AnyAsync(t => t.UserId == userId);
    }
}
