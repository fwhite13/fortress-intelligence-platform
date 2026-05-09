# Review Report — ADO#3109
## G4: MCP Tool Allowlist Enforcement
**Reviewer:** Hawkeye (Clint Barton) — Cycle 1
**Review Tool:** Claude Code CLI (sonnet)
**Verdict: ✅ PASS**

---

## CC Invocation
```
cat pipeline/review-brief-3096-3107-3109.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Summary
Clean, minimal, correctly ordered. Allowlist map and helper function are correct. G4 check fires in the right sequence (after G3/G6 checks). Named routes correctly bypass the catch-all. STITCH_TOOLS overlap is consistent and non-problematic.

---

## Detailed Findings

### `MCP_TOOL_ALLOWLIST` — lines 295–310
```js
const MCP_TOOL_ALLOWLIST = {
    'graph': new Set([...7 tools...]),
    'ado':   new Set([...6 tools...]),
    'stitch': new Set([...10 tools...]),
};
```
- **Graph**: `graph_list_emails`, `graph_list_calendar`, `graph_get_email`, `graph_send_email`, `graph_list_files`, `graph_get_file_content`, `graph_list_calendar_events` — covers all named graph routes ✅
- **ADO**: `ado_list_work_items`, `ado_get_work_item`, `ado_create_work_item`, `ado_update_work_item`, `ado_list_projects`, `ado_wiql_query` — covers all named ADO routes ✅
- **Stitch**: Both `stitch_*` prefixed names (have named routes) AND canonical MCP names (`generate_screen_from_text`, `extract_design_context`, `fetch_screen_code`, `fetch_screen_image`, `list_projects`, `list_screens`, `refine_screen`) ✅

### `BUILTIN_TOOLS` — lines 312–314
```js
const BUILTIN_TOOLS = new Set([
    'list_workspace_files', 'search_memory'
]);
```
Both required builtins present. ✅

### `isToolAllowed()` — lines 316–324
```js
function isToolAllowed(toolName) {
    for (const [, tools] of Object.entries(MCP_TOOL_ALLOWLIST)) {
        if (tools.has(toolName)) return true;
    }
    if (BUILTIN_TOOLS.has(toolName)) return true;
    return false;
}
```
Iterates all server buckets using `Set.has()` (O(1) per lookup), then checks `BUILTIN_TOOLS`. Returns `false` for anything unmatched. ✅

### Check ordering in generic `/tools/:toolName` handler — lines 831–870
Order verified:
1. **ADO#3106 KB-write check** (line 831): `if (reqPluginAgentId && isKbWriteTool(toolName))` → 403 ✅
2. **ADO#3101 server-permission check** (line 839): `if (reqPluginAgentId && reqMcpServerPermissions)` → 403 ✅
3. **G4 allowlist check** (line 866): `if (!isToolAllowed(toolName))` → 403 ✅
4. **Stitch dispatch guard** (line 872): `if (!STITCH_TOOLS.has(toolName))` → 404 ✅

G4 fires **after** both prior checks as required. Specific denials surface first; generic allowlist gate is last. ✅

### Named routes bypass catch-all
Named routes (`/tools/graph_list_emails`, `/tools/ado_get_work_item`, etc.) are registered as specific Express `app.post()` handlers before the generic catch-all. Express gives priority to specific routes; named routes never reach the catch-all and are never subject to the G4 check. Inherently trusted by registration — correct design. ✅

### STITCH_TOOLS / MCP_TOOL_ALLOWLIST.stitch consistency
`MCP_TOOL_ALLOWLIST.stitch` includes all canonical Stitch MCP tool names. The Stitch dispatch guard at line 872 (`if (!STITCH_TOOLS.has(toolName))`) correctly gates dispatch. Since both sets cover the same canonical names, a tool passing G4 will also pass the Stitch guard. No mismatch. ✅

---

## Critical Issues
None.

## Important Issues
None.

## Nitpick Issues
1. The `stitch_*` prefixed names in `MCP_TOOL_ALLOWLIST` (lines 306–308) are unreachable dead entries — those named routes never hit the catch-all. No security impact; harmless padding. Could be removed in a cleanup pass.
2. `Object.entries(MCP_TOOL_ALLOWLIST)` iterates three buckets maximum — trivially small. No performance concern. A flat `Set` would be marginally simpler but restructuring isn't worth it at this scale.

## Observations
None.

---

## Gate Decision
**PASS → advance to DEPLOY**
