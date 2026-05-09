using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace FortressAI.V2.Web.Services;

public class ContextEnvelopeService : IContextEnvelopeService
{
    private readonly IWebHostEnvironment _env;
    private readonly IForgeKbService _forgeKbService;
    private readonly IConnectorService _connectorService;
    private readonly IPluginAgentService _pluginAgentService;
    private readonly IRAGReadService _ragReadService;
    private readonly ILogger<ContextEnvelopeService> _logger;

    public ContextEnvelopeService(
        IWebHostEnvironment env,
        IForgeKbService forgeKbService,
        IConnectorService connectorService,
        IPluginAgentService pluginAgentService,
        IRAGReadService ragReadService,
        ILogger<ContextEnvelopeService> logger)
    {
        _env = env;
        _forgeKbService = forgeKbService;
        _connectorService = connectorService;
        _pluginAgentService = pluginAgentService;
        _ragReadService = ragReadService;
        _logger = logger;
    }

    public string GetSystemClaudeMd()
    {
        var claudeDir = Path.Combine(_env.WebRootPath, "claude");
        var parts = new List<string>();

        var mainFile = Path.Combine(claudeDir, "CLAUDE.md");
        if (File.Exists(mainFile))
            parts.Add(File.ReadAllText(mainFile));

        var rulesDir = Path.Combine(claudeDir, "rules");
        if (Directory.Exists(rulesDir))
        {
            foreach (var ruleFile in Directory.GetFiles(rulesDir, "*.md").OrderBy(f => f))
                parts.Add(File.ReadAllText(ruleFile));
        }

        return string.Join("\n\n---\n\n", parts);
    }

    public async Task<CCContextEnvelope> BuildEnvelopeAsync(
        string userId,
        string userDisplayName,
        string? userEmail,
        string taskInstructions,
        string? pluginId = null,
        CancellationToken ct = default)
    {
        var kbIds = new List<string>();
        var enabledMcpServers = new List<string>();

        try
        {
            var kbs = await _forgeKbService.ListKbsAsync(userId, ct);
            kbIds.AddRange(kbs.Select(kb => kb.KbId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list FORGE KBs for user {UserId}", userId);
        }

        try
        {
            var connectors = await _connectorService.ListConnectorsAsync(userId, ct);
            enabledMcpServers.AddRange(connectors
                .Where(c => c.IsConnected)
                .Select(c => c.Name));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list connectors for user {UserId}", userId);
        }

        string? memorySummary = null;

        if (pluginId != null)
        {
            try
            {
                var plugin = await _pluginAgentService.GetPluginByIdAsync(pluginId, ct);
                if (plugin != null)
                {
                    // Merge plugin's read-enabled MCP servers into the envelope (union)
                    var pluginMcpServers = JsonSerializer.Deserialize<List<McpServerPermission>>(
                        plugin.AllowedMcpServers,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
                    var additionalServers = pluginMcpServers
                        .Where(s => s.Read && !enabledMcpServers.Contains(s.ServerId))
                        .Select(s => s.ServerId);
                    enabledMcpServers.AddRange(additionalServers);

                    // Append plugin skills to memory summary
                    var skills = await _pluginAgentService.GetSkillsContentAsync(plugin, ct);
                    if (!string.IsNullOrEmpty(skills))
                        memorySummary = $"# Plugin Skills\n{skills}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin {PluginId} for envelope", pluginId);
            }
        }

        // §4.3 — pgvector memory summary: top-3 relevant memory chunks
        try
        {
            var memChunks = await _ragReadService.SearchAsync(userId, taskInstructions, topK: 3, ct);
            if (memChunks.Count > 0)
            {
                var memSection = "## Relevant Memory\n" +
                    string.Join("\n", memChunks.Select(c =>
                        $"{c.TopicSlug}: {c.Content.Substring(0, Math.Min(200, c.Content.Length))}"));

                memorySummary = memorySummary != null
                    ? memorySummary + "\n\n" + memSection
                    : memSection;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ContextEnvelopeService: failed to retrieve memory summary for user {UserId}", userId);
        }

        // §6.1 — inject available specialist agents into system prompt
        try
        {
            var activePlugins = await _pluginAgentService.ListActivePluginsAsync(userId, ct);
            if (activePlugins.Count > 0)
            {
                var agentLines = activePlugins.Select(p =>
                    $"- **{p.Name}** — {p.Description}");
                var agentSection = "## Available Specialist Agents\n" +
                    "You have access to the following specialist agents. When a request is better served by one, suggest switching — do not handle it yourself.\n" +
                    string.Join("\n", agentLines) + "\n" +
                    "To suggest: say e.g. 'This looks like a great task for the Marketing Agent — want me to hand it off?' Do NOT switch autonomously. Always ask first.";
                memorySummary = memorySummary != null
                    ? memorySummary + "\n\n" + agentSection
                    : agentSection;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ContextEnvelopeService: failed to load plugin agents for user {UserId}", userId);
        }

        return new CCContextEnvelope
        {
            UserId = userId,
            UserDisplayName = userDisplayName,
            UserEmail = userEmail,
            KbIds = kbIds,
            EnabledMcpServers = enabledMcpServers,
            MemorySummary = memorySummary,
            TaskInstructions = taskInstructions,
        };
    }
}
