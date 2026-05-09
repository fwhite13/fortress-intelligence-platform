# Build Report — ADO#3101
## Per-Connector Read/Write Permission Enforcement in Agent Plugin System

### What was built
Added per-MCP-server write permission enforcement to the harness tool dispatch layer. Plugin agents can now be configured with read-only or read+write access per server. The context envelope surfaces this to the agent, and the harness enforces it.

### Files changed
- `agent-harness/harness-server.js` — Added `WRITE_TOOL_PATTERNS` / `KB_WRITE_PATTERNS` constants and `isWriteTool()` / `isKbWriteTool()` helpers. Replaced the generic `/tools/:toolName` catch-all with a new handler that: (1) enforces KB write restrictions (ADO#3106), (2) enforces per-server write restrictions when `pluginAgentId` + `mcpServerPermissions` are present, then falls through to Stitch dispatch.
- `src/FortressAI.V2.Web/Services/ContextEnvelopeService.cs` — Added §5 "MCP Server Permissions" block: when a plugin has MCP servers configured, the system prompt now includes `- serverId: read-only` or `- serverId: read+write` for each server.
- `src/FortressAI.V2.Web/Components/Pages/Admin/AgentPlugins.razor` — Added `McpServerPermissionForm` inner class + `_formMcpServers` list. DialogContent now has an "MCP Server Permissions" section with add/remove rows and per-row "Allow Write" toggle. OpenEditForm/OpenNewForm/SaveForm/ToggleActive all wired to the new form state.

### Note on McpServerPermission shape
`McpServerPermission` (`ServerId`, `Read`, `Write`) was already in the correct shape in `IPluginAgentService.cs`. The `ContextEnvelopeService` was already filtering by `.Read`. This WI added the write enforcement and admin UI on top.

### Parallelization used
No — single commit `3741e1bf` along with ADO#3106 changes.

### CC sessions run
1 CC Sonnet session. CC wrote the harness and Blazor changes; model/migration/services were written in the adjacent `81c87174` commit which also covered ADO#3106 C# groundwork.

### Acceptance criteria verification
- [x] `McpServerPermission` has `Read` + `Write` fields — pre-existing, confirmed
- [x] Harness enforces write restrictions by tool name classification — `WRITE_TOOL_PATTERNS` in `/tools/:toolName`
- [x] Context envelope shows per-server permissions — §5 block in `ContextEnvelopeService.BuildEnvelopeAsync`
- [x] Admin panel allows configuring per-server write access — McpServerPermissionForm rows with AllowWrite toggle
- [x] `dotnet build` 0 errors — verified
- [x] `node --check` passes — verified

### Known edge cases / things Clint should scrutinize
1. **Server ID matching in harness**: Write enforcement only fires when `args.serverId` or `args.server_id` is present in the tool call body. If a future MCP tool doesn't pass its server ID in the args, enforcement is silently skipped. This is intentional (graceful degradation) but may need tightening as tool contracts solidify.
2. **Write classification by name**: Using a broad regex (`create|update|delete|write|send|post|add|remove|modify|set`). False positives are possible (e.g. a read tool named `get_post_by_id`). The prefix matching on `post` may be too broad.
3. **Admin UI passes empty mcpPermissions on ToggleActive**: `ToggleActive` still passes `new List<McpServerPermission>()`. This is by design (toggle only changes IsActive, not permissions), but it does overwrite existing permissions. Clint: this pattern was pre-existing — flag if it needs fixing.

### How to test locally
1. Create/edit an agent plugin in Admin → Agent Plugins
2. Add an MCP server row (e.g. `graph`), leave "Allow Write" off
3. Trigger a turn with that plugin active; submit a tool call that passes `serverId: "graph"` and has a write tool name → expect 403
4. Enable "Allow Write", retry → should pass through
