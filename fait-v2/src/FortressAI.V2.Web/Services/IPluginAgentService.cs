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
        string createdBy, bool allowKbWrite = false, CancellationToken ct = default);

    /// <summary>Update plugin (admin only).</summary>
    Task<AgentPlugin> UpdatePluginAsync(string pluginId, string name, string description,
        string? skillsDirectory, List<McpServerPermission> allowedMcpServers,
        List<string> allowedRoles, bool isActive, bool allowKbWrite = false, CancellationToken ct = default);

    /// <summary>Get skill content for a plugin (reads markdown from skills directory).</summary>
    Task<string> GetSkillsContentAsync(AgentPlugin plugin, CancellationToken ct = default);

    /// <summary>List all active plugins (no user filter — all active plugins visible to all users).</summary>
    Task<List<AgentPlugin>> ListActivePluginsAsync(string userId, CancellationToken ct = default);
}

/// <summary>
/// A fact discovered by a plugin agent during conversation.
/// No confidence scoring — all facts returned by plugin agents are persisted.
/// Quality gate is in plugin prompt engineering, not post-hoc filtering.
/// </summary>
public record DiscoveredFact(string Fact, string Source);

public class McpServerPermission
{
    public string ServerId { get; set; } = string.Empty;
    public bool Read { get; set; } = true;
    public bool Write { get; set; } = false;
}
