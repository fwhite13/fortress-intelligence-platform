using System.Text.Json;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class PluginAgentService : IPluginAgentService
{
    private readonly FaitV2DbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PluginAgentService> _logger;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public PluginAgentService(FaitV2DbContext db, IWebHostEnvironment env, ILogger<PluginAgentService> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    public async Task<List<AgentPlugin>> GetAvailablePluginsAsync(string userId,
        IEnumerable<string> userRoles, CancellationToken ct = default)
    {
        var activePlugins = await _db.AgentPlugins
            .Where(p => p.IsActive)
            .ToListAsync(ct);

        var roles = userRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return activePlugins.Where(p =>
        {
            var allowedRoles = DeserializeStringList(p.AllowedRoles);
            return allowedRoles.Count == 0 || allowedRoles.Any(r => roles.Contains(r));
        }).ToList();
    }

    public async Task<AgentPlugin?> GetPluginByIdAsync(string pluginId, CancellationToken ct = default)
    {
        return await _db.AgentPlugins.FindAsync(new object[] { pluginId }, ct);
    }

    public async Task<AgentPlugin> CreatePluginAsync(string name, string description,
        string? skillsDirectory, List<McpServerPermission> allowedMcpServers,
        List<string> allowedRoles, string createdBy, CancellationToken ct = default)
    {
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

        _db.AgentPlugins.Add(plugin);
        await _db.SaveChangesAsync(ct);
        return plugin;
    }

    public async Task<AgentPlugin> UpdatePluginAsync(string pluginId, string name, string description,
        string? skillsDirectory, List<McpServerPermission> allowedMcpServers,
        List<string> allowedRoles, bool isActive, CancellationToken ct = default)
    {
        var plugin = await _db.AgentPlugins.FindAsync(new object[] { pluginId }, ct)
            ?? throw new InvalidOperationException($"Plugin {pluginId} not found.");

        plugin.Name = name;
        plugin.Description = description;
        plugin.SkillsDirectory = skillsDirectory;
        plugin.AllowedMcpServers = JsonSerializer.Serialize(allowedMcpServers, _json);
        plugin.AllowedRoles = JsonSerializer.Serialize(allowedRoles, _json);
        plugin.IsActive = isActive;
        plugin.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return plugin;
    }

    public async Task<string> GetSkillsContentAsync(AgentPlugin plugin, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(plugin.SkillsDirectory))
            return string.Empty;

        if (plugin.SkillsDirectory.StartsWith("wwwroot/"))
        {
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
