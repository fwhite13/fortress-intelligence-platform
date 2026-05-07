# ADO#2879 — FAIT v2 Plugin Agent Framework — BUILD Brief

## Spec
`memory/projects/fait-v2-spec-2026-04-27.md §6.1, §6.4, §6.5`
Feature: Epic F (Agent/Plugin System)
Sprint: FAIT v2 Sprint 5

## Context
Current HEAD: `7dbe42b` on `main`. fait-v2 repo: `/home/fredw/projects/fip/fait-v2/`

Plugin agents are admin-provisioned skill agents. They appear as options the user invokes from their main assistant. Each agent bundles: skills (markdown files), connector configuration (which MCP servers, with per-connector read/write flags), and allowed roles.

## What to Build

### 1. Aurora DB Model

**`Data/Models/AgentPlugin.cs`**
```csharp
public class AgentPlugin
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;         // e.g. "Marketing", "Finance"
    public string Description { get; set; } = string.Empty;
    public string? SkillsDirectory { get; set; }              // Azure Blob path to skills markdown files
    public string AllowedMcpServers { get; set; } = "[]";    // JSON: [{"server_id":"ms365","read":true,"write":false}]
    public string AllowedRoles { get; set; } = "[]";          // JSON: array of Entra role names
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }                    // FK → users.id
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

### 2. FaitV2DbContext updates

Add to `Data/FaitV2DbContext.cs`:
```csharp
public DbSet<AgentPlugin> AgentPlugins => Set<AgentPlugin>();
```

`OnModelCreating` config:
- Table name: `agent_plugins`
- `Id`, `CreatedBy`: `HasMaxLength(36)`
- `Name`: `HasMaxLength(100)`, `IsRequired()`
- `AllowedMcpServers`, `AllowedRoles`: column type `longtext` (JSON stored as string)

### 3. EF Core Migration

Generate migration `AddAgentPlugins`.
Core API only — no raw SQL.

### 4. IPluginAgentService + PluginAgentService

**`Services/IPluginAgentService.cs`**
```csharp
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
```

**`Services/PluginAgentService.cs`** — implementation:
- `GetAvailablePluginsAsync`: query `AgentPlugins` where `IsActive = true`. Filter by role: deserialize `AllowedRoles` JSON, include the plugin if `userRoles` contains any of the allowed roles OR if `AllowedRoles` is empty array (available to all).
- `GetSkillsContentAsync`: if `SkillsDirectory` is null/empty, return empty string. Otherwise read the skills markdown from Azure Blob Storage (use `IWorkspaceService` or direct S3/Blob call). For Sprint 5 MVP, if blob integration isn't wired yet, return `$"# {plugin.Name} Agent\n\n{plugin.Description}"` as a fallback — log a warning.
- `CreatePluginAsync` / `UpdatePluginAsync`: serialize `McpServerPermission` list to JSON for `AllowedMcpServers`, serialize roles list for `AllowedRoles`. Set `UpdatedAt = DateTime.UtcNow`.

### 5. Plugin-Aware Context Envelope Update

Update `ContextEnvelopeService.BuildEnvelopeAsync()` to accept an optional `pluginId` parameter:
```csharp
Task<CCContextEnvelope> BuildEnvelopeAsync(
    string userId,
    string userDisplayName,
    string taskInstructions,
    string? pluginId = null,
    CancellationToken ct = default);
```

When `pluginId` is provided:
1. Load the `AgentPlugin` from DB
2. Merge plugin's `AllowedMcpServers` into the envelope's `EnabledMcpServers` (union with user's own connectors, filtered to `read: true`)
3. Append the plugin's skills content to `MemorySummary` (prefix with `# Plugin Skills\n`)

### 6. Plugin Selector in ChatView.razor

Add a subtle plugin selector to `ChatView.razor` — a MudSelect or MudChipSet above the message input showing available plugins for the current user. "None (Main Assistant)" is the default.

When a plugin is selected:
- Store `_selectedPluginId` in component state
- Pass it to `BuildEnvelopeAsync` when dispatching CC or chat turns
- Show the active plugin name near the send button (e.g., `[Marketing]`)
- Selection persists for the session but not across page reloads

Use CSS variables only — no hardcoded colors.

### 7. Registration in Program.cs
```csharp
builder.Services.AddScoped<IPluginAgentService, PluginAgentService>();
```

Update `IContextEnvelopeService` interface and `ContextEnvelopeService` to include the optional `pluginId` parameter.

## Acceptance Criteria
- [ ] `AgentPlugin` model with correct column types
- [ ] EF migration `AddAgentPlugins`
- [ ] `IPluginAgentService` interface clean — no implementation details
- [ ] `PluginAgentService.GetAvailablePluginsAsync` filters by role correctly
- [ ] `ContextEnvelopeService.BuildEnvelopeAsync` accepts optional pluginId, merges MCP servers and skills
- [ ] Plugin selector in ChatView.razor (MudSelect or MudChipSet)
- [ ] `IPluginAgentService` registered as Scoped in Program.cs
- [ ] No hardcoded role names, plugin names, or IDs
- [ ] CSS variables only in Razor UI
- [ ] dotnet build 0 errors

## Rules
- string IDs (Guid.NewGuid().ToString()) — NOT Guid type
- GuidFormat=None on all Aurora connections
- No Cognito references
- CSS variable rule MANDATORY for any UI added

## MANDATORY: Use Claude Code CLI
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2879-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

## ADO Comment (add after build)
Project: Fortress, ID: 2879
```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: AgentPlugin model, EF migration AddAgentPlugins, IPluginAgentService + impl, ContextEnvelopeService pluginId support, plugin selector in ChatView. Build: SUCCEEDED.
```
