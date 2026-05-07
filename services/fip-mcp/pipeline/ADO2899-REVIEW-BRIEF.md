# REVIEW Brief: ADO#2899 — fip-mcp routing refactor

**ADO WI:** #2899 (Fortress project)
**Review Cycle:** 1
**Build Commit:** `8f247b9d668141a108b2dd5ac94cd1bd6b9cdd92`

---

## MANDATORY: Use Claude Code CLI

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/services/fip-mcp/pipeline/ADO2899-REVIEW-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

Working directory: `/home/fredw/projects/fip/services/fip-mcp/`

---

## What Changed

Tony created 3 new server factory files and updated `src/server.js` to add path-routed endpoints:

**Created:**
- `src/servers/ms365-server.js` — `createMs365Server(user, rawToken)`, 7 MS365 tools
- `src/servers/ado-server.js` — `createAdoServer(user, rawToken)`, 7 ADO tools
- `src/servers/web-server.js` — `createWebServer(user, rawToken)`, `web_search` tool

**Modified:**
- `src/server.js` — 3 new imports, 9 new routes (/mcp/ms365, /mcp/ado, /mcp/web: POST + GET /sse + GET /health each), startup log updated

---

## Review Checklist

Use CC to read each file. Verify:

### Architecture / Routing
1. Each new server factory (`createMs365Server`, `createAdoServer`, `createWebServer`) properly instantiates a **new** `McpServer` instance — not sharing the same instance across requests
2. Each path has correct SSE session map (independent `Map()` per path, not shared)
3. SSE transport path strings match the route paths (e.g. `new SSEServerTransport('/mcp/ms365/sse', res)`)
4. `server.close()` called after `transport.handleRequest()` for StreamableHTTP (POST) routes — prevents memory leaks
5. `/mcp` monolith is genuinely unchanged — diff `src/server.js` to confirm only additions
6. `/mcp/forge-kb` untouched — `src/servers/forge-kb-server.js` not modified

### Tool Registration
7. All 7 MS365 tools present in `ms365-server.js` with correct Zod schemas matching monolith
8. All 7 ADO tools present in `ado-server.js` — `isPATConfigured()` check on each tool (same as monolith)
9. `web_search` tool present in `web-server.js` — `isAPIKeyConfigured()` check present
10. No tool schemas changed from monolith — this is a routing-only refactor

### Auth & Error Handling
11. `authMiddleware` applied to all POST and GET /sse routes (not to /health)
12. Health routes are public (no authMiddleware) — consistent with `/mcp/forge-kb/health` pattern
13. Error handling pattern (`try/catch`, check `!res.headersSent` before 500) consistent with forge-kb routes
14. `rawToken` properly passed from `req.user.rawToken` to MS365/ADO server factories

### ESM Compliance
15. All new files use ESM (`import`/`export`, no `require`) — service is ESM throughout (Node 22)
16. No CommonJS patterns introduced

### Code Quality
17. No dead code, unused imports, or stray debug logs
18. Naming consistent with existing codebase patterns

---

## ADO Tracking (MANDATORY)

After review complete:
```bash
mcporter call devops.add_comment --args '{
  "project": "Fortress",
  "id": 2899,
  "text": "**[Hawkeye — REVIEW cycle 1]**\nCode review {PASS|NEEDS-CHANGES}. Cycles: 1. {summary of findings or \"No issues.\"}"
}'
```

---

## Deliverables

1. **Review Report** at `/home/fredw/projects/fip/services/fip-mcp/pipeline/ADO2899-REVIEW-REPORT-C1.md`
2. **Verdict:** PASS / NEEDS-CHANGES / FAIL
3. If NEEDS-CHANGES: specific file + line + exact fix required
