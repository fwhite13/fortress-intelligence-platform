using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using FortressIntelligenceRM.Web.Services.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FortressIntelligenceRM.Web.Services.Plaud;

/// <summary>
/// WI #9133 — Plaud account linking (spec §2.4 P1, §5.4, §9.2). Mirrors ZoomOAuthService where it
/// can, with the Plaud differences: public client registered per environment through RFC 7591 DCR,
/// PKCE S256, RFC 8707 resource, tokens encrypted at rest, a row-locked refresh and a reconnect state.
///
/// All Plaud HTTP traffic (discovery, registration, token, MCP) uses the "PlaudMcp" named client,
/// which has its logging handlers removed. Logs carry user id, status and latency only.
/// </summary>
public class PlaudOAuthService : IPlaudOAuthService
{
    public const string Provider = "plaud";
    public const string TokenPurpose = "Firm.PlaudTokens.v1";
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MetadataCacheTtl = TimeSpan.FromHours(1);
    private const int DefaultExpiresInSeconds = 3600;

    private static readonly SemaphoreSlim MetadataLock = new(1, 1);
    private static AuthServerMetadata? _cachedMetadata;
    private static DateTime _cachedMetadataAt;

    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PlaudMcpClient _mcp;
    private readonly TokenProtector _tokens;
    private readonly PlaudOptions _options;
    private readonly ILogger<PlaudOAuthService> _logger;
    private readonly string _envHost;

    public PlaudOAuthService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IHttpClientFactory httpClientFactory,
        PlaudMcpClient mcp,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PlaudOptions> options,
        ILogger<PlaudOAuthService> logger)
    {
        _dbFactory = dbFactory;
        _httpClientFactory = httpClientFactory;
        _mcp = mcp;
        _tokens = new TokenProtector(dataProtectionProvider, TokenPurpose);
        _options = options.Value;
        _logger = logger;
        // oauth_env = the redirect host. Only the environment whose host matches refreshes a row (P7).
        _envHost = Uri.TryCreate(_options.RedirectUri, UriKind.Absolute, out var redirect) ? redirect.Host : "";
    }

    public bool IsEnabled => _options.Enabled;

    // ── Authorize ─────────────────────────────────────────────────────────

    public async Task<string> BuildAuthorizationUrlAsync(string state, string codeVerifier, CancellationToken ct = default)
    {
        EnsureConfigured();
        var metadata = await GetMetadataAsync(ct);
        var clientId = await GetOrRegisterClientIdAsync(metadata, ct);

        var qs = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["state"] = state,
            ["code_challenge"] = Pkce.ChallengeS256(codeVerifier),
            ["code_challenge_method"] = Pkce.Method,
            ["resource"] = _options.McpUrl,
        };
        var query = string.Join("&", qs.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        var separator = metadata.AuthorizationEndpoint.Contains('?') ? "&" : "?";
        return metadata.AuthorizationEndpoint + separator + query;
    }

    // ── Callback ──────────────────────────────────────────────────────────

    public async Task<string?> HandleCallbackAsync(Guid userId, string code, string codeVerifier, CancellationToken ct = default)
    {
        EnsureConfigured();
        var metadata = await GetMetadataAsync(ct);
        var clientId = await GetOrRegisterClientIdAsync(metadata, ct);

        // Public client: client_id + code_verifier, no client_secret and no Basic auth.
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["client_id"] = clientId,
            ["code_verifier"] = codeVerifier,
            ["resource"] = _options.McpUrl,
        };
        var outcome = await PostTokenAsync(metadata.TokenEndpoint, form, userId, "exchange", ct);
        if (outcome.Tokens == null)
            throw new InvalidOperationException($"Plaud code exchange failed ({outcome.OAuthError ?? "http " + outcome.Status})");
        var tokens = outcome.Tokens;

        // "Connected as": best effort. A failure here does not fail the connect.
        PlaudCurrentUser? me = null;
        try
        {
            me = await _mcp.GetCurrentUserAsync(tokens.AccessToken, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[PlaudOAuth] get_current_user failed for user {UserId}: {ErrorType}", userId, ex.GetType().Name);
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.PlaudAccounts.FirstOrDefaultAsync(a => a.UserId == userId, ct);
        var now = DateTime.UtcNow;
        if (row == null)
        {
            row = new FirmPlaudAccount { UserId = userId, CreatedAt = now };
            db.PlaudAccounts.Add(row);
        }
        row.PlaudUserId = Truncate(me?.Id, 128);
        row.PlaudEmail = Truncate(me?.Email, 256);
        row.AccessTokenEnc = _tokens.Protect(tokens.AccessToken);
        row.RefreshTokenEnc = _tokens.Protect(tokens.RefreshToken ?? "");
        row.ExpiresAt = tokens.ExpiresAtUtc;
        row.ClientId = clientId;
        row.OAuthEnv = _envHost;
        row.NeedsReconnect = false;
        row.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("[PlaudOAuth] account linked for user {UserId} identity={HasIdentity}", userId, me?.Email != null);
        return row.PlaudEmail ?? row.PlaudUserId;
    }

    // ── Access token (locked refresh) ─────────────────────────────────────

    public async Task<string?> GetAccessTokenAsync(Guid userId, bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!IsEnabled || _envHost.Length == 0)
            return null;

        FirmPlaudAccount? snapshot;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
            snapshot = await db.PlaudAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == userId, ct);

        if (snapshot == null || snapshot.NeedsReconnect || snapshot.OAuthEnv != _envHost)
            return null;

        if (!forceRefresh && snapshot.ExpiresAt > DateTime.UtcNow + RefreshSkew)
        {
            if (_tokens.TryUnprotect(snapshot.AccessTokenEnc, out var cached))
                return cached;
            await MarkNeedsReconnectAsync(userId, "decrypt", ct);
            return null;
        }

        return await RefreshLockedAsync(userId, forceRefresh, snapshot.UpdatedAt, ct);
    }

    private async Task<string?> RefreshLockedAsync(Guid userId, bool forceRefresh, DateTime seenUpdatedAt, CancellationToken ct)
    {
        await using var outer = await _dbFactory.CreateDbContextAsync(ct);
        // FIRM's context has EnableRetryOnFailure, so a user transaction must run inside the strategy.
        var strategy = outer.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Row lock: concurrent callers (poller, Sync now, the Settings tab) queue here, and the
            // loser sees the winner's refreshed token on the re-check below instead of refreshing again.
            var rows = await db.PlaudAccounts
                .FromSqlRaw("SELECT * FROM firm_plaud_accounts WHERE user_id = {0} FOR UPDATE", userId.ToString())
                .ToListAsync(ct);
            var row = rows.FirstOrDefault();
            if (row == null || row.NeedsReconnect || row.OAuthEnv != _envHost)
            {
                await tx.CommitAsync(ct);
                return null;
            }

            // Re-check after acquiring the lock.
            var refreshedByOther = forceRefresh && row.UpdatedAt != seenUpdatedAt;
            if ((!forceRefresh && row.ExpiresAt > DateTime.UtcNow + RefreshSkew) || refreshedByOther)
            {
                if (_tokens.TryUnprotect(row.AccessTokenEnc, out var current))
                {
                    await tx.CommitAsync(ct);
                    return current;
                }
                return await SetNeedsReconnectAsync(db, tx, row, "decrypt", ct);
            }

            if (!_tokens.TryUnprotect(row.RefreshTokenEnc, out var refreshToken) || refreshToken.Length == 0)
                return await SetNeedsReconnectAsync(db, tx, row, "decrypt", ct);

            var metadata = await GetMetadataAsync(ct);
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = row.ClientId,
                ["resource"] = _options.McpUrl,
            };
            var outcome = await PostTokenAsync(metadata.TokenEndpoint, form, userId, "refresh", ct);

            if (outcome.Tokens != null)
            {
                row.AccessTokenEnc = _tokens.Protect(outcome.Tokens.AccessToken);
                // Keep the stored refresh token when Plaud does not return a new one.
                if (!string.IsNullOrEmpty(outcome.Tokens.RefreshToken))
                    row.RefreshTokenEnc = _tokens.Protect(outcome.Tokens.RefreshToken);
                row.ExpiresAt = outcome.Tokens.ExpiresAtUtc;
                row.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return outcome.Tokens.AccessToken;
            }

            if (outcome.OAuthError == "invalid_grant")
                return await SetNeedsReconnectAsync(db, tx, row, "invalid_grant", ct);

            // Transient (5xx, timeout, other error): leave the row alone; the caller backs off.
            await tx.RollbackAsync(ct);
            return null;
        });
    }

    private async Task<string?> SetNeedsReconnectAsync(FirmDbContext db, Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx,
        FirmPlaudAccount row, string reason, CancellationToken ct)
    {
        row.NeedsReconnect = true;
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        _logger.LogWarning("[PlaudOAuth] reconnect needed for user {UserId} reason={Reason}", row.UserId, reason);
        return null;
    }

    private async Task MarkNeedsReconnectAsync(Guid userId, string reason, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var updated = await db.PlaudAccounts
            .Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.NeedsReconnect, true)
                .SetProperty(a => a.UpdatedAt, DateTime.UtcNow), ct);
        if (updated > 0)
            _logger.LogWarning("[PlaudOAuth] reconnect needed for user {UserId} reason={Reason}", userId, reason);
    }

    // ── Status / disconnect ───────────────────────────────────────────────

    public async Task<PlaudAccountStatus> GetStatusAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var row = await db.PlaudAccounts.AsNoTracking()
            .Where(a => a.UserId == userId)
            .Select(a => new { a.PlaudEmail, a.PlaudUserId, a.NeedsReconnect, a.OAuthEnv })
            .FirstOrDefaultAsync(ct);
        if (row == null)
            return new PlaudAccountStatus(false, null, false, false);
        return new PlaudAccountStatus(true, row.PlaudEmail ?? row.PlaudUserId, row.NeedsReconnect, row.OAuthEnv != _envHost);
    }

    public async Task DisconnectAsync(Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var deleted = await db.PlaudAccounts.Where(a => a.UserId == userId).ExecuteDeleteAsync(ct);
        _logger.LogInformation("[PlaudOAuth] disconnect for user {UserId} removed={Removed}", userId, deleted);
    }

    // ── Discovery and dynamic client registration ─────────────────────────

    private async Task<AuthServerMetadata> GetMetadataAsync(CancellationToken ct)
    {
        if (_cachedMetadata != null && DateTime.UtcNow - _cachedMetadataAt < MetadataCacheTtl)
            return _cachedMetadata;

        await MetadataLock.WaitAsync(ct);
        try
        {
            if (_cachedMetadata != null && DateTime.UtcNow - _cachedMetadataAt < MetadataCacheTtl)
                return _cachedMetadata;

            var mcp = new Uri(_options.McpUrl);
            var url = $"{mcp.Scheme}://{mcp.Authority}/.well-known/oauth-authorization-server";
            var sw = Stopwatch.StartNew();
            using var res = await Http().GetAsync(url, ct);
            _logger.LogInformation("[PlaudOAuth] discovery status={Status} ms={Ms}", (int)res.StatusCode, sw.ElapsedMilliseconds);
            res.EnsureSuccessStatusCode();

            var json = await res.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Plaud authorization server metadata is empty");
            var metadata = new AuthServerMetadata(
                json["authorization_endpoint"]?.GetValue<string>() ?? throw new InvalidOperationException("No authorization_endpoint"),
                json["token_endpoint"]?.GetValue<string>() ?? throw new InvalidOperationException("No token_endpoint"),
                json["registration_endpoint"]?.GetValue<string>());
            _cachedMetadata = metadata;
            _cachedMetadataAt = DateTime.UtcNow;
            return metadata;
        }
        finally
        {
            MetadataLock.Release();
        }
    }

    private async Task<string> GetOrRegisterClientIdAsync(AuthServerMetadata metadata, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_options.ClientId))
            return _options.ClientId;

        var existing = await ReadClientIdAsync(ct);
        if (existing != null)
            return existing;

        if (string.IsNullOrEmpty(metadata.RegistrationEndpoint))
            throw new InvalidOperationException("Plaud metadata has no registration_endpoint and Firm:Plaud:ClientId is not set");

        var registration = new JsonObject
        {
            ["client_name"] = "FIRM",
            ["redirect_uris"] = new JsonArray(_options.RedirectUri),
            ["grant_types"] = new JsonArray("authorization_code", "refresh_token"),
            ["response_types"] = new JsonArray("code"),
            ["token_endpoint_auth_method"] = "none",
        };
        var sw = Stopwatch.StartNew();
        using var res = await Http().PostAsJsonAsync(metadata.RegistrationEndpoint, registration, ct);
        _logger.LogInformation("[PlaudOAuth] client registration status={Status} ms={Ms}", (int)res.StatusCode, sw.ElapsedMilliseconds);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
        var clientId = json?["client_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(clientId))
            throw new InvalidOperationException("Plaud registration returned no client_id");

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            db.OAuthClients.Add(new FirmOAuthClient
            {
                Provider = Provider,
                RedirectUri = _options.RedirectUri,
                ClientId = clientId,
                RegisteredAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
            return clientId;
        }
        catch (DbUpdateException)
        {
            // Lost the PK race with a concurrent first Connect: use the winner's client.
            return await ReadClientIdAsync(ct) ?? throw new InvalidOperationException("Plaud client registration could not be stored");
        }
    }

    private async Task<string?> ReadClientIdAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.OAuthClients.AsNoTracking()
            .Where(c => c.Provider == Provider && c.RedirectUri == _options.RedirectUri)
            .Select(c => c.ClientId)
            .FirstOrDefaultAsync(ct);
    }

    // ── Token endpoint ────────────────────────────────────────────────────

    private async Task<TokenOutcome> PostTokenAsync(string tokenEndpoint, Dictionary<string, string> form, Guid userId, string grant, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        HttpResponseMessage res;
        try
        {
            res = await Http().PostAsync(tokenEndpoint, new FormUrlEncodedContent(form), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("[PlaudOAuth] token {Grant} for user {UserId} failed: {ErrorType} ms={Ms}", grant, userId, ex.GetType().Name, sw.ElapsedMilliseconds);
            return new TokenOutcome(null, 0, null);
        }

        using (res)
        {
            var status = (int)res.StatusCode;
            JsonObject? json = null;
            try
            {
                json = await res.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // Non-JSON body: treat as a plain HTTP failure below.
            }

            if (!res.IsSuccessStatusCode || json?["access_token"] is null)
            {
                // Only the RFC 6749 error code is kept, never the body or error_description.
                var oauthError = json?["error"]?.GetValue<string>();
                _logger.LogWarning("[PlaudOAuth] token {Grant} for user {UserId} status={Status} error={OAuthError} ms={Ms}",
                    grant, userId, status, oauthError, sw.ElapsedMilliseconds);
                return new TokenOutcome(null, status, oauthError);
            }

            _logger.LogInformation("[PlaudOAuth] token {Grant} for user {UserId} status={Status} ms={Ms}", grant, userId, status, sw.ElapsedMilliseconds);
            var expiresIn = json["expires_in"] is JsonValue ev && int.TryParse(ev.ToString(), out var secs) && secs > 0
                ? secs
                : DefaultExpiresInSeconds;
            return new TokenOutcome(
                new TokenSet(
                    json["access_token"]!.GetValue<string>(),
                    json["refresh_token"]?.GetValue<string>(),
                    DateTime.UtcNow.AddSeconds(expiresIn)),
                status,
                null);
        }
    }

    private HttpClient Http() => _httpClientFactory.CreateClient(PlaudMcpClient.HttpClientName);

    private void EnsureConfigured()
    {
        if (!IsEnabled)
            throw new InvalidOperationException("Plaud is not enabled");
        if (_envHost.Length == 0)
            throw new InvalidOperationException("Firm:Plaud:RedirectUri is not configured");
    }

    private static string? Truncate(string? value, int max) =>
        value == null ? null : value.Length <= max ? value : value[..max];

    private record AuthServerMetadata(string AuthorizationEndpoint, string TokenEndpoint, string? RegistrationEndpoint);
    private record TokenSet(string AccessToken, string? RefreshToken, DateTime ExpiresAtUtc);
    private record TokenOutcome(TokenSet? Tokens, int Status, string? OAuthError);
}
