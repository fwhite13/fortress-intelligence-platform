using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public class ConnectorService : IConnectorService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly ILogger<ConnectorService> _logger;

    private static readonly Dictionary<string, (string DisplayName, string Description, ConnectorAuthType AuthType)> ConnectorMeta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["forge-kb"] = ("FORGE Knowledge Base", "Search and add to your organization's knowledge bases", ConnectorAuthType.None),
            ["ms365"]    = ("Microsoft 365", "Email, calendar, Teams, OneDrive, SharePoint", ConnectorAuthType.OAuthEntra),
            ["search"]   = ("Web Search", "Search the web via Brave Search API", ConnectorAuthType.None),
            ["ado"]      = ("Azure DevOps", "Work items, repos, pipelines", ConnectorAuthType.OAuthEntra),
        };

    // Connectors that are always connected (service-level, no per-user OAuth)
    private static readonly HashSet<string> ManagedConnectors =
        new(StringComparer.OrdinalIgnoreCase) { "forge-kb", "search" };

    public ConnectorService(IDbContextFactory<FaitV2DbContext> dbFactory, ILogger<ConnectorService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConnectorViewModel>> ListConnectorsAsync(string entraOid, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var servers = await db.McpServers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid, ct);

        HashSet<string> connectedServers = new(StringComparer.OrdinalIgnoreCase);
        if (user != null)
        {
            var tokens = await db.McpUserTokens
                .Where(t => t.UserId == user.Id)
                .ToListAsync(ct);

            foreach (var token in tokens)
            {
                if (token.TokenExpiresAt.HasValue && token.TokenExpiresAt.Value < DateTime.UtcNow)
                {
                    _logger.LogWarning("Token for user {UserId} server {Server} is expired", user.Id, token.ServerName);
                }
                // Still count as connected even if expired (display only)
                connectedServers.Add(token.ServerName);
            }
        }

        var result = new List<ConnectorViewModel>(servers.Count);
        foreach (var server in servers)
        {
            var (displayName, description, authType) = ConnectorMeta.TryGetValue(server.Name, out var meta)
                ? meta
                : (server.Name, string.Empty, ConnectorAuthType.OAuthEntra);

            bool isConnected;
            DateTime? connectedAt = null;

            if (ManagedConnectors.Contains(server.Name))
            {
                isConnected = true;
            }
            else
            {
                isConnected = user != null && connectedServers.Contains(server.Name);
            }

            result.Add(new ConnectorViewModel(
                Name: server.Name,
                DisplayName: displayName,
                Description: description,
                IsConnected: isConnected,
                CanRead: server.DefaultRead,
                CanWrite: server.DefaultWrite,
                AuthType: authType,
                ConnectedAt: connectedAt
            ));
        }

        return result;
    }

    public async Task<ConnectorStatus> GetConnectionStatusAsync(string entraOid, string serverName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid, ct);
        if (user == null)
            return ConnectorStatus.NotConnected;

        var token = await db.McpUserTokens
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.ServerName == serverName, ct);

        if (token == null)
            return ConnectorStatus.NotConnected;

        if (token.TokenExpiresAt.HasValue && token.TokenExpiresAt.Value < DateTime.UtcNow)
            return ConnectorStatus.TokenExpired;

        return ConnectorStatus.Connected;
    }

    public async Task RevokeConnectionAsync(string entraOid, string serverName, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == entraOid, ct);
        if (user == null)
            throw new InvalidOperationException($"User with EntraOid '{entraOid}' not found.");

        var token = await db.McpUserTokens
            .FirstOrDefaultAsync(t => t.UserId == user.Id && t.ServerName == serverName, ct);

        if (token != null)
        {
            db.McpUserTokens.Remove(token);
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Revoked token for user {UserId} server {Server}", user.Id, serverName);
        }
    }
}
