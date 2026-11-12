using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class ZoomOAuthService : IZoomOAuthService
{
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ZoomOAuthService> _logger;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _redirectUri;

    public ZoomOAuthService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<ZoomOAuthService> logger)
    {
        _dbFactory = dbFactory;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        // Same Zoom app used by the SDK bot path (WI #7024) — FIRM_ZOOM_SDK_KEY/SECRET are the
        // app's Client ID/Secret. Firm:ZoomClientId/Secret take precedence if set explicitly.
        _clientId = config["Firm:ZoomClientId"] is { Length: > 0 } cid ? cid : (config["FIRM_ZOOM_SDK_KEY"] ?? "");
        _clientSecret = config["Firm:ZoomClientSecret"] is { Length: > 0 } cs ? cs : (config["FIRM_ZOOM_SDK_SECRET"] ?? "");
        _redirectUri = config["Firm:ZoomOAuthRedirectUri"] is { Length: > 0 } uri
            ? uri
            : "https://meetings.dev.fortressam.ai/oauth/zoom/callback";
    }

    public string GetAuthorizationUrl(string state)
    {
        var qs = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = _clientId,
            ["redirect_uri"] = _redirectUri,
            ["state"] = state,
        };
        var query = string.Join("&", qs.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        return $"https://zoom.us/oauth/authorize?{query}";
    }

    public async Task<ZoomTokenExchangeResult> ExchangeCodeAsync(string code)
    {
        var http = _httpClientFactory.CreateClient();

        using var tokenReq = new HttpRequestMessage(HttpMethod.Post, "https://zoom.us/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = _redirectUri,
            })
        };
        tokenReq.Headers.Authorization = BasicAuthHeader();

        var tokenRes = await http.SendAsync(tokenReq);
        var tokenBody = await tokenRes.Content.ReadAsStringAsync();
        if (!tokenRes.IsSuccessStatusCode)
        {
            _logger.LogError("FIRM: Zoom OAuth code exchange failed: {Status} {Error}", tokenRes.StatusCode, OAuthErrorCode(tokenBody));
            throw new InvalidOperationException("Zoom OAuth code exchange failed");
        }

        var tokenJson = JsonSerializer.Deserialize<JsonElement>(tokenBody);
        var accessToken = tokenJson.GetProperty("access_token").GetString()!;
        var refreshToken = tokenJson.GetProperty("refresh_token").GetString()!;
        var expiresAt = DateTime.UtcNow.AddSeconds(tokenJson.GetProperty("expires_in").GetInt32());

        using var meReq = new HttpRequestMessage(HttpMethod.Get, "https://api.zoom.us/v2/users/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meRes = await http.SendAsync(meReq);
        var meBody = await meRes.Content.ReadAsStringAsync();
        if (!meRes.IsSuccessStatusCode)
        {
            _logger.LogError("FIRM: Zoom /users/me failed: {Status} {Body}", meRes.StatusCode, meBody);
            throw new InvalidOperationException("Failed to fetch Zoom user profile");
        }

        var meJson = JsonSerializer.Deserialize<JsonElement>(meBody);
        var zoomUserId = meJson.GetProperty("id").GetString() ?? "";
        var zoomEmail = meJson.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;

        return new ZoomTokenExchangeResult(zoomUserId, zoomEmail, accessToken, refreshToken, expiresAt);
    }

    public async Task<string> HandleCallbackAsync(Guid userId, string code)
    {
        var (zoomUserId, zoomEmail, accessToken, refreshToken, expiresAt) = await ExchangeCodeAsync(code);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.ZoomOAuthTokens.FirstOrDefaultAsync(z => z.UserId == userId);
        var now = DateTime.UtcNow;

        if (record == null)
        {
            db.ZoomOAuthTokens.Add(new FirmZoomOAuth
            {
                UserId = userId,
                ZoomUserId = zoomUserId,
                ZoomEmail = zoomEmail,
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                ExpiresAt = expiresAt,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            record.ZoomUserId = zoomUserId;
            record.ZoomEmail = zoomEmail;
            record.AccessToken = accessToken;
            record.RefreshToken = refreshToken;
            record.ExpiresAt = expiresAt;
            record.UpdatedAt = now;
        }
        await db.SaveChangesAsync();

        _logger.LogInformation("FIRM: Zoom account linked for user {UserId} as {Email}", userId, zoomEmail);
        return zoomEmail ?? zoomUserId;
    }

    public async Task<string?> GetObfTokenAsync(Guid userId, long meetingId)
    {
        try
        {
            FirmZoomOAuth? snapshot;
            await using (var db = await _dbFactory.CreateDbContextAsync())
                snapshot = await db.ZoomOAuthTokens.AsNoTracking().FirstOrDefaultAsync(z => z.UserId == userId);
            if (snapshot == null)
                return null;

            string? accessToken = snapshot.AccessToken;
            if (snapshot.ExpiresAt < DateTime.UtcNow + RefreshSkew)
            {
                accessToken = await RefreshLockedAsync(userId, accessToken, forceRefresh: false);
                if (accessToken == null)
                    return null;
            }

            var (ok, authError, token) = await FetchObfTokenAsync(accessToken, meetingId);
            if (ok)
                return token;
            if (!authError)
                return null;

            // 401/403: the token we used is bad. Refresh under the row lock unless another caller
            // already replaced it, then retry once.
            accessToken = await RefreshLockedAsync(userId, accessToken, forceRefresh: true);
            if (accessToken == null)
                return null;

            var (ok2, _, token2) = await FetchObfTokenAsync(accessToken, meetingId);
            return ok2 ? token2 : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: Failed to obtain Zoom OBF token for user {UserId}, meeting {MeetingId}", userId, meetingId);
            return null;
        }
    }

    public async Task DisconnectAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.ZoomOAuthTokens.FirstOrDefaultAsync(z => z.UserId == userId);
        if (record == null)
            return;
        db.ZoomOAuthTokens.Remove(record);
        await db.SaveChangesAsync();
    }

    public async Task<string?> GetLinkedEmailAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ZoomOAuthTokens
            .Where(z => z.UserId == userId)
            .Select(z => z.ZoomEmail)
            .FirstOrDefaultAsync();
    }

    public async Task<FirmZoomOAuth?> GetConnectionAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ZoomOAuthTokens.AsNoTracking().FirstOrDefaultAsync(z => z.UserId == userId);
    }

    /// <summary>
    /// Refreshes the user's Zoom tokens under a row lock (SELECT ... FOR UPDATE) so concurrent callers
    /// do not both spend the refresh token. <paramref name="seenAccessToken"/> is the access token the
    /// caller read before locking: if the stored one differs after the lock, another caller has already
    /// refreshed and its token is returned without calling Zoom. (updated_at is not usable for this
    /// check: the column has ON UPDATE CURRENT_TIMESTAMP.) Tokens stay stored as plaintext, as before.
    /// Returns the current access token, or null if there is no row or the refresh failed.
    /// </summary>
    private async Task<string?> RefreshLockedAsync(Guid userId, string seenAccessToken, bool forceRefresh)
    {
        await using var outer = await _dbFactory.CreateDbContextAsync();
        // FIRM's context has EnableRetryOnFailure, so a user transaction must run inside the strategy.
        var strategy = outer.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await using var tx = await db.Database.BeginTransactionAsync();

            var rows = await db.ZoomOAuthTokens
                .FromSqlRaw("SELECT * FROM firm_zoom_oauth WHERE user_id = {0} FOR UPDATE", userId.ToString())
                .ToListAsync();
            var row = rows.FirstOrDefault();
            if (row == null)
            {
                await tx.CommitAsync();
                return null;
            }

            // Re-check after acquiring the lock.
            var refreshedByOther = row.AccessToken != seenAccessToken;
            var stillFresh = !forceRefresh && row.ExpiresAt >= DateTime.UtcNow + RefreshSkew;
            if (refreshedByOther || stillFresh)
            {
                await tx.CommitAsync();
                return row.AccessToken;
            }

            var refreshed = await RefreshTokensAsync(row.RefreshToken);
            if (refreshed == null)
            {
                await tx.RollbackAsync();
                _logger.LogWarning("FIRM: Zoom token refresh failed for user {UserId}", userId);
                return null;
            }

            row.AccessToken = refreshed.AccessToken;
            // Keep the stored refresh token when Zoom does not return a new one.
            if (!string.IsNullOrEmpty(refreshed.RefreshToken))
                row.RefreshToken = refreshed.RefreshToken;
            row.ExpiresAt = refreshed.ExpiresAt;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return row.AccessToken;
        });
    }

    public async Task<ZoomTokenRefreshResult?> RefreshTokensAsync(string refreshToken)
    {
        var http = _httpClientFactory.CreateClient();
        // The refresh token goes in the form body, never the URL: request URIs are logged by the
        // IHttpClientFactory handlers and by any proxy in between.
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://zoom.us/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            })
        };
        req.Headers.Authorization = BasicAuthHeader();

        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("FIRM: Zoom token refresh failed: {Status} {Error}", res.StatusCode, OAuthErrorCode(body));
            return null;
        }

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        return new ZoomTokenRefreshResult(
            json.GetProperty("access_token").GetString()!,
            json.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            DateTime.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32()));
    }

    public async Task<(bool ok, bool authError, string? token)> FetchObfTokenAsync(string accessToken, long meetingId)
    {
        var http = _httpClientFactory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.zoom.us/v2/users/me/token?type=onbehalf&meeting_id={meetingId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var res = await http.SendAsync(req);
        if (res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync();
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            var token = json.TryGetProperty("token", out var tokenProp) ? tokenProp.GetString() : null;
            return (true, false, token);
        }

        var authError = res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
        _logger.LogInformation("FIRM: Zoom OBF token request returned {Status} for meeting {MeetingId}", res.StatusCode, meetingId);
        return (false, authError, null);
    }

    /// <summary>
    /// The OAuth "error" (or Zoom "reason") field of an error response, for logging instead of the raw body.
    /// </summary>
    private static string OAuthErrorCode(string body)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(body);
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    return e.GetString() ?? "unknown";
                if (json.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String)
                    return r.GetString() ?? "unknown";
            }
        }
        catch (JsonException) { }
        return "unknown";
    }

    private AuthenticationHeaderValue BasicAuthHeader() =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));
}
