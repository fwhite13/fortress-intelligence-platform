# Build Report — ADO#3248

**Commit:** `ec8ed7ff`
**Date:** 2026-05-11
**Branch:** main

## What was built

Fixed Brave web_search being blocked by Cloudflare in the agent harness. The `/tools/web_search` handler was constructing the Brave MCP endpoint URL using `FAIT_BASE_URL` (the public Cloudflare-fronted URL). Since the Brave endpoint lives in the same Blazor container, routing through the public URL causes Cloudflare to intercept and block the request.

Fix: replaced `FAIT_BASE_URL` with a localhost URL using `BLAZOR_INTERNAL_PORT` (defaulting to `8080`), bypassing Cloudflare entirely.

## Files changed

- `fait-v2/agent-harness/harness-server.js` (~line 1181)
  - Removed: `const blazorBase = FAIT_BASE_URL;`
  - Added: `const blazorPort = process.env.BLAZOR_INTERNAL_PORT || '8080';` and `const braveLocalUrl = \`http://localhost:${blazorPort}/internal/mcp/brave\`;`
  - Changed fetch target from `` `${blazorBase}/internal/mcp/brave` `` to `braveLocalUrl`

## Build verification

- `node --check harness-server.js` → **syntax OK**

## Acceptance criteria

- [x] Brave MCP fetch uses `http://localhost:${port}/internal/mcp/brave` instead of public URL
- [x] `BLAZOR_INTERNAL_PORT` env var respected with `8080` default
- [x] All fetch options (headers, body, method) unchanged
- [x] `node --check` — syntax OK

## Notes

- In ECS Fargate, harness and Blazor run in the same task (same network namespace), so `localhost:8080` is correct.
- `BLAZOR_INTERNAL_PORT` can be set to override if the Blazor container port changes.
