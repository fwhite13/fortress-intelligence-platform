using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using FortressAI.Web.Services.Mcp;

namespace FortressAI.Web.Services;

public interface IMcpConnectionService
{
    Task<List<McpServerConnectionStatus>> GetUserConnectionsAsync(Guid userId);
    Task<bool> IsConnectedAsync(Guid userId, Guid serverId);
    Task<string?> GetAccessTokenAsync(Guid userId, Guid serverId);
    Task SaveTokenAsync(Guid userId, Guid serverId, string accessToken, string? refreshToken,
        DateTime? expiresAt, string? scopes, string? externalUserId, string? externalEmail);
    Task SaveUserTokenAsync(Guid userId, Guid serverId, string rawAccessToken,
        string? rawRefreshToken = null, DateTime? expiresAt = null, string? externalEmail = null);
    Task DisconnectAsync(Guid userId, Guid serverId);
}

public class McpConnectionService : IMcpConnectionService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDataProtector _dataProtector;
    private readonly IDataProtector _adminKeyProtector;
    private readonly McpTokenRefreshService _refreshService;
    private readonly ILogger<McpConnectionService> _logger;

    public McpConnectionService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider,
        McpTokenRefreshService refreshService,
        ILogger<McpConnectionService> logger)
    {
        _dbFactory = dbFactory;
        _dataProtector = dataProtectionProvider.CreateProtector("McpTokens.v1");
        _adminKeyProtector = dataProtectionProvider.CreateProtector("McpAdmin.SystemKeys.v1");
        _refreshService = refreshService;
        _logger = logger;
    }

    public async Task<List<McpServerConnectionStatus>> GetUserConnectionsAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var servers = await db.McpServers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();

        var serverIds = servers.Select(s => s.Id).ToList();
        var tokens = await db.UserMcpTokens
            .Where(t => t.UserId == userId && serverIds.Contains(t.ServerId))
            .ToListAsync();

        return servers.Select(server =>
        {
            var token = tokens.FirstOrDefault(t => t.ServerId == server.Id);
            bool isConnected;
            string? connectedAs = null;
            DateTime? expiresAt = null;

            if (!server.RequiresUserAuth)
            {
                isConnected = true;
                connectedAs = "System API Key";
            }
            else
            {
                isConnected = token != null && !token.IsExpired;
                connectedAs = token?.ExternalEmail;
                expiresAt = token?.TokenExpiresAt;
            }

            return new McpServerConnectionStatus
            {
                Server = server,
                IsConnected = isConnected,
                ConnectedAs = connectedAs,
                ExpiresAt = expiresAt
            };
        }).ToList();
    }

    public async Task<bool> IsConnectedAsync(Guid userId, Guid serverId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FindAsync(serverId);
        if (server is null) return false;
        if (!server.RequiresUserAuth) return true;

        var token = await db.UserMcpTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ServerId == serverId);
        return token != null && !token.IsExpired;
    }

    public async Task<string?> GetAccessTokenAsync(Guid userId, Guid serverId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FindAsync(serverId);
        if (server is null) return null;

        // System API key server: decrypt and return system key
        if (!server.RequiresUserAuth)
        {
            if (string.IsNullOrEmpty(server.SystemApiKey)) return null;
            try { return _adminKeyProtector.Unprotect(server.SystemApiKey); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt system API key for server {ServerId}", serverId);
                return null;
            }
        }

        // User token server: get and decrypt user's token
        var token = await db.UserMcpTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ServerId == serverId);
        if (token is null) return null;

        if (token.IsExpired)
        {
            // Attempt silent token refresh if we have a refresh token
            if (token.RefreshToken != null)
            {
                var refreshed = await _refreshService.RefreshTokenAsync(server, token.RefreshToken);
                if (refreshed != null)
                {
                    await SaveTokenAsync(userId, serverId, refreshed.AccessToken, refreshed.RefreshToken,
                        refreshed.ExpiresAt, refreshed.Scopes, token.ExternalUserId, token.ExternalEmail);
                    return refreshed.AccessToken;
                }
            }
            return null; // Refresh failed or no refresh token — let tool call fail gracefully
        }

        try { return _dataProtector.Unprotect(token.AccessToken); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt user token for user {UserId} server {ServerId}", userId, serverId);
            return null;
        }
    }

    public async Task SaveTokenAsync(Guid userId, Guid serverId, string accessToken, string? refreshToken,
        DateTime? expiresAt, string? scopes, string? externalUserId, string? externalEmail)
    {
        // Encrypt tokens before persisting
        var encryptedAccess = _dataProtector.Protect(accessToken);
        var encryptedRefresh = refreshToken is not null ? _dataProtector.Protect(refreshToken) : null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.UserMcpTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ServerId == serverId);

        if (existing is not null)
        {
            existing.AccessToken = encryptedAccess;
            existing.RefreshToken = encryptedRefresh;
            existing.TokenExpiresAt = expiresAt;
            existing.Scopes = scopes;
            existing.ExternalUserId = externalUserId;
            existing.ExternalEmail = externalEmail;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            db.UserMcpTokens.Add(new UserMcpToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ServerId = serverId,
                AccessToken = encryptedAccess,
                RefreshToken = encryptedRefresh,
                TokenExpiresAt = expiresAt,
                Scopes = scopes,
                ExternalUserId = externalUserId,
                ExternalEmail = externalEmail,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Saved token for user {UserId} server {ServerId}", userId, serverId);
    }

    public async Task SaveUserTokenAsync(Guid userId, Guid serverId, string rawAccessToken,
        string? rawRefreshToken = null, DateTime? expiresAt = null, string? externalEmail = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existing = await db.UserMcpTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ServerId == serverId);

        var protectedAccess = _dataProtector.Protect(rawAccessToken);
        var protectedRefresh = rawRefreshToken != null ? _dataProtector.Protect(rawRefreshToken) : null;

        if (existing is null)
        {
            db.UserMcpTokens.Add(new UserMcpToken
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ServerId = serverId,
                AccessToken = protectedAccess,
                RefreshToken = protectedRefresh,
                TokenExpiresAt = expiresAt,
                ExternalEmail = externalEmail,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.AccessToken = protectedAccess;
            existing.RefreshToken = protectedRefresh ?? existing.RefreshToken;
            existing.TokenExpiresAt = expiresAt ?? existing.TokenExpiresAt;
            existing.ExternalEmail = externalEmail ?? existing.ExternalEmail;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }

    public async Task DisconnectAsync(Guid userId, Guid serverId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Delete user token
        var token = await db.UserMcpTokens
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ServerId == serverId);
        if (token != null)
            db.UserMcpTokens.Remove(token);

        // Delete conversation toggles for this user's conversations
        var userConversationIds = await db.Conversations
            .Where(c => c.UserId == userId)
            .Select(c => c.Id)
            .ToListAsync();

        if (userConversationIds.Count > 0)
        {
            var toggles = await db.ConversationMcpServers
                .Where(cms => cms.ServerId == serverId && userConversationIds.Contains(cms.ConversationId))
                .ToListAsync();
            db.ConversationMcpServers.RemoveRange(toggles);
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("Disconnected user {UserId} from server {ServerId}", userId, serverId);
    }
}
