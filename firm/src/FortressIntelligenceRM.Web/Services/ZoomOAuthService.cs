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

    public async Task<string> HandleCallbackAsync(Guid userId, string code)
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
            _logger.LogError("FIRM: Zoom OAuth code exchange failed: {Status} {Body}", tokenRes.StatusCode, tokenBody);
            throw new InvalidOperationException("Zoom OAuth code exchange failed");
        }

        var tokenJson = JsonSerializer.Deserialize<JsonElement>(tokenBody);
        var accessToken = tokenJson.GetProperty("access_token").GetString()!;
        var refreshToken = tokenJson.GetProperty("refresh_token").GetString()!;
        var expiresIn = tokenJson.GetProperty("expires_in").GetInt32();

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

        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.ZoomOAuthTokens.FirstOrDefaultAsync(z => z.UserId == userId);
        var now = DateTime.UtcNow;
        var expiresAt = now.AddSeconds(expiresIn);

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
            await using var db = await _dbFactory.CreateDbContextAsync();
            var record = await db.ZoomOAuthTokens.FirstOrDefaultAsync(z => z.UserId == userId);
            if (record == null)
                return null;

            if (record.ExpiresAt < DateTime.UtcNow.AddMinutes(5) && !await RefreshAccessTokenAsync(db, record))
                return null;

            var (ok, authError, token) = await FetchObfTokenAsync(record.AccessToken, meetingId);
            if (ok)
                return token;
            if (!authError)
                return null;

            if (!await RefreshAccessTokenAsync(db, record))
                return null;

            var (ok2, _, token2) = await FetchObfTokenAsync(record.AccessToken, meetingId);
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

    private async Task<bool> RefreshAccessTokenAsync(FirmDbContext db, FirmZoomOAuth record)
    {
        var http = _httpClientFactory.CreateClient();
        var url = $"https://zoom.us/oauth/token?grant_type=refresh_token&refresh_token={Uri.EscapeDataString(record.RefreshToken)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Authorization = BasicAuthHeader();

        var res = await http.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            _logger.LogWarning("FIRM: Zoom token refresh failed for user {UserId}: {Status} {Body}", record.UserId, res.StatusCode, body);
            return false;
        }

        var json = JsonSerializer.Deserialize<JsonElement>(body);
        record.AccessToken = json.GetProperty("access_token").GetString()!;
        if (json.TryGetProperty("refresh_token", out var rt))
            record.RefreshToken = rt.GetString()!;
        record.ExpiresAt = DateTime.UtcNow.AddSeconds(json.GetProperty("expires_in").GetInt32());
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    private async Task<(bool ok, bool authError, string? token)> FetchObfTokenAsync(string accessToken, long meetingId)
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

    private AuthenticationHeaderValue BasicAuthHeader() =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));
}
