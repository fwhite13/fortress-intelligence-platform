using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using FortressAI.Web.Services.Mcp;

namespace FortressAI.Web.Services;

public interface IMcpRegistryService
{
    Task<List<McpServer>> GetActiveServersAsync();
    Task<McpServer?> GetBySlugAsync(string slug);
    Task<McpServer?> GetByIdAsync(Guid id);
    Task<McpServer> CreateServerAsync(CreateMcpServerRequest request);
    Task UpdateServerAsync(Guid serverId, UpdateMcpServerRequest request);
    Task SetServerActiveAsync(Guid serverId, bool active);
    Task<List<McpToolDefinition>> RefreshToolManifestAsync(Guid serverId);
    /// <summary>
    /// Returns the deserialized AuthConfig for a server with the ClientSecret decrypted.
    /// Returns null if the server has no auth config or if auth_type is not oauth2.
    /// </summary>
    Task<McpOAuthConfig?> GetDecryptedAuthConfigAsync(Guid serverId);
    /// <summary>
    /// Returns the decrypted OAuthClientSecret for a server, or null if not set.
    /// </summary>
    Task<string?> GetDecryptedClientSecretAsync(Guid serverId);
}

public class McpRegistryService : IMcpRegistryService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IDataProtector _adminKeyProtector;
    private readonly McpHttpTransport _transport;
    private readonly ILogger<McpRegistryService> _logger;

    public McpRegistryService(
        IDbContextFactory<AppDbContext> dbFactory,
        IDataProtectionProvider dataProtectionProvider,
        McpHttpTransport transport,
        ILogger<McpRegistryService> logger)
    {
        _dbFactory = dbFactory;
        _adminKeyProtector = dataProtectionProvider.CreateProtector("McpAdmin.SystemKeys.v1");
        _transport = transport;
        _logger = logger;
    }

    public async Task<List<McpServer>> GetActiveServersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.McpServers
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<McpServer?> GetBySlugAsync(string slug)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.McpServers.FirstOrDefaultAsync(s => s.Slug == slug);
    }

    public async Task<McpServer?> GetByIdAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.McpServers.FindAsync(id);
    }

    public async Task<McpServer> CreateServerAsync(CreateMcpServerRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = new McpServer
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Slug = request.Slug,
            Description = request.Description,
            IconUrl = request.IconUrl,
            TransportType = request.TransportType,
            EndpointUrl = request.EndpointUrl,
            AuthType = request.AuthType,
            RequiresUserAuth = request.RequiresUserAuth,
            AuthConfigJson = EncryptClientSecretInAuthConfig(request.AuthType, request.AuthConfigJson),
            RateLimitPerMinute = request.RateLimitPerMinute,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        if (!string.IsNullOrEmpty(request.SystemApiKey))
            server.SystemApiKey = _adminKeyProtector.Protect(request.SystemApiKey);
        if (!string.IsNullOrEmpty(request.OAuthClientSecret))
            server.OAuthClientSecret = _adminKeyProtector.Protect(request.OAuthClientSecret);
        db.McpServers.Add(server);
        await db.SaveChangesAsync();
        return server;
    }

    public async Task UpdateServerAsync(Guid serverId, UpdateMcpServerRequest request)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FindAsync(serverId);
        if (server is null) return;
        if (request.Name is not null) server.Name = request.Name;
        if (request.Description is not null) server.Description = request.Description;
        if (request.IconUrl is not null) server.IconUrl = request.IconUrl;
        if (request.EndpointUrl is not null) server.EndpointUrl = request.EndpointUrl;
        if (request.AuthType is not null) server.AuthType = request.AuthType;
        if (request.RequiresUserAuth.HasValue) server.RequiresUserAuth = request.RequiresUserAuth.Value;
        if (request.AuthConfigJson is not null)
            server.AuthConfigJson = EncryptClientSecretInAuthConfig(request.AuthType ?? server.AuthType, request.AuthConfigJson);
        if (!string.IsNullOrEmpty(request.SystemApiKey))
            server.SystemApiKey = _adminKeyProtector.Protect(request.SystemApiKey);
        if (!string.IsNullOrEmpty(request.OAuthClientSecret))
            server.OAuthClientSecret = _adminKeyProtector.Protect(request.OAuthClientSecret);
        if (request.RateLimitPerMinute.HasValue) server.RateLimitPerMinute = request.RateLimitPerMinute.Value;
        server.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetServerActiveAsync(Guid serverId, bool active)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FindAsync(serverId);
        if (server is null) return;
        server.IsActive = active;
        server.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<McpToolDefinition>> RefreshToolManifestAsync(Guid serverId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FindAsync(serverId);
        if (server is null || string.IsNullOrEmpty(server.EndpointUrl))
            return new List<McpToolDefinition>();
        try
        {
            var tools = await _transport.ListToolsAsync(server.EndpointUrl, ct: default);
            server.ToolManifestJson = JsonSerializer.Serialize(tools);
            server.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh tool manifest for server {ServerId}", serverId);
            return new List<McpToolDefinition>();
        }
    }

    public async Task<McpOAuthConfig?> GetDecryptedAuthConfigAsync(Guid serverId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FindAsync(serverId);
        if (server is null || server.AuthType != "oauth2" || string.IsNullOrEmpty(server.AuthConfigJson))
            return null;

        try
        {
            var config = JsonSerializer.Deserialize<McpOAuthConfig>(server.AuthConfigJson);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize AuthConfig for server {ServerId}", serverId);
            return null;
        }
    }

    public async Task<string?> GetDecryptedClientSecretAsync(Guid serverId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var server = await db.McpServers.FindAsync(serverId);
        if (server is null || string.IsNullOrEmpty(server.OAuthClientSecret)) return null;
        try { return _adminKeyProtector.Unprotect(server.OAuthClientSecret); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt OAuthClientSecret for server {ServerId}", serverId);
            return null;
        }
    }

    /// <summary>
    /// Sanitizes the AuthConfigJson by round-tripping through McpOAuthConfig, which strips any
    /// stray ClientSecret values (ClientSecret is now stored separately as McpServer.OAuthClientSecret).
    /// Returns the original json unchanged for non-oauth2 types.
    /// </summary>
    private string? EncryptClientSecretInAuthConfig(string? authType, string? authConfigJson)
    {
        if (authType != "oauth2" || string.IsNullOrEmpty(authConfigJson))
            return authConfigJson;

        try
        {
            var config = JsonSerializer.Deserialize<McpOAuthConfig>(authConfigJson);
            if (config is null) return authConfigJson;
            return JsonSerializer.Serialize(config);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sanitize auth_config — storing as-is");
            return authConfigJson;
        }
    }
}
