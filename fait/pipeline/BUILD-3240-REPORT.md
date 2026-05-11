# Build Report — ADO#3240

## What was built

Two MCP bugs fixed in `fait-v2/agent-harness/harness-server.js`:
1. Brave Web Search was invisible to the model (missing from `MCP_TOOL_SPECS` / `MCP_TOOL_ALLOWLIST` / agentic loop dispatch)
2. MS365 and ADO tools always appeared unauthenticated due to wrong DB table/column lookups

## Files changed

- `fait-v2/agent-harness/harness-server.js` — Four targeted changes:
  - Added `brave: new Set(['web_search'])` to `MCP_TOOL_ALLOWLIST`
  - Added `brave` entry to `MCP_TOOL_SPECS` with `web_search` tool spec (description + inputSchema)
  - Added `POST /tools/web_search` route → proxies to `${FAIT_BASE_URL}/internal/mcp/brave` via JSON-RPC 2.0
  - Extended agentic loop dispatch condition to include `toolUseAccumulator.name === 'web_search'`
  - Fixed `getUserMs365Token` → reads `user_microsoft_tokens.AccessToken` (Blazor's actual table/column, was querying `mcp_user_tokens`/`user_ms365_tokens` — neither exists)
  - Fixed `getUserAdoToken` → calls internal Blazor endpoint `GET /api/internal/devops-pat/{userId}` with `X-Internal-Token` (was querying `user_ado_connections.personal_access_token` — doesn't exist; actual table has DataProtection-encrypted blob Node can't decrypt directly)

- `fait/src/FortressAI.Web/Program.cs` — Added `GET /api/internal/devops-pat/{userId}` endpoint:
  - Anonymous route, gated by `X-Internal-Token` header check (same pattern as WorkspaceController)
  - Calls `DevOpsConnectionService.GetDecryptedPatAsync(userGuid)` and returns `{ pat }` JSON
  - Returns 404 when user has no ADO connection

## Parallelization used
No — sequential: investigation first, then single CC run for both changes.

## CC sessions run
1 (Sonnet)

## Acceptance criteria verification
- [ ] `node --check` passes — ✅ verified by CC before commit
- [ ] Blazor builds — ✅ `dotnet build` succeeded before commit
- [ ] Brave slug in `MCP_TOOL_SPECS` — ✅ `brave` key present
- [ ] `web_search` in `MCP_TOOL_ALLOWLIST` — ✅ `brave: new Set(['web_search'])`
- [ ] `POST /tools/web_search` route exists — ✅ calls `/internal/mcp/brave` JSON-RPC
- [ ] Agentic loop dispatches `web_search` — ✅ condition extended
- [ ] `getUserMs365Token` reads `user_microsoft_tokens.AccessToken` — ✅
- [ ] `getUserAdoToken` calls Blazor internal endpoint — ✅
- [ ] Blazor internal endpoint `/api/internal/devops-pat/{userId}` exists — ✅

## Known edge cases / things Clint should scrutinize

1. **MS365 token expiry**: `getUserMs365Token` returns the raw `AccessToken` from DB without checking `ExpiresAt`. If the token is expired, Graph API calls will get 401s. The old code had the same gap — but worth noting. `MicrosoftTokenService.GetValidAccessTokenAsync` handles refresh; the harness bypasses it. Low priority for now.

2. **Brave `/internal/mcp/brave` loopback restriction**: The `BraveSearchMcpAdapter` rejects non-loopback callers. Since the harness calls `localhost:8080`, this is fine in ECS Fargate (same task). Fine.

3. **ADO PAT endpoint returns 404 silently**: If a user hasn't connected ADO, `getUserAdoToken` returns `null` and tool calls fail silently. Same behavior as before — just through a different path.

4. **Single commit covers both repos** (monorepo): `fait-v2/` and `fait/` are both under the same git root. Commit `7c276084` covers both files.

## How to test locally

```bash
# 1. Start Blazor app (port 8080)
cd ~/projects/fip/fait && dotnet run --project src/FortressAI.Web

# 2. Start harness
cd ~/projects/fip/fait-v2/agent-harness && node harness-server.js

# 3. Test Brave (with a user who has brave enabled)
curl -X POST http://localhost:3000/tools/web_search \
  -H 'Content-Type: application/json' \
  -d '{"query": "current weather Atlanta"}'

# 4. Test MS365 token lookup (verify it reads user_microsoft_tokens)
# Check harness logs for "[harness] getUserMs365Token" — should not see "error" for connected users

# 5. Test ADO PAT endpoint
curl http://localhost:8080/api/internal/devops-pat/<userId> \
  -H "X-Internal-Token: <INTERNAL_API_TOKEN>"
```

## Commit
`7c276084` — `fix(fait#3240): brave web_search in toolConfig + MCP auth token lookup fix`

---

# Build Report — ADO#3240 — Cycle 3

**Commit:** `3159ee3b`
**Date:** 2026-05-10
**Branch:** main

## What was built

Added `/internal/mcp/brave` minimal API endpoint to `Program.cs` — token-authenticated via `X-Internal-Token` / `INTERNAL_API_TOKEN`. This endpoint coexists with the existing `BraveSearchMcpAdapter` controller but takes precedence in ASP.NET 8's routing pipeline (minimal API registered before `MapControllers()`). The new endpoint is what the harness targets (X-Internal-Token auth, not loopback IP).

Also added `bool IsInternalAuthorized(HttpContext, IConfiguration)` local function (consistent with other internal endpoints in the file).

## Files changed

### `src/FortressAI.Web/Program.cs`

- **Added `using System.Text.Json;`** — was missing, required for `JsonDocument.Parse` in the new endpoint
- **Added `app.MapPost("/internal/mcp/brave", ...)` block** — before `app.MapControllers()`. Validates `X-Internal-Token`, parses MCP JSON-RPC envelope, dispatches `web_search` to `BraveSearchClient.SearchAsync()`, returns `{ content: [{ type: "text", text: <formatted results> }] }`
- **Added `bool IsInternalAuthorized(HttpContext, IConfiguration)` local function** — after `app.Run()`, consistent with local function pattern in Program.cs

## Parallelization used

No — single file, single CC run.

## CC sessions run

1 (Sonnet). Build passed first pass, 0 errors, 0 warnings.

## Acceptance criteria verification

- [x] `dotnet build` — **PASS, 0 errors, 0 warnings**
- [x] `/internal/mcp/brave` MapPost endpoint added to Program.cs
- [x] `IsInternalAuthorized` helper defined and used
- [x] Response format: `{ content: [{ type: "text", text: <formatted> }] }` — matches harness `result.content[0].text` extraction
- [x] Uses `BraveSearchClient` directly (correct — `IBraveSearchService` doesn't exist in this codebase)
- [x] Commit message: `fix(fait#3240): add /internal/mcp/brave endpoint to Blazor (cycle 3)`

## Known edge cases / things Clint should scrutinize

- **Routing precedence:** The `BraveSearchMcpAdapter` controller also handles `POST /internal/mcp/brave` with loopback IP check. The new minimal API takes precedence (registered before `MapControllers()`). The controller is effectively shadowed for this route. Consider removing or disabling `BraveSearchMcpAdapter` to avoid confusion — but that's a cleanup task, not a blocker.
- **`IsInternalAuthorized` placement:** Defined as a local function after `app.Run()`. C# top-level statement local functions defined after the "main" code are valid and visible throughout the file scope.

## How to test locally

1. Run FAIT locally with `INTERNAL_API_TOKEN` set
2. `curl -X POST http://localhost:5000/internal/mcp/brave -H "X-Internal-Token: <token>" -H "Content-Type: application/json" -d '{"method":"tools/call","params":{"name":"web_search","arguments":{"query":"test","count":3}}}'`
3. Expect: `{ "content": [{ "type": "text", "text": "1. ..." }] }`

## Status

🟡 **Awaiting Clint review — DO NOT CLOSE**
