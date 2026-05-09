using System.Text.Json;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class PluginAgentService : IPluginAgentService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PluginAgentService> _logger;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public PluginAgentService(IDbContextFactory<FaitV2DbContext> dbFactory, IWebHostEnvironment env, ILogger<PluginAgentService> logger)
    {
        _dbFactory = dbFactory;
        _env = env;
        _logger = logger;
    }

    public async Task<List<AgentPlugin>> GetAvailablePluginsAsync(string userId,
        IEnumerable<string> userRoles, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var activePlugins = await db.AgentPlugins
            .Where(p => p.IsActive)
            .ToListAsync(ct);

        var roles = userRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return activePlugins.Where(p =>
        {
            var allowedRoles = DeserializeStringList(p.AllowedRoles);
            return allowedRoles.Count == 0 || allowedRoles.Any(r => roles.Contains(r));
        }).ToList();
    }

    public async Task<List<AgentPlugin>> ListActivePluginsAsync(string userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AgentPlugins
            .Where(p => p.IsActive)
            .ToListAsync(ct);
    }

    public async Task<AgentPlugin?> GetPluginByIdAsync(string pluginId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.AgentPlugins
            .Where(p => p.Id == pluginId && p.IsActive)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AgentPlugin> CreatePluginAsync(string name, string description,
        string? skillsDirectory, List<McpServerPermission> allowedMcpServers,
        List<string> allowedRoles, string createdBy, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var plugin = new AgentPlugin
        {
            Name = name,
            Description = description,
            SkillsDirectory = skillsDirectory,
            AllowedMcpServers = JsonSerializer.Serialize(allowedMcpServers, _json),
            AllowedRoles = JsonSerializer.Serialize(allowedRoles, _json),
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.AgentPlugins.Add(plugin);
        await db.SaveChangesAsync(ct);
        return plugin;
    }

    public async Task<AgentPlugin> UpdatePluginAsync(string pluginId, string name, string description,
        string? skillsDirectory, List<McpServerPermission> allowedMcpServers,
        List<string> allowedRoles, bool isActive, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var plugin = await db.AgentPlugins.FindAsync(new object[] { pluginId }, ct)
            ?? throw new InvalidOperationException($"Plugin {pluginId} not found.");

        plugin.Name = name;
        plugin.Description = description;
        plugin.SkillsDirectory = skillsDirectory;
        plugin.AllowedMcpServers = JsonSerializer.Serialize(allowedMcpServers, _json);
        plugin.AllowedRoles = JsonSerializer.Serialize(allowedRoles, _json);
        plugin.IsActive = isActive;
        plugin.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return plugin;
    }

    public async Task<string> GetSkillsContentAsync(AgentPlugin plugin, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(plugin.SkillsDirectory))
            return string.Empty;

        if (plugin.SkillsDirectory.StartsWith("wwwroot/"))
        {
            if (string.IsNullOrEmpty(_env.WebRootPath))
            {
                _logger.LogWarning("WebRootPath is null; cannot resolve skills file for plugin {Name}", plugin.Name);
                return string.Empty;
            }

            // Local file — resolve against content root
            var filePath = Path.Combine(_env.WebRootPath,
                plugin.SkillsDirectory["wwwroot/".Length..]);
            if (File.Exists(filePath))
                return await File.ReadAllTextAsync(filePath, ct);
            _logger.LogWarning("Skills file not found: {Path}", filePath);
            return string.Empty;
        }

        // Future: blob path — return placeholder for now
        return $"# {plugin.Name} Agent\n\n{plugin.Description}";
    }

    private List<McpServerPermission> DeserializeMcpServers(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<McpServerPermission>>(json, _json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static List<string> DeserializeStringList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
