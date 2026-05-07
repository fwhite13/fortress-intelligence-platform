# Build Report — ADO#2887 — FORGE KB Integration Service

**Sprint 3 | FAIT v2 | Tony Stark — BUILD cycle 2 (review fixes)**
**Date:** 2026-05-07
**Commit:** `77bcb20`
**Branch:** `main`
**Build:** SUCCEEDED (0 errors, 0 warnings)

---

## Cycle 2 — Review Fixes Applied

### Fix 1 (BLOCKING): FipTokenProvider — DB lookup via entraOid

**Files changed:**
- `Data/FipPortalDbContext.cs` *(new)* — minimal DbContext for `fip_dev.user_microsoft_tokens`; entity `FipPortalUserMicrosoftToken` inlined (mirrors FIRM's `FipDbContext` pattern)
- `Services/FipTokenProvider.cs` — rewrote to inject `IDbContextFactory<FipPortalDbContext>`; resolves `entraOid` from `oid` / objectidentifier claim, queries token store, checks `ExpiresAt` (5-min window), returns `AccessToken`
- `Program.cs` — added `IDbContextFactory<FipPortalDbContext>` registration using `FORTRESS_DB_*` env vars and `FIP_DB_NAME` (default `fip_dev`)

**Root cause:** Cycle 1 read `access_token`/`token` claims from the cookie principal — those claims don't exist in the FIP shared cookie. Every call returned null → all fip-mcp requests went out without Bearer → 401s.

---

### Fix 2 (BLOCKING): Remove design_agent tables from AddMcpTables migration

**Files changed:**
- `Data/Migrations/20260507125357_AddMcpTables.cs` — removed `design_agent_sessions` and `design_agent_artifacts` CreateTable calls and associated CreateIndex calls from `Up()`; removed corresponding DropTable calls from `Down()`
- `Data/Migrations/FaitV2DbContextModelSnapshot.cs` — removed DesignAgentArtifact and DesignAgentSession entity blocks, FK/navigation entries

**Root cause:** Cycle 1 migration created 4 tables; the design_agent ones are owned by WI#2865 — double-create failure when migrations run in order.

**Coordination note:** `DbSet<DesignAgentSession>` and `DbSet<DesignAgentArtifact>` remain in `FaitV2DbContext`. WI#2865 should run `dotnet ef migrations add AddDesignAgentTables` to produce a clean migration for those tables.

---

### Fix 3 (NITPICK): Replace hardcoded `height: 28px` with CSS variable

**Files changed:**
- `wwwroot/css/fortress.css` — added `--pill-height-sm: 28px;` under `:root` Component sizes section
- `Components/Chat/ChatView.razor` — replaced all 3 occurrences of `height: 28px` (in `.chat-agent-pill` style and `GetFortressKbStyle()`/`GetPersonalKbStyle()` helpers) with `height: var(--pill-height-sm)`

---

## Build Gate (Cycle 2)
```
dotnet build — src/FortressAI.V2.Web/
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Cycle 1 Summary (for reference)

| Deliverable | Status |
|-------------|--------|
| EF migration: mcp_servers + mcp_user_tokens | PASS |
| ListKbsAsync / SearchKbAsync / AddToKbAsync / GetKbMetadataAsync | PASS |
| ForgeKbService uses IHttpClientFactory ("FipMcpClient") | PASS |
| mcp_servers seeded with forge-kb on startup (idempotent) | PASS |
| Dashboard displays accessible KBs | PASS |
| DI registration, 0 build errors | PASS |

---

## Cycle 2 Acceptance Criteria

| Criterion | Status |
|-----------|--------|
| FipTokenProvider queries fip_dev.user_microsoft_tokens by entraOid | PASS |
| Token expiry check (5-min window) before returning | PASS |
| FipPortalDbContext registered with correct connection string | PASS |
| design_agent tables absent from AddMcpTables migration Up()/Down() | PASS |
| design_agent entities absent from model snapshot | PASS |
| `height: 28px` replaced with CSS variable in ChatView.razor (3 occurrences) | PASS |
| --pill-height-sm defined in fortress.css :root | PASS |
| Build: 0 errors, 0 warnings | PASS |
