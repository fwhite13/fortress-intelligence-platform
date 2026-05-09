## Build Report — ADO#3109

### What was built
G4: MCP Tool Allowlist Enforcement at the harness level. The generic `/tools/:toolName` catch-all route now rejects any tool not present in `MCP_TOOL_ALLOWLIST` with HTTP 403 before dispatching to the Stitch handler.

### Files changed
- `agent-harness/harness-server.js` — Added `MCP_TOOL_ALLOWLIST` map (graph, ado, stitch entries), `BUILTIN_TOOLS` Set, `isToolAllowed()` helper, and G4 403-check in the generic `/tools/:toolName` route

### Parallelization used
No — single-file change, serial.

### CC sessions run
1 CC Sonnet session

### Acceptance criteria verification
- [x] `MCP_TOOL_ALLOWLIST` map present with graph, ado, stitch entries — **verified via grep**
- [x] `isToolAllowed()` function present — **verified via grep**
- [x] Generic `/tools/:toolName` handler rejects unlisted tools with 403 — **verified at line ~867**
- [x] Known tools (graph_list_emails, ado_get_work_item, etc.) still pass through — check is `if (!isToolAllowed)` so allowlisted tools fall through to named handlers naturally
- [x] `node --check` passes — **SYNTAX OK**

### Commit
`3741e1bf` — included in the feat(fait#3101,fait#3106) commit (CC bundled related G-series harness changes)

### Known edge cases / things Clint should scrutinize
- The G4 check fires AFTER the ADO#3101 server-permission check and ADO#3106 KB-write check — this is intentional (specific denials surface first, then the generic allowlist gate)
- Named routes (e.g. `app.post('/tools/graph_list_emails', ...)`) are registered separately and always reachable; `isToolAllowed` is only in the generic catch-all
- The stitch allowlist in `MCP_TOOL_ALLOWLIST` mirrors `STITCH_TOOLS` — both remain for clarity; the Stitch-named check (`STITCH_TOOLS.has`) is still the final dispatch guard inside the generic handler

### How to test locally
```bash
# Unknown tool → 403
curl -X POST http://localhost:4000/tools/evil_tool -H 'Content-Type: application/json' -d '{}'
# → {"error":"Tool 'evil_tool' is not in the allowed tool list"}

# Known tool → proceeds to Stitch handler (which may 500 if Stitch not running, but not 403)
curl -X POST http://localhost:4000/tools/generate_screen_from_text -H 'Content-Type: application/json' -d '{}'
```
