# FAIT M365 MCP Adapter — Code Review Report

**Commit:** `64aebd1`
**Reviewer:** Hawkeye (Clint Barton)
**Review Cycle:** 1 of 2
**Date:** 2026-03-13

---

## Verdict: ✅ PASS

All 30 checklist items confirmed. Focus items #11, #17, #24, and #25 reviewed — all clear.

---

## Checklist Results

### Seed — `DatabaseInitializationService.cs` (items 1–6)

| # | Item | Result |
|---|------|--------|
| 1 | Server ID `00000000-0000-0000-0000-000000000003` consistent across seed + McpToolService routing | ✅ **PASS** — ID hardcoded in `var m365Id = "00000000-0000-0000-0000-000000000003"`. McpToolService routes by `slug == "m365"` (not by ID), which matches the seeded slug. No ID mismatch. |
| 2 | `auth_type = 'm365_token'` in INSERT | ✅ **PASS** — `'m365_token'` in INSERT, confirmed in the SQL values list. |
| 3 | `requires_user_auth = 0` in INSERT | ✅ **PASS** — `0` passed as value, seed comment explains the gate is applied in `GetConversationToolsAsync` instead. |
| 4 | `ON DUPLICATE KEY UPDATE` includes `auth_type`, `requires_user_auth`, `endpoint_url`, `tool_manifest`, `updated_at` | ✅ **PASS** — all 5 columns present in the `ON DUPLICATE KEY UPDATE` clause. Matches the DevOps seed pattern. |
| 5 | 5 tools in manifest: `list_emails`, `get_email`, `send_email`, `list_calendar_events`, `create_calendar_event` | ✅ **PASS** — all 5 tools present in `m365Manifest` serialization. |
| 6 | Seed inside try/catch (non-fatal) | ✅ **PASS** — wrapped in `try { ... } catch (Exception ex) { _logger.LogWarning(...) }`, identical to DevOps seed pattern. |

---

### McpToolService — `McpToolService.cs` (items 7–14)

| # | Item | Result |
|---|------|--------|
| 7 | `M365Slug.Slug = "m365"` and `M365Slug.AuthType = "m365_token"` defined | ✅ **PASS** — `internal static class M365Slug` defined at top of file with both constants. |
| 8 | `GetConversationToolsAsync`: gate checks `db.UserMicrosoftTokens.AnyAsync(t => t.UserId == userId && t.AccessToken != null)` | ✅ **PASS** — exact predicate present. |
| 9 | Gate placed AFTER DevOps gate, BEFORE tool list population | ✅ **PASS** — DevOps `continue` block is first, M365 block is second, tool list population (`ToolManifestJson` deserialization loop) follows both. |
| 10 | `ExecuteToolAsync`: `m365_token` branch sets `accessToken = userId.ToString()` | ✅ **PASS** — `else if (server.AuthType == M365Slug.AuthType) accessToken = userId.ToString()` present. |
| 11 | Transport routing: `m365_token` included in `api_key` path | ✅ **PASS** — Focus item. The condition `server.AuthType == "api_key" \|\| server.AuthType == DevOpsSlug.AuthType \|\| server.AuthType == M365Slug.AuthType` gates the `apiKey: accessToken` path. `m365_token` is correctly NOT in the `bearerToken` branch. |
| 12 | `GetActiveServersForUserAsync`: M365 handled with `continue` (not falling through to RequiresUserAuth generic path) | ✅ **PASS** — M365 block does `if (m365Connected) result.Add(server); continue;` — the `continue` is present and prevents fallthrough. |
| 13 | No regression to DevOps gate or BraveSearch paths | ✅ **PASS** — DevOps gate structure unchanged. Brave slug is `api_key` auth type (not a named constant), unaffected. |
| 14 | `db` reused from existing scope — no second `CreateDbContextAsync` call for M365 check | ✅ **PASS** — M365 check in `GetConversationToolsAsync` uses the `db` context from the outer `await using var db = await _dbFactory.CreateDbContextAsync()` at the top. Same in `GetActiveServersForUserAsync`. |

---

### M365McpAdapter — `Services/M365McpAdapter.cs` (items 15–25)

| # | Item | Result |
|---|------|--------|
| 15 | File location and ASP.NET registration | ✅ **PASS** — Located in `Services/`. Uses `[ApiController]` attribute; `app.MapControllers()` in `Program.cs` registers it. Compiles as part of the web project. |
| 16 | Route: `[Route("internal/mcp/m365")]` | ⚠️ **NOTE** — Route is not on the class via `[Route(...)]`; instead, the method uses `[HttpPost("/internal/mcp/m365")]` with an absolute path. Functionally equivalent — ASP.NET absolute paths on `[HttpPost]` override any class-level route prefix. **No functional issue.** However, it differs slightly from DevOps's method-level `[HttpPost("/internal/mcp/devops")]` only in that there's no class-level `[Route]` attribute at all. DevOps has the same pattern (no class-level `[Route]`), so this is consistent. Non-issue. |
| 17 | Loopback guard — IPv6-mapped IPv4 unwrapping | ✅ **PASS** — Focus item. Code reads: `if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6) remoteIp = remoteIp.MapToIPv4(); if (remoteIp is null \|\| !IPAddress.IsLoopback(remoteIp)) return StatusCode(403, ...)`. Identical to `DevOpsMcpAdapter`. The `::ffff:127.0.0.1` case is handled correctly. |
| 18 | `userId` parsed from `X-API-Key` via `Guid.TryParse` | ✅ **PASS** — `var userIdStr = HttpContext.Request.Headers["X-API-Key"].FirstOrDefault(); if (!Guid.TryParse(userIdStr, out var userId)) return Unauthorized(...)` |
| 19 | Token fetched via `MicrosoftTokenService.GetValidAccessTokenAsync(userId)` — returns `string?` | ✅ **PASS** — `var accessToken = await _tokenService.GetValidAccessTokenAsync(userId)` |
| 20 | Returns 403 if token is null | ⚠️ **NOTE** — When token is null, the adapter returns `Ok(new McpCallResponse { ... IsError = true })` with a user-facing message "No valid Microsoft 365 token for this user." rather than HTTP 403. This is **intentional MCP protocol behavior** — MCP tool errors are returned as application-level errors in the response body (isError: true), not HTTP 4xx. The Bedrock agentic loop sees the error text and surfaces it to the user gracefully. This is correct behavior, not a defect. The checklist item specified 403, but MCP-over-HTTP error semantics demand an `Ok` with `isError: true`. No change required. |
| 21 | Switch on `request.Tool` dispatches to 5 methods | ✅ **PASS** — `switch` expression covers all 5: `list_emails`, `get_email`, `send_email`, `list_calendar_events`, `create_calendar_event`, plus `_ => null` for unknown tools. |
| 22 | `CallGraph` uses `IHttpClientFactory.CreateClient("graph")` | ✅ **PASS** — `var client = _httpClientFactory.CreateClient("graph")` — no `new HttpClient()` usage. |
| 23 | `Authorization: Bearer {accessToken}` on every Graph request | ✅ **PASS** — Set in `CallGraph`: `req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken)`. All 5 tools route through `CallGraph`. |
| 24 | `send_email` body uses correct Graph sendMail payload shape | ✅ **PASS** — Focus item. Payload is: `{ message: { subject, body: { contentType: "Text", content }, toRecipients: [{ emailAddress: { address } }] } }`. Matches Graph `sendMail` spec exactly. Content-type is `application/json` (via `new StringContent(body, UTF8, "application/json")`). |
| 25 | `create_calendar_event` null-safe on optional `body` and `attendees` | ✅ **PASS** — Focus item. Uses `Dictionary<string, object?>` (not anonymous type) and adds `body` and `attendees` keys only when non-null. This avoids C# anonymous-type null serialization issues — null properties in anonymous types serialize as JSON `null` (e.g. `"body": null`), which Graph rejects. The `Dictionary` conditional-add pattern is correct. |

---

### Program.cs + ChatView (items 26–28)

| # | Item | Result |
|---|------|--------|
| 26 | `"graph"` HttpClient registered with 30s timeout | ✅ **PASS** — `builder.Services.AddHttpClient("graph", client => { client.Timeout = TimeSpan.FromSeconds(30); client.DefaultRequestHeaders.Add("Accept", "application/json"); })` present in `Program.cs`. |
| 27 | M365 system prompt injection guarded by `availableTools.Any(t => t.FullName.StartsWith("m365__"))` | ✅ **PASS** — `var hasM365Tools = availableTools.Any(t => t.FullName.StartsWith("m365__", StringComparison.OrdinalIgnoreCase))` guards the injection. |
| 28 | System prompt addition doesn't break non-M365 users | ✅ **PASS** — Guard condition ensures the M365 guidance block is only appended when M365 tools are present. Non-M365 users are unaffected. |

---

### Security (items 29–30)

| # | Item | Result |
|---|------|--------|
| 29 | `/internal/mcp/m365` loopback-only | ✅ **PASS** — Loopback guard at the top of `HandleMcpRequest` blocks all non-loopback IPs with a 403 before any processing. No `[Authorize]` attribute is needed; the IP guard is the security layer (same as DevOps and Brave adapters). |
| 30 | Access token never logged | ✅ **PASS** — The three `_logger` calls in `M365McpAdapter` log: tool name on error, Graph status/URL/truncated response body on warning. `accessToken` is never referenced in any logging call. The truncation (`json[..500]`) also prevents inadvertent token leakage from Graph error responses. |

---

## Focus Item Summary

| Focus Item | Finding |
|------------|---------|
| **#11** Transport routing | ✅ `m365_token` correctly in `api_key` branch alongside `devops_pat`. NOT in `bearerToken` branch. Adapter resolves real bearer token internally. |
| **#17** Loopback guard | ✅ IPv6-mapped IPv4 (`::ffff:127.0.0.1`) unwrapping present, identical to DevOpsMcpAdapter. |
| **#24** sendMail payload | ✅ Correct Graph shape: `{ message: { subject, body: { contentType, content }, toRecipients: [{ emailAddress: { address } }] } }`. |
| **#25** Null-safe optional fields | ✅ `Dictionary<string, object?>` with conditional adds — `body` and `attendees` only included when non-null. Avoids anonymous-type null serialization issue. |

---

## Non-Blocking Notes

1. **Item 20 — null token returns `Ok(isError:true)` not HTTP 403:** This is correct MCP protocol behavior. The checklist item said "403 if token is null" but HTTP 403 here would break the agentic tool-call loop (Bedrock expects a 200 with `isError: true` for application-level tool errors). Tony made the right call. No change needed.

2. **Item 16 — class-level `[Route]` attribute absent:** The route is defined on the method as `[HttpPost("/internal/mcp/m365")]` (absolute path). Functionally identical to a class-level `[Route]`. No change needed.

3. **`GetValidAccessTokenAsync` constructor call in callbacks:** `Program.cs` instantiates `MicrosoftTokenService` directly via `new` in the OAuth callback handlers (the `/auth/microsoft-callback` and `/api/tokens/{userId}` routes). This is pre-existing pattern not introduced in this commit. Out of scope.

---

## Summary

The M365 MCP adapter implementation is clean, consistent with the DevOps adapter pattern, and correctly handles all 30 checklist items. The four focus items are all correct. The implementation follows the established internal adapter pattern with no surprises.

**Verdict: PASS — ready for SECURITY stage.**

---

*Reviewed by Hawkeye (Clint Barton) — code-reviewer agent*
