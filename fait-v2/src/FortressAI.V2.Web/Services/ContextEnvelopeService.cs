using Microsoft.AspNetCore.Hosting;

namespace FortressAI.V2.Web.Services;

public class ContextEnvelopeService : IContextEnvelopeService
{
    private readonly IWebHostEnvironment _env;
    private readonly IForgeKbService _forgeKbService;
    private readonly IConnectorService _connectorService;
    private readonly ILogger<ContextEnvelopeService> _logger;

    public ContextEnvelopeService(
        IWebHostEnvironment env,
        IForgeKbService forgeKbService,
        IConnectorService connectorService,
        ILogger<ContextEnvelopeService> logger)
    {
        _env = env;
        _forgeKbService = forgeKbService;
        _connectorService = connectorService;
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
        string taskInstructions,
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

        return new CCContextEnvelope
        {
            UserId = userId,
            UserDisplayName = userDisplayName,
            KbIds = kbIds,
            EnabledMcpServers = enabledMcpServers,
            MemorySummary = null,
            TaskInstructions = taskInstructions,
        };
    }
}
