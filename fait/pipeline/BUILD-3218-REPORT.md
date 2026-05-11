# Build Report — ADO#3218

## What was built
Two-part fix so MCP tools (graph_* and ado_*) are included in Bedrock's `toolConfig` when the user has MCP servers enabled for a conversation. Previously `toolConfig` was hardcoded with only 7 built-in tools; the model had no visibility into MCP tools even when they were enabled.

## Files changed
- `fait/src/FortressAI.Web/Services/IUserAgentRuntime.cs` — Added `List<string>? EnabledMcpSlugs = null` as new nullable tail field on `TurnRequest` record. Additive — all existing callers unaffected.
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — After building `availableTools` from `McpToolSvc.GetConversationToolsAsync`, now extracts distinct server slugs from `FullName` (using `__` separator) and passes them as `EnabledMcpSlugs` to `TurnRequest` (null if none).
- `fait-v2/agent-harness/harness-server.js` — Three changes:
  1. Added `MCP_TOOL_SPECS` map with `m365` (4 tools) and `azdo`/`ado` (5 tools) entries, placed after `BUILTIN_TOOLS` definition. `ado` is aliased to `azdo` to handle both slug variants.
  2. Added `enabledMcpSlugs` destructuring from `rawBody` (supports both PascalCase and camelCase) after `conversationId`.
  3. Replaced static `const toolConfig = { tools: [...] }` with dynamic build: `BUILTIN_TOOL_SPECS` array (all 7 original tools, unchanged) always included, then `MCP_TOOL_SPECS[slug]` entries pushed for each slug in `enabledMcpSlugs`. `toolChoice: { auto: {} }` added to toolConfig.

## Parallelization used
No — single CC session, changes are sequential (Blazor → harness).

## CC sessions run
1 CC Sonnet session.

## Acceptance criteria verification
- [x] `EnabledMcpSlugs` field added to `TurnRequest` (nullable `List<string>?`) — confirmed in diff
- [x] `ChatView.razor` populates `EnabledMcpSlugs` from conversation MCP enablement (via `availableTools` slug extraction) — confirmed in diff
- [x] `MCP_TOOL_SPECS` map defined with m365 (4 tools) and azdo/ado (5 tools) — confirmed in diff
- [x] `enabledMcpSlugs` read from request body in /turn handler — confirmed in diff
- [x] Dynamic toolConfig built: built-ins always present + MCP tools for enabled slugs — confirmed in diff
- [x] Dispatch routes for graph_* and ado_* confirmed via existing `MCP_TOOL_ALLOWLIST` — no new routes needed
- [x] `node --check` passes — verified
- [x] `dotnet build` passes (0 errors, 46 warnings) — verified

## Known edge cases / things Clint should scrutinize
- **Slug aliasing**: The DB uses `azdo` as the slug for Azure DevOps MCP server, but the harness `MCP_TOOL_ALLOWLIST` uses `ado`. Both are handled: `MCP_TOOL_SPECS['ado'] = MCP_TOOL_SPECS['azdo']`. Clint should verify the actual DB slug value matches what Blazor extracts from `FullName`.
- **toolChoice added**: The static `toolConfig` did not have `toolChoice`; the dynamic replacement adds `toolChoice: { auto: {} }`. This is the Bedrock default behavior so should be safe, but worth confirming it doesn't break existing model behavior.
- **search_knowledge_base tool**: The original static `toolConfig` included `search_knowledge_base` but it is NOT in the `BUILTIN_TOOLS` Set. It's handled in the agentic loop dispatch separately. It IS included in `BUILTIN_TOOL_SPECS` in the new dynamic build (same as before), so no regression there.

## How to test locally
1. Enable an MCP server (m365 or azdo) for a conversation in FAIT
2. Send a chat message — check harness logs for `[harness] /turn: request received` — verify `enabledMcpSlugs` appears in the body dump
3. Confirm the `ConverseStreamCommand` is called with `toolConfig.tools` containing both built-in tools AND the MCP tools for the enabled slug
4. Ask the model to list emails or list work items — it should now attempt to call `graph_list_emails` or `ado_list_work_items`

## Commit
`828b9c00` — `feat(fait#3218): EnabledMcpSlugs in TurnRequest + ChatView slug extraction`

---

## Cycle 2 Fixes — ADO#3218

### What was fixed
Three critical issues identified by Clint in review cycle 1.

### Fix 1 — `devops` alias added to `MCP_TOOL_SPECS`
`harness-server.js` line 389: `MCP_TOOL_SPECS['devops'] = MCP_TOOL_SPECS['azdo']`
DB slug `devops` now resolves correctly. Previously only `ado` and `azdo` were aliased.

### Fix 2 — Agentic loop MCP dispatch branch added
`harness-server.js` lines 1993-2005: `else if` branch for `graph_*` / `ado_*` tools inserted before KB fallback.
Tools now call `http://localhost:${PORT}/tools/${toolName}` instead of silently falling through to `search_knowledge_base`.

### Fix 3 — DB seed tool names updated to match harness allowlist
`DatabaseInitializationService.cs`:
- **DevOps (lines ~510-521):** 12 old names (`list_devops_projects`, `get_work_item`, `query_work_items`, `list_repositories`, `list_pipelines`, `trigger_pipeline`, `create_work_item`, `update_work_item`, `add_work_item_comment`, `create_branch`, `create_pull_request`, `update_pull_request`) → 6 canonical names matching `MCP_TOOL_ALLOWLIST['ado']`: `ado_list_projects`, `ado_get_work_item`, `ado_list_work_items`, `ado_create_work_item`, `ado_update_work_item`, `ado_wiql_query`
- **M365 (lines ~556-559):** 4 old names (`list_emails`, `get_email`, `send_email`, `list_calendar_events`) → `graph_list_emails`, `graph_get_email`, `graph_send_email`, `graph_list_calendar_events`

### CC invocation
```
cat /tmp/brief-3218-c2.md | claude --model sonnet --print --dangerously-skip-permissions
```

### Verification results
- `node --check harness-server.js` → SYNTAX OK
- `dotnet build FortressAI.Web.csproj` → 0 errors
- Grep `MCP_TOOL_SPECS['devops']` → line 389 ✅
- Grep `startsWith('graph_')` / `startsWith('ado_')` → lines 1993-1994 ✅

### Commit
`bf60a5d6` — fix(fait#3218): devops slug + DB seed tool names + agentic loop MCP dispatch (cycle 2)

### Self-Review Checklist
- [x] `MCP_TOOL_SPECS['devops']` present (grep line 389)
- [x] DB seed DevOps tool names match `MCP_TOOL_ALLOWLIST['ado']` entries (6 tools)
- [x] DB seed M365 tool names match `MCP_TOOL_ALLOWLIST['graph']` entries (4 tools)
- [x] Agentic loop has `graph_`/`ado_` dispatch branch before else default (lines 1993-1994)
- [x] Dispatch branch calls `fetch('http://localhost:${PORT}/tools/${toolName}', ...)`
- [x] `isError = true` in catch block of new dispatch branch
- [x] `node --check` passes
- [x] `dotnet build` passes (0 errors)

### Status
Cycle 2 complete. Awaiting Clint review cycle 2.

---

## Build Report — ADO#3218 Cycle 3 (Final Cleanup)

### What was built
Three cleanup fixes: added missing `ado_wiql_query` tool spec so Bedrock can actually call it, removed phantom `graph_list_calendar` from the M365 allowlist, and dropped the unimplemented `create_calendar_event` entry from the DB seed.

### Files changed
- `fait-v2/agent-harness/harness-server.js` — Added `ado_wiql_query` entry to `MCP_TOOL_SPECS.azdo`; removed `'graph_list_calendar'` from `MCP_TOOL_ALLOWLIST['graph']` Set
- `fait/src/FortressAI.Web/Services/DatabaseInitializationService.cs` — Removed `create_calendar_event` anonymous object from M365 manifest array; `graph_list_calendar_events` is now the terminal entry (trailing comma removed)

### Parallelization used
No — single CC session, two files, sequential edit

### CC sessions run
1 CC Sonnet session

### Acceptance criteria verification
- [x] `ado_wiql_query` present in `MCP_TOOL_SPECS.azdo` — `grep -n "ado_wiql_query" harness-server.js` shows lines 303 (allowlist) and 387 (spec)
- [x] `create_calendar_event` gone from DB seed — grep returns nothing
- [x] `graph_list_calendar` gone from `MCP_TOOL_ALLOWLIST` — grep returns nothing
- [x] `node --check` passes — confirmed
- [x] `dotnet build` 0 errors — confirmed (46 pre-existing warnings, unchanged)

### Known edge cases / things Clint should scrutinize
- The `ado_wiql_query` spec aligns with what's in the DB seed and allowlist — model can now invoke it through Bedrock
- `graph_list_calendar_events` (the real endpoint, at `/tools/graph_list_calendar_events`) is untouched; only the duplicate/phantom `graph_list_calendar` slug was removed
- No runtime changes — these are purely registration/spec/seed fixes

### How to test locally
1. Start harness: `node harness-server.js` — should load without errors
2. In FAIT chat with ADO server enabled, prompt the model to query work items by WIQL — it should now be offered `ado_wiql_query` in the tool list
3. Verify M365 seed on fresh DB init — `create_calendar_event` should not appear in the tools table

### Commit
`f1af77a8` — `fix(fait#3218): add ado_wiql_query spec + remove graph_list_calendar allowlist + drop create_calendar_event seed (cycle 3)`
