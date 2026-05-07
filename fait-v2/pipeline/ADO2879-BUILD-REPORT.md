# Build Report — ADO#2879

## What was built
Plugin agent framework for FAIT v2: `AgentPlugin` DB model, EF migration, `IPluginAgentService` + `PluginAgentService` implementation with role-based filtering, `ContextEnvelopeService` updated to accept optional `pluginId` (merges plugin MCP servers + skills into context), and plugin selector pill bar in `ChatView.razor`.

## Commits
- `3132b9f` — Primary delivery (all new files created, concurrent with #2877 sprint)
- `17ba9e5` — Cleanup: removed duplicate `agent_plugins` entity config, consolidated indexes

## Files changed
- `src/FortressAI.V2.Web/Data/Models/AgentPlugin.cs` — New model: string ID, Name, Description, SkillsDirectory, AllowedMcpServers (longtext JSON), AllowedRoles (longtext JSON), IsActive, CreatedBy, CreatedAt, UpdatedAt
- `src/FortressAI.V2.Web/Data/FaitV2DbContext.cs` — Added `AgentPlugins` DbSet, `agent_plugins` table config with max lengths, longtext JSON columns, name uniqueness index; cleanup of duplicate config in 17ba9e5
- `src/FortressAI.V2.Web/Data/Migrations/20260507180752_AddAgentPlugins.cs` — EF migration: CREATE TABLE `agent_plugins` with correct schema
- `src/FortressAI.V2.Web/Data/Migrations/20260507180752_AddAgentPlugins.Designer.cs` — EF migration designer snapshot
- `src/FortressAI.V2.Web/Data/Migrations/FaitV2DbContextModelSnapshot.cs` — Updated model snapshot
- `src/FortressAI.V2.Web/Services/IPluginAgentService.cs` — Interface: `GetAvailablePluginsAsync`, `GetPluginByIdAsync`, `CreatePluginAsync`, `UpdatePluginAsync`, `GetSkillsContentAsync` + `McpServerPermission` DTO
- `src/FortressAI.V2.Web/Services/PluginAgentService.cs` — Implementation: role-based filtering (empty AllowedRoles = available to all), skills blob fallback with log warning
- `src/FortressAI.V2.Web/Services/IContextEnvelopeService.cs` — Added optional `pluginId` param to `BuildEnvelopeAsync`
- `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs` — Loads plugin when `pluginId` provided; unions read-allowed MCP servers into envelope; appends plugin skills to `MemorySummary` under `# Plugin Skills`
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — Plugin selector pill bar above message input; `_selectedPluginId` session state; active plugin badge near send button; CSS variables only
- `src/FortressAI.V2.Web/Program.cs` — `builder.Services.AddScoped<IPluginAgentService, PluginAgentService>()`

## Parallelization used
Yes — #2877 (ScheduledTask) and #2879 (AgentPlugin) ran in the same CC session concurrently. All #2879 deliverables landed in `3132b9f`, with a follow-up cleanup commit `17ba9e5`.

## CC sessions run
1 CC run (concurrent with #2877). CC noted migration snapshot timing race and manually filled the migration body.

## Acceptance criteria verification
- [x] `AgentPlugin` model with correct column types — `longtext` for JSON fields, `HasMaxLength(36)` on Id/CreatedBy, `HasMaxLength(100)` on Name
- [x] EF migration `AddAgentPlugins` — present in `Migrations/20260507180752_AddAgentPlugins.cs`
- [x] `IPluginAgentService` interface clean — no implementation details
- [x] `PluginAgentService.GetAvailablePluginsAsync` filters by role correctly — includes plugin if `AllowedRoles` is empty OR userRoles intersect
- [x] `ContextEnvelopeService.BuildEnvelopeAsync` accepts optional pluginId, merges MCP servers (read:true only) and skills into MemorySummary
- [x] Plugin selector in ChatView.razor — MudChipSet pill bar, session-scoped state
- [x] `IPluginAgentService` registered as Scoped in Program.cs
- [x] No hardcoded role names, plugin names, or IDs
- [x] CSS variables only in Razor UI
- [x] dotnet build — **0 errors, 0 warnings**

## Known edge cases / things Clint should scrutinize
1. **Migration body** — CC flagged a snapshot timing race with concurrent #2877 migration. The `AddAgentPlugins` migration body was manually filled. Verify the `Up()`/`Down()` SQL matches the model exactly before running against a live DB.
2. **Duplicate entity config cleanup** — Original 3132b9f had duplicate `agent_plugins` config in two places. The 17ba9e5 cleanup removed the first block and kept the second (which includes the `is_active` index). Verify only one config block exists for `agent_plugins` in `FaitV2DbContext.cs`.
3. **Skills blob integration** — `GetSkillsContentAsync` returns a placeholder string when `SkillsDirectory` is null/empty. Blob wiring is deferred to a future sprint per the brief.
4. **Plugin selector passes pluginId to CC dispatch** — Verify the ChatView wiring passes `_selectedPluginId` through all `BuildEnvelopeAsync` call sites (CC dispatch + regular chat turns).

## How to test locally
```bash
cd ~/projects/fip/fait-v2
dotnet build src/FortressAI.V2.Web/FortressAI.V2.Web.csproj --configuration Release
# Verify migration (dry run):
dotnet ef migrations list --project src/FortressAI.V2.Web -- --environment Development
# Seed a test plugin in DB, verify plugin selector appears in ChatView for user with matching role
```

## Build result
```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.48
```

## ADO
- Project: Fortress, WI: #2879
- Comment ID: 782636 posted ✅
