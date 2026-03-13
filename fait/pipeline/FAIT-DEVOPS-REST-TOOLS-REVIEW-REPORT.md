# Code Review Report: FAIT Azure DevOps REST Tools

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `c242bbb`
**Review Cycle:** 1 of 2
**Date:** 2026-03-12
**Verdict:** ✅ PASS

---

## Summary

All 32 checklist items verified against the actual code. All 5 focus items confirmed clean. No blocking issues found. The implementation follows the BraveSearch pattern faithfully with correct auth, two-step WIQL, loopback enforcement, gated tool injection, and idempotent seeding. One minor deviation noted (item #8: `$expand=fields` vs `$expand=all`) — acceptable, does not affect correctness.

---

## Checklist Results

### DevOpsToolService (items 1–12)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | All 6 methods use `CreateClient("azure-devops")` | ✅ PASS | All 6 methods + helper `GetFirstProjectNameAsync` use `_httpClientFactory.CreateClient("azure-devops")` |
| 2 | Auth header: `Basic base64(":{PAT}")` colon-prefixed | ✅ PASS | `BuildRequest` does `$":{pat}"` before encoding — colon prefix confirmed |
| 3 | PAT retrieved via `DevOpsConnectionService.GetDecryptedPatAsync(userId)` | ✅ PASS | `GetCredentialsAsync` calls `_devOpsConn.GetDecryptedPatAsync(userId)` — not hardcoded |
| 4 | Returns `null` when not connected or PAT is null | ✅ PASS | `GetCredentialsAsync` returns null tuple on empty orgUrl or null PAT; all 6 methods check and return null |
| 5 | All methods wrapped in try/catch | ✅ PASS | Every public method has a top-level try/catch logging warning and returning null |
| 6 | PAT never appears in log output | ✅ PASS | Only `userId` and non-secret params logged; PAT never referenced in any `_logger` call |
| 7 | `QueryWorkItemsAsync` uses WIQL endpoint: `POST {org}/{project}/_apis/wit/wiql?api-version=7.1` with `{"query":"..."}` | ✅ PASS | URL and body construction confirmed correct |
| 8 | Two-step WIQL: IDs from WIQL response, then details via `GET /_apis/wit/workitems?ids=...` | ✅ PASS | Two-step pattern implemented. Note: detail call uses `$expand=fields` (not `$expand=all`). Both are valid; `fields` is sufficient for `FormatWorkItem`. Acceptable. |
| 9 | `TriggerPipelineRunAsync` uses `POST {org}/{project}/_apis/pipelines/{id}/runs?api-version=7.1` | ✅ PASS | URL confirmed correct |
| 10 | `ListProjectsAsync` handles empty/null org URL gracefully | ✅ PASS | `GetCredentialsAsync` returns null on empty org URL before any HTTP call |
| 11 | Named HttpClient `"azure-devops"` registered in Program.cs with 30s timeout | ✅ PASS | `builder.Services.AddHttpClient("azure-devops", client => { client.Timeout = TimeSpan.FromSeconds(30); })` confirmed in Program.cs |
| 12 | `DevOpsToolService` registered as scoped | ✅ PASS | `builder.Services.AddScoped<DevOpsToolService>()` confirmed in Program.cs |

---

### DevOpsMcpAdapter (items 13–18)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 13 | Loopback-only check matches BraveSearchMcpAdapter pattern | ✅ PASS | Identical pattern: `IsIPv4MappedToIPv6` → `MapToIPv4()` → `IPAddress.IsLoopback()` |
| 14 | `X-API-Key` header carries userId | ✅ PASS | `HttpContext.Request.Headers["X-API-Key"]` parsed as GUID |
| 15 | userId from header used to call service methods — no hardcoded IDs | ✅ PASS | All 6 tool dispatches pass the extracted `userId` |
| 16 | Returns 400 on missing/invalid tool name | ✅ PASS | Unknown tool falls to `return BadRequest(...)` with code `-32601` |
| 17 | Returns 403 on non-loopback requests | ✅ PASS | `return StatusCode(403, ...)` on non-loopback |
| 18 | No PAT in adapter | ✅ PASS | Adapter only receives userId, passes it to `DevOpsToolService` — no PAT handling |

---

### McpToolService Integration (items 19–23)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 19 | `devops_pat` auth type gates DevOps tools on `IsConnectedAsync(userId)` | ✅ PASS | `var devOpsConnected = await _devOpsConn.IsConnectedAsync(userId)` evaluated before iterating servers |
| 20 | When not connected: DevOps tools absent from tool list | ✅ PASS | `if (server.AuthType == DevOpsSlug.AuthType && !devOpsConnected) continue;` — tools skipped entirely, not just silently failing |
| 21 | `userId` extracted from ClaimsPrincipal and passed as `X-API-Key` | ✅ PASS | `userId` is passed as a parameter to `ExecuteToolAsync` from `ChatView.razor` (`Session.UserId`), then set as `accessToken = userId.ToString()` which flows to `McpHttpTransport` as `apiKey:` (maps to `X-API-Key` header) |
| 22 | No regression to BraveSearch, M365, or other tool wiring | ✅ PASS | DevOps path is gated behind `server.AuthType == DevOpsSlug.AuthType` checks; existing `api_key` / bearer paths untouched |
| 23 | DevOps server seeded with `INSERT ... ON DUPLICATE KEY UPDATE` | ✅ PASS | Both Brave and DevOps seed statements use `ON DUPLICATE KEY UPDATE` |

---

### DB Seed (items 24–26)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 24 | Server ID `00000000-0000-0000-0000-000000000002` consistent between seed and service reference | ✅ PASS | Seed uses `devOpsId = "00000000-0000-0000-0000-000000000002"`. McpToolService identifies DevOps server by `AuthType == DevOpsSlug.AuthType` (not by hardcoded ID), which is the correct approach — slug/auth-type lookup is safer than ID coupling |
| 25 | Seed uses `devops_pat` as AuthType | ✅ PASS | `auth_type` column in INSERT is `'devops_pat'`; matches `DevOpsSlug.AuthType` constant |
| 26 | Seed idempotent on startup | ✅ PASS | `ON DUPLICATE KEY UPDATE endpoint_url = VALUES(endpoint_url), tool_manifest = VALUES(tool_manifest), updated_at = NOW(6)` — safe to re-run |

---

### ChatView / System Prompt (items 27–29)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 27 | DevOps tool guidance only injected when devops tools in active tool list | ✅ PASS | `var hasDevOpsTool = availableTools.Any(t => t.FullName.StartsWith("devops__", ...))` guard present; guidance only injected when true |
| 28 | Default WIQL template in system prompt is syntactically valid | ✅ PASS | `SELECT [System.Id],[System.Title],[System.State],[System.AssignedTo] FROM WorkItems WHERE [System.AssignedTo]=@Me AND [System.State]<>'Closed' AND [System.State]<>'Resolved' ORDER BY [System.ChangedDate] DESC` — valid WIQL |
| 29 | System prompt addition doesn't break non-DevOps users | ✅ PASS | `hasDevOpsTool` guard means the block is entirely skipped for users without DevOps tools |

---

### Security (items 30–32)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 30 | `/internal/mcp/devops` is loopback-restricted | ✅ PASS | Loopback check is the first thing executed in `HandleMcpRequest`; returns 403 before any processing |
| 31 | userId from `X-API-Key` validated as non-empty GUID before DB lookup | ✅ PASS | `Guid.TryParse(userIdStr, out var userId)` — returns 401 on parse failure; empty string also fails `TryParse` |
| 32 | No PAT in HTTP response bodies or log lines | ✅ PASS | Adapter never receives or returns PAT; service logs only userId, project names, and work item IDs |

---

## Focus Item Results

| Focus | Item | Finding |
|-------|------|---------|
| **#2** | Basic auth colon prefix | ✅ **CLEAN** — `$":{pat}"` confirmed in `BuildRequest` helper |
| **#8** | Two-step WIQL | ✅ **CLEAN** — Step 1: `POST wiql` → IDs; Step 2: `GET workitems?ids=...` |
| **#13** | Loopback check pattern | ✅ **CLEAN** — IPv6-mapped IPv4 unwrapping present, identical to BraveSearchMcpAdapter |
| **#20** | DevOps tools absent when not connected | ✅ **CLEAN** — Hard `continue` removes tools from list entirely |
| **#22** | BraveSearch regression | ✅ **CLEAN** — No changes to BraveSearch wiring; DevOps uses parallel code path |

---

## Minor Observations (Non-Blocking)

1. **`$expand=fields` vs `$expand=all` (item #8):** The checklist specifies `$expand=all`, but the implementation uses `$expand=fields` for the batch work item detail call. `$expand=all` also includes `relations`, `links`, and `html` which add significant payload overhead. `$expand=fields` is the better production choice. The checklist is slightly imprecise here; implementation is correct.

2. **`GetWorkItemAsync` uses `$expand=all`** (single item call) while `QueryWorkItemsAsync` batch call uses `$expand=fields`. This minor inconsistency is acceptable — the single-item view is expected to be more detailed.

3. **`DevOpsSlug` internal static class** is a clean pattern for preventing string duplication between adapter, service, and seed. Well done.

4. **`requires_user_auth = 0` with `IsConnectedAsync` guard:** The DevOps server is seeded as `requires_user_auth = 0` (auto-available) but filtered in application code. This is intentional (documented in seed comment) and consistent with how the PAT connection table works. No issue.

---

## Verdict

**✅ PASS — Ready to advance to SECURITY stage.**

All 32 checklist items confirmed. All 5 focus items clean. No critical or important issues. Implementation is solid, follows established patterns, and introduces no regressions.
