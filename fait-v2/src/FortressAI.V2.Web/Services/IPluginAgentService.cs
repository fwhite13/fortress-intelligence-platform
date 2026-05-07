using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IPluginAgentService
{
    /// <summary>Get all active plugins visible to the given user (based on allowed roles).</summary>
    Task<List<AgentPlugin>> GetAvailablePluginsAsync(string userId, IEnumerable<string> userRoles,
        CancellationToken ct = default);

    /// <summary>Get a specific plugin by ID (admin or allowed user).</summary>
    Task<AgentPlugin?> GetPluginByIdAsync(string pluginId, CancellationToken ct = default);

    /// <summary>Create a new plugin (admin only — caller must enforce).</summary>
    Task<AgentPlugin> CreatePluginAsync(string name, string description, string? skillsDirectory,
        List<McpServerPermission> allowedMcpServers, List<string> allowedRoles,
        string createdBy, CancellationToken ct = default);

    /// <summary>Update plugin (admin only).</summary>
    Task<AgentPlugin> UpdatePluginAsync(string pluginId, string name, string description,
        string? skillsDirectory, List<McpServerPermission> allowedMcpServers,
        List<string> allowedRoles, bool isActive, CancellationToken ct = default);

    /// <summary>Get skill content for a plugin (reads markdown from skills directory).</summary>
    Task<string> GetSkillsContentAsync(AgentPlugin plugin, CancellationToken ct = default);
}

public class McpServerPermission
{
    public string ServerId { get; set; } = string.Empty;
    public bool Read { get; set; } = true;
    public bool Write { get; set; } = false;
}
