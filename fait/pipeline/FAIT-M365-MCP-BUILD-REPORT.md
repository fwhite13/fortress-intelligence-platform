# Build Report: FAIT-M365-MCP

**Agent:** Tony Stark (software-engineer)
**Date:** 2026-03-13
**Commit:** `64aebd1`
**Build Result:** ✅ 0 Error(s), 29 Warning(s) (pre-existing warnings, none new)

---

## Summary

Implemented the full M365 MCP adapter stack: database seed, McpToolService gating, M365McpAdapter controller, Program.cs HttpClient registration, and ChatView system prompt injection.

---

## Files Modified

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/DatabaseInitializationService.cs` | Added M365 MCP server seed block after DevOps seed |
| `src/FortressAI.Web/Services/McpToolService.cs` | Added M365Slug sentinel, GetConversationToolsAsync gate, ExecuteToolAsync token routing, GetActiveServersForUserAsync gate, transport call routing |
| `src/FortressAI.Web/Services/M365McpAdapter.cs` | **NEW** — MCP JSON-RPC adapter for Microsoft Graph API (5 tools) |
| `src/FortressAI.Web/Program.cs` | Registered `"graph"` named HttpClient |
| `src/FortressAI.Web/Components/Chat/ChatView.razor` | Added M365 system prompt injection block |

---

## Part 1: DatabaseInitializationService.cs

- **Server ID:** `00000000-0000-0000-0000-000000000003`
- **Slug:** `m365`
- **Name:** `Microsoft 365`
- **AuthType:** `m365_token`
- **RequiresUserAuth:** `0` ✅ — gated in McpToolService by `UserMicrosoftTokens` token existence check
- **ON DUPLICATE KEY UPDATE covers:** `endpoint_url`, `auth_type`, `requires_user_auth`, `tool_manifest`, `updated_at` ✅
- **Tool manifest:** 5 tools seeded — `list_emails`, `get_email`, `send_email`, `list_calendar_events`, `create_calendar_event`

---

## Part 2: McpToolService.cs

- **M365Slug sentinel added** — `Slug = "m365"`, `AuthType = "m365_token"`
- **GetConversationToolsAsync gate:** checks `db.UserMicrosoftTokens.AnyAsync(t => t.UserId == userId && t.AccessToken != null)` — if no token, tools are skipped
- **ExecuteToolAsync token routing:** `m365_token` routes `userId.ToString()` as the api_key (same as DevOps pattern)
- **Transport routing:** `m365_token` added to the `api_key` branch so userId is sent as `X-API-Key` header
- **GetActiveServersForUserAsync gate:** M365 server only appears when `UserMicrosoftTokens` has a valid token for the user

### EF DbSet Name for Microsoft Tokens
**`db.UserMicrosoftTokens`** (confirmed from `AppDbContext.cs` line 22: `public DbSet<UserMicrosoftToken> UserMicrosoftTokens => Set<UserMicrosoftToken>();`)

---

## Part 3: M365McpAdapter.cs

- **Method used to get valid M365 token:** `MicrosoftTokenService.GetValidAccessTokenAsync(Guid userId)` — returns `string?` directly (already handles auto-refresh with 5-minute buffer; removes token from DB if refresh fails)
- **Note:** The task brief referenced `GetValidTokenAsync` which does not exist. The correct method is `GetValidAccessTokenAsync` which returns the access token string directly (not an entity object). Used this method accordingly.
- **Loopback guard:** Uses `IPAddress.IsLoopback(remoteIp)` — same as DevOpsMcpAdapter
- **MCP protocol:** Implements full JSON-RPC (`tools/list` + `tools/call`) matching DevOpsMcpAdapter pattern
- **5 Graph API tools implemented:** `list_emails`, `get_email`, `send_email`, `list_calendar_events`, `create_calendar_event`
- **`"graph"` named HttpClient:** registered in Program.cs with 30s timeout and Accept: application/json header

---

## Part 4: ChatView.razor

- Added `hasM365Tools` check for `m365__` prefix (case-insensitive)
- Injected M365 guidance into `effectiveSystemPrompt` after DevOps block, before search tool block
- Guidance instructs model to use tools proactively for email/calendar queries and to confirm before send/create actions

---

## Key Findings

| Item | Value |
|------|-------|
| Token method | `GetValidAccessTokenAsync(Guid userId)` returns `string?` |
| EF DbSet name | `db.UserMicrosoftTokens` (`DbSet<UserMicrosoftToken>`) |
| `requires_user_auth = 0` in seed | ✅ |
| McpToolService gate uses token existence | ✅ (`AnyAsync` on `UserMicrosoftTokens`) |
| `ON DUPLICATE KEY UPDATE` covers `auth_type` + `requires_user_auth` | ✅ |
| Build errors | 0 ✅ |
| Commit SHA | `64aebd1` |

---

## Acceptance Criteria Verification

- [x] M365 seed row inserted with correct ID, slug, auth_type, requires_user_auth=0
- [x] ON DUPLICATE KEY UPDATE includes all required fields
- [x] M365Slug sentinel defined in McpToolService
- [x] GetConversationToolsAsync gates on token existence
- [x] ExecuteToolAsync routes m365_token via api_key path
- [x] GetActiveServersForUserAsync gates on token existence
- [x] M365McpAdapter.cs created with loopback guard + 5 Graph tools
- [x] `"graph"` HttpClient registered in Program.cs
- [x] ChatView.razor M365 system prompt injection added
- [x] `dotnet build` → 0 errors
- [x] Committed and pushed (`64aebd1`)
