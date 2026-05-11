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
