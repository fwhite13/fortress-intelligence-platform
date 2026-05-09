# Build Report — ADO#3106
## G3: KB Write Intent Enforcement (AllowKbWrites in Context Envelope)

### What was built
Added `AllowKbWrite` bool to `AgentPlugin`, wired it through the full stack: EF migration → services → TurnRequest → harness enforcement → context envelope §7 → admin UI toggle.

### Files changed
- `src/FortressAI.V2.Web/Data/Models/AgentPlugin.cs` — Added `AllowKbWrite` bool field (default `false`)
- `src/FortressAI.V2.Web/Data/FaitV2DbContext.cs` — Added `entity.Property(e => e.AllowKbWrite).HasColumnName("allow_kb_write").HasDefaultValue(false)` in agent_plugins entity config
- `src/FortressAI.V2.Web/Data/Migrations/20260509090000_AddAllowKbWriteToAgentPlugin.cs` — EF migration: `ALTER TABLE agent_plugins ADD COLUMN allow_kb_write tinyint(1) NOT NULL DEFAULT 0`
- `src/FortressAI.V2.Web/Data/Migrations/20260509090000_AddAllowKbWriteToAgentPlugin.Designer.cs` — Designer file for the migration
- `src/FortressAI.V2.Web/Data/Migrations/FaitV2DbContextModelSnapshot.cs` — Updated snapshot with `AllowKbWrite` property
- `src/FortressAI.V2.Web/Services/IPluginAgentService.cs` — Added `bool allowKbWrite = false` parameter to `CreatePluginAsync` and `UpdatePluginAsync`
- `src/FortressAI.V2.Web/Services/PluginAgentService.cs` — `AllowKbWrite = allowKbWrite` in Create; `plugin.AllowKbWrite = allowKbWrite` in Update
- `src/FortressAI.V2.Web/Services/IUserAgentRuntime.cs` — Added `bool KbWriteAllowed = true` to `TurnRequest` record with §G3 comment
- `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs` — §7: appends "KB Write Access: allowed/not allowed" block to system prompt when plugin is active
- `src/FortressAI.V2.Web/Components/Chat/ChatView.razor` — TurnRequest now passes `KbWriteAllowed: activePlugin?.AllowKbWrite ?? true`
- `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor` — "Allow KB Write" toggle in form; wired to `_formAllowKbWrite`; passed to CreatePluginAsync/UpdatePluginAsync
- `agent-harness/harness-server.js` — `/turn` handler extracts `kbWriteAllowed` from request body; passes `HARNESS_KB_WRITE_ALLOWED` + `HARNESS_PLUGIN_AGENT_ID` env to CC spawn path; `/tools/:toolName` handler blocks `kb_write|kb_upsert|kb_create|knowledge_write` tools when `kbWriteAllowed === false` and `pluginAgentId` is set

### Parallelization used
No — C# groundwork landed in `81c87174` (alongside ADO#3107 scheduled task work), harness/UI/envelope in `3741e1bf`.

### CC sessions run
1 CC Sonnet session for the combined 3101/3106 build.

### Acceptance criteria verification
- [x] `AgentPlugin.AllowKbWrite` field + EF migration `20260509090000_AddAllowKbWriteToAgentPlugin` — confirmed in DB context + migration files
- [x] `TurnRequest.KbWriteAllowed` parameter — added to record with default `true`
- [x] Harness blocks KB write tools when `kbWriteAllowed === false` — KB_WRITE_PATTERNS + check in `/tools/:toolName`
- [x] Context envelope includes KB write status — §7 block in `ContextEnvelopeService`
- [x] Admin panel has "Allow KB Write" toggle — confirmed in AgentPlugins.razor
- [x] `dotnet build` 0 errors — verified
- [x] `node --check` passes — verified

### Known edge cases / things Clint should scrutinize
1. **Default agent (no pluginAgentId)**: `kbWriteAllowed` defaults to `true` in both TurnRequest and harness. This means the main assistant can still write to KB freely. The restriction applies only to plugin agents. This aligns with the spec but may need a future gate for the main agent too.
2. **ToggleActive clears AllowKbWrite**: Same issue as ADO#3101 — `ToggleActive` passes `false` for `allowKbWrite` rather than preserving the existing value. Pre-existing pattern, but flag for Clint.
3. **CC spawn path**: `HARNESS_KB_WRITE_ALLOWED` env var is passed to the CC process. CC itself doesn't read this env var — it's available for any shell-level scripts CC might spawn. The actual enforcement is at the `/tools/:toolName` HTTP layer, which the harness controls regardless of CC.
4. **Migration ordering**: `20260509090000` timestamp is after the seed migration `20260509080000_SeedDefaultAgentPlugins.cs`. EF will apply in timestamp order correctly.

### How to test locally
1. Create a plugin agent with "Allow KB Write" OFF
2. In a chat with that agent active, trigger a KB write tool call (e.g. `kb_write` or `kb_upsert`)
3. Harness should return 403 `{ "error": "KB write not permitted for this agent" }`
4. Enable "Allow KB Write" for the agent
5. Retry → tool call proceeds normally
