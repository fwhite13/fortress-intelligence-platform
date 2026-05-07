# ADO#2887 — FORGE KB Integration Service
## Code Review Report — Hawkeye (Clint Barton) | Cycle 1
**Date:** 2026-05-07
**Verdict: NEEDS-CHANGES**

---

## Summary

Two blocking issues found. One mandatory rule violation in ChatView. Advisory note on silent error behavior. All other mandatory checks pass.

---

## Blocking Issues

### ISSUE-1: `FipTokenProvider` will always return null — wrong claim name

**File:** `src/FortressAI.V2.Web/Services/FipTokenProvider.cs:17`

```csharp
var token = ctx.User.FindFirst("access_token")?.Value
         ?? ctx.User.FindFirst("token")?.Value;
```

**Root cause:** The FIP shared cookie (`.FortressAI.Session`) does NOT contain `access_token` or `token` as named claims. Verified in `fip/src/FortressIntelligencePlatform.Web/Program.cs:99-159`: on `OnTokenValidated`, the FIP portal stores the Entra/Graph access token to the `fip_dev.user_microsoft_tokens` table keyed by `entra_oid`. The only additional claim added to the principal is `ClaimTypes.Role` (line 107). The token is never written as a claim into the cookie.

`GetAccessTokenAsync()` will always return `null` for every request. This means all fip-mcp calls (`ForgeKbService.CallToolAsync`) go out without an `Authorization: Bearer` header and will receive 401 from fip-mcp.

**Correct pattern:** FIRM's `FipTokenService` (`firm/src/FortressIntelligenceRM.Web/Services/FipTokenService.cs`) reads from the DB:

```csharp
// FIRM pattern — correct
await using var db = await _dbFactory.CreateDbContextAsync();
var token = await db.UserMicrosoftTokens.FindAsync(entraOid);
if (token.ExpiresAt > DateTime.UtcNow.AddMinutes(5))
    return token.AccessToken;
// else refresh via OIDC token endpoint
```

**Required fix:** `FipTokenProvider` must accept `IDbContextFactory<FipDbContext>` (the `fip_dev` DB context), take `entraOid` as a parameter (or read it from `ctx.User`), and query `user_microsoft_tokens` — mirroring the FIRM pattern. The `IFipTokenProvider` interface signature may also need to change to accept `entraOid`.

---

### ISSUE-2: Design Agent tables bundled into AddMcpTables migration — will double-create on deploy

**File:** `src/FortressAI.V2.Web/Data/Migrations/20260507125357_AddMcpTables.cs:14-125`

The migration `AddMcpTables` creates four tables: `design_agent_sessions`, `mcp_servers`, `mcp_user_tokens`, `design_agent_artifacts`. WI #2865 (Design Agent, running in parallel) owns a separate migration for `design_agent_sessions` and `design_agent_artifacts`.

When both WIs merge to main and EF migrations are applied in sequence, the second migration to run will attempt `CREATE TABLE design_agent_sessions` / `CREATE TABLE design_agent_artifacts` on tables that already exist, causing a hard deploy failure.

**Required fix:** Remove `design_agent_sessions` and `design_agent_artifacts` from `AddMcpTables` migration (both `Up()` and `Down()`). This migration should only create `mcp_servers` and `mcp_user_tokens`. Coordinate with WI #2865 — those tables belong to that WI's migration.

Affected blocks to remove from `Up()`:
- Lines 14–41: `CreateTable("design_agent_sessions", ...)`
- Lines 96–125: `CreateTable("design_agent_artifacts", ...)`
- Lines 127–135: `CreateIndex` for `design_agent_artifacts` and `design_agent_sessions`

Affected blocks to remove from `Down()`:
- Lines 158–159: `DropTable("design_agent_artifacts")`
- Lines 165–169: `DropTable("design_agent_sessions")`

---

## Mandatory Rule Violations

### RULE-1: Hardcoded `28px` size in ChatView.razor

**File:** `src/FortressAI.V2.Web/Components/Chat/ChatView.razor`

Rule: "No hardcoded colors/fonts/sizes in .razor files (CSS variables only)"

Violations:
- **Line 121** (`<style>` block): `.chat-agent-pill { height: 28px; ... }` — hardcoded pixel size in CSS block inside razor file
- **Lines 168–174** (`GetFortressKbStyle` / `GetPersonalKbStyle` methods): inline style strings include `height: 28px` — hardcoded size emitted via C# string

The `28px` value should be a CSS variable (e.g., `var(--pill-height-sm)` or `var(--height-chip)`). All other values in these style strings correctly use CSS vars (`var(--space-3)`, `var(--color-gold)`, `var(--radius-md)`, etc.).

---

## Advisory Notes (No Code Change Required)

### ADV-1: Silent empty-list when fip-mcp is unreachable

Tony's concern #2 — `ListKbsAsync` returning empty list on null is intentional. `Dashboard.razor:74` comments confirm "KB list is non-critical — swallow errors, leave _kbs empty." The UI simply doesn't render the KB pills bar if fip-mcp is down. This is acceptable UX for a non-critical informational widget. No change needed.

---

## Items That PASS

| Check | Result |
|-------|--------|
| `GuidFormat = MySqlGuidFormat.None` on keyring CSB (`Program.cs:61`) | PASS |
| DefaultConnection includes `GuidFormat=None` in appsettings.json | PASS |
| All GUID columns `varchar(36)` in migration | PASS |
| All DateTime columns use `datetime(6)` in migration | PASS |
| `IHttpClientFactory` named client — `"FipMcpClient"` registered and used | PASS |
| No raw `HttpClient` instantiation | PASS |
| No Cognito references in new files | PASS |
| No `@{ var x = ... }` inside `@if/@else` blocks with markup | PASS |
| `IForgeKbService` DI registration — `AddScoped` | PASS |
| `FipTokenProvider` DI registration — `AddScoped` | PASS |
| `IHttpContextAccessor` registered (`AddHttpContextAccessor`) | PASS |
| MCP JSON-RPC 2.0 contract matches spec (method `tools/call`, `result.content[0].text` extraction) | PASS |
| `FipMcp:EndpointUrl` read from IConfiguration (not hardcoded) | PASS |
| Startup seed idempotent (checks `AnyAsync` before insert) | PASS |
| EF `DbSet<McpServer>` and `DbSet<McpUserToken>` registered in `FaitV2DbContext` | PASS |
| `ix_mcp_servers_name` unique index present | PASS |
| `ix_mcp_user_tokens_user_server` composite unique index present | PASS |

---

## Required Actions Before Re-Review

1. Fix `FipTokenProvider` to read from `fip_dev.user_microsoft_tokens` by `entraOid` (DB lookup, not claim lookup). Mirror FIRM's `FipTokenService` pattern.
2. Remove `design_agent_sessions` / `design_agent_artifacts` from `AddMcpTables` migration (Up + Down + indexes). Let WI #2865 own those tables.
3. Replace `height: 28px` in ChatView.razor `<style>` block and in `GetFortressKbStyle`/`GetPersonalKbStyle` with a CSS variable.
