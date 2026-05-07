# BUILD Brief: ADO#2899 — fip-mcp routing refactor: split monolithic McpServer into path-routed servers

**ADO WI:** #2899 (Fortress project)
**Repo:** `/home/fredw/projects/fip`
**Service:** `services/fip-mcp/`
**Priority:** FIRST in Sprint 4 — foundational, blocks all future tool group additions

---

## MANDATORY: Use Claude Code CLI

Write a brief file, then execute:
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/services/fip-mcp/pipeline/ADO2899-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Do NOT edit files directly. CC Sonnet default.

---

## Context

`fip-mcp` currently has a monolithic `createMcpServer()` factory in `src/server.js` that registers ALL tools (forge-kb, MS365, ADO, web search) on a single McpServer at `/mcp`. The forge-kb split was already done (a separate `createForgeKbServer()` factory exists in `src/servers/forge-kb-server.js` and routes at `/mcp/forge-kb`).

This WI splits the remaining tool groups — MS365, ADO, and web search — into separate per-path McpServer factories, following the same pattern as forge-kb.

**Current state (verified from source):**
- `/mcp` — monolith (all tools)
- `/mcp/forge-kb` — ✅ already path-routed (`src/servers/forge-kb-server.js`)
- `/mcp/ms365` — ❌ not yet
- `/mcp/ado` — ❌ not yet
- `/mcp/web` — ❌ not yet

---

## Target Architecture

```
/mcp             → monolith (PRESERVED for backward compatibility — do NOT remove)
/mcp/forge-kb    → forge-kb server (already done, no change)
/mcp/ms365       → MS365 MCP server (email, calendar, Teams — 7 tools)
/mcp/ado         → ADO MCP server (7 tools: list_projects, list_work_items, get_work_item, create_work_item, update_work_item, add_comment, list_iterations)
/mcp/web         → Web search MCP server (1 tool: web_search)
```

**Critical:** The existing `/mcp` monolith endpoint MUST be preserved and functional after this refactor. Do not remove it. Clients using `/mcp` must continue to work. This is purely additive routing.

---

## Implementation

### 1. Create three new server factories in `src/servers/`

Following the exact same pattern as `src/servers/forge-kb-server.js`:

**`src/servers/ms365-server.js`**
- Export `createMs365Server(user, rawToken)`
- Register all 7 MS365 tools: `list_emails`, `get_email`, `send_email`, `list_calendar_events`, `create_calendar_event`, `list_teams_chats`, `send_teams_message`
- Import from existing `src/tools/ms365/*.js` files — NO changes to tool implementations
- Use `createGraphClient(rawToken)` exactly as the monolith does
- Use `handleGraphError` for error handling

**`src/servers/ado-server.js`**
- Export `createAdoServer(user, rawToken)`
- Register all 7 ADO tools: `list_ado_projects`, `list_ado_work_items`, `get_ado_work_item`, `create_ado_work_item`, `update_ado_work_item`, `add_ado_comment`, `list_ado_iterations`
- Import from existing `src/tools/ado/*.js` files — NO changes to tool implementations
- Use `isPATConfigured()` check exactly as the monolith does

**`src/servers/web-server.js`**
- Export `createWebServer(user, rawToken)`
- Register 1 tool: `web_search`
- Import from existing `src/tools/search/web_search.js` — NO changes to tool implementation
- Use `isAPIKeyConfigured()` check exactly as the monolith does

### 2. Wire the new servers into `src/server.js`

Add routes for each new server following the EXACT same pattern as the forge-kb routes. Each server needs:
- `GET /mcp/{group}/health` — public, no auth
- `POST /mcp/{group}` — auth + StreamableHTTP transport
- `GET /mcp/{group}/sse` — auth + SSE transport (with its own session map)

Example pattern (replicate for ms365, ado, web):
```javascript
// --- ms365 path-routed server ---
const ms365SseSessions = new Map();

app.get('/mcp/ms365/health', (_req, res) => {
  res.json({ status: 'ok', server: 'ms365', version: VERSION });
});

app.post('/mcp/ms365', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createMs365Server(user, rawToken);
    const transport = new StreamableHTTPServerTransport({ sessionIdGenerator: undefined });
    await server.connect(transport);
    await transport.handleRequest(req, res, req.body);
    await server.close();
  } catch (err) {
    console.error('[fip-mcp] POST /mcp/ms365 error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
  }
});

app.get('/mcp/ms365/sse', authMiddleware, async (req, res) => {
  const user = req.user;
  const rawToken = req.user.rawToken;
  try {
    const server = createMs365Server(user, rawToken);
    const transport = new SSEServerTransport('/mcp/ms365/sse', res);
    const sessionId = transport.sessionId;
    if (sessionId) ms365SseSessions.set(sessionId, transport);
    transport.onclose = () => { if (sessionId) ms365SseSessions.delete(sessionId); };
    await server.connect(transport);
  } catch (err) {
    console.error('[fip-mcp] GET /mcp/ms365/sse error:', err);
    if (!res.headersSent) res.status(500).json({ error: 'Internal server error' });
  }
});
```

Repeat identically for `/mcp/ado` (using `createAdoServer`) and `/mcp/web` (using `createWebServer`).

### 3. Update startup log

In the `app.listen` callback, add lines announcing the new routes:
```javascript
console.log(`[fip-mcp] Path routes: /mcp (monolith), /mcp/forge-kb, /mcp/ms365, /mcp/ado, /mcp/web`);
```

---

## Scope Constraints

- **DO NOT** modify any file in `src/tools/` — tool implementations are unchanged
- **DO NOT** remove or modify the `/mcp` monolith routes
- **DO NOT** modify `src/servers/forge-kb-server.js`
- **DO NOT** change `src/auth.js`
- This is a routing/wiring refactor only — pure addition of new routes

---

## Acceptance Criteria

- [ ] `src/servers/ms365-server.js` created with `createMs365Server(user, rawToken)` exporting all 7 MS365 tools
- [ ] `src/servers/ado-server.js` created with `createAdoServer(user, rawToken)` exporting all 7 ADO tools
- [ ] `src/servers/web-server.js` created with `createWebServer(user, rawToken)` exporting `web_search` tool
- [ ] `src/server.js` routes `/mcp/ms365`, `/mcp/ado`, `/mcp/web` (POST + GET /sse + GET /health for each)
- [ ] `/mcp` monolith preserved and unchanged
- [ ] `/mcp/forge-kb` preserved and unchanged
- [ ] No tool implementation files modified
- [ ] `npm test` passes (if tests exist) or `node --check src/server.js` runs clean

---

## ADO Tracking (MANDATORY)

**You must add a comment to ADO#2899 after completing the build:**
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2899,
  "text": "**[Tony Stark — BUILD cycle 1]**\nCommit {hash}: {summary}. Build: SUCCEEDED. Files: src/servers/ms365-server.js, src/servers/ado-server.js, src/servers/web-server.js, src/server.js (routes added)."
}'
```

---

## Deliverables

1. **Three new files**: `src/servers/ms365-server.js`, `src/servers/ado-server.js`, `src/servers/web-server.js`
2. **Updated**: `src/server.js` with new routes
3. **Build Report** at `/home/fredw/projects/fip/services/fip-mcp/pipeline/ADO2899-BUILD-REPORT.md`

### Build Report Format
```markdown
# Build Report: ADO#2899

## Status: SUCCEEDED

## Commits
- {hash}: {message}

## Files Modified
- src/server.js — routes added for /mcp/ms365, /mcp/ado, /mcp/web

## Files Created
- src/servers/ms365-server.js
- src/servers/ado-server.js
- src/servers/web-server.js

## Verification
- [ ] `node --check src/server.js` clean
- [ ] All 3 server factories export correctly
- [ ] /mcp monolith unchanged
- [ ] /mcp/forge-kb unchanged

## CC Invocation Used
[The exact cat brief | claude command used]
```
