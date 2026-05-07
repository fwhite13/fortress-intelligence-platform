# Build Report — ADO#2856

**Task:** FAIT v2 Web Search MCP Tool  
**Commit:** `0cde72c`  
**Branch:** `main`  
**Date:** 2026-05-07

---

## What was built

Added a `search/` tool group to `fip-mcp` with one tool: `web_search`, wrapping the Brave Search API. Follows the same `ado/` and `ms365/` tool group patterns with service-level API key auth and graceful missing-key handling.

---

## Files changed

- `src/tools/search/search-client.js` (**NEW**) — Brave Search API client. `braveWebSearch()` builds query params, sends `GET /web/search` with `X-Subscription-Token` header, returns raw Brave JSON. `isAPIKeyConfigured()` checks env var presence. Count capped at 20 (Brave API limit).
- `src/tools/search/web_search.js` (**NEW**) — `webSearch(user, params)` — calls `braveWebSearch()`, maps results to `{ title, url, description, age }` objects. Safe null coalescing on `results?.web?.results`.
- `src/server.js` (**MODIFIED**) — Added imports for `webSearch` and `isAPIKeyConfigured` after ADO imports. Registered `web_search` tool in `createMcpServer()` with `query` / `count` / `country` params.

---

## Parallelization used

No — single sequential CC session (3 files, straightforward implementation).

---

## CC sessions run

1 CC session (Sonnet). Ran cleanly on first pass, no retry needed.

---

## Acceptance criteria verification

- [x] `web_search` tool registered in `server.js` — verified via grep at line 523
- [x] Brave Search API called at `https://api.search.brave.com/res/v1/web/search` — in `search-client.js`
- [x] Auth via `X-Subscription-Token: {BRAVE_API_KEY}` header — in `search-client.js`
- [x] Missing `BRAVE_API_KEY` → graceful `isError: true` response, no crash — `isAPIKeyConfigured()` guard in tool handler
- [x] Returns array of `{ title, url, description, age }` objects — `web_search.js` map
- [x] Count capped at 20 — `Math.min(count, 20)` in `search-client.js`
- [x] No API key logged/returned in errors — error messages only contain status codes and `statusText`
- [x] ESM throughout — no `require()` — verified

---

## Known edge cases / things Clint should scrutinize

- `BRAVE_API_KEY` is read at module load time (`const BRAVE_API_KEY = process.env.BRAVE_API_KEY`), not per-call. This matches the ADO PAT pattern. No concern in ECS (env vars are static), but worth noting.
- `isAPIKeyConfigured()` will return `false` if key is set to empty string `""` — intentional (same as ADO `isPATConfigured()`).
- Brave returns `age` as a relative string (e.g. `"2 days ago"`) or `undefined` — mapped to `null` when absent.

---

## How to test locally

```bash
# In fip-mcp directory:
BRAVE_API_KEY=<your-test-key> PORT=3000 node src/server.js

# Then POST to /mcp with a valid JWT and tool call:
# { "method": "tools/call", "params": { "name": "web_search", "arguments": { "query": "test search" } } }

# Missing key test — start without BRAVE_API_KEY:
PORT=3000 node src/server.js
# web_search should return isError: true with "Web search not configured: BRAVE_API_KEY env var missing"
```

---

## Env var required for deploy

`BRAVE_API_KEY` must be added to the `fip-mcp` ECS task definition. **Rhodey action required.**

---

## Build Cycle 2 — Polish Fixes (Clint Review)

**Commit:** `4c74494066ea8386b701ba8235dc15cd8bbb87c5`  
**Date:** 2026-05-07

### Changes applied

| ID | File | Fix |
|----|------|-----|
| I1 | `src/tools/search/search-client.js` | Added `AbortController` + 10s fetch timeout with `finally { clearTimeout(timeout) }` |
| I2 | `src/tools/search/search-client.js` | Removed module-load-time `const BRAVE_API_KEY`; replaced with lazy `getAPIKey()` function — reads env var at call time, fixes local dev `.env` issue |
| N1 | `src/server.js` | `z.number()` → `z.number().int()` for `count` param |
| N2 | `src/server.js` | `z.string()` → `z.string().min(1)` for `query` param |

### CC sessions
1 CC session (Sonnet). Clean pass, no retry.
