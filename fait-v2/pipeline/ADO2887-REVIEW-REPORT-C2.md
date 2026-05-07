# Review Report: ADO#2887 — FORGE KB Integration Service (Cycle 2)

**Commit:** `77bcb20`
**Branch:** `main`
**Cycle:** 2 of 2
**Reviewer:** Hawkeye (code-reviewer)
**Date:** 2026-05-07

---

## Verdict: ✅ PASS

All three cycle 1 issues are fully resolved. No new blocking issues introduced.

---

## Spec Compliance Check

All cycle 1 NEEDS-CHANGES items verified fixed:
- ✅ **C1 FipTokenProvider** — now queries `fip_dev.user_microsoft_tokens` via `IDbContextFactory<FipPortalDbContext>` (mirrors FIRM's FipTokenService pattern)
- ✅ **C2 Migration tables** — `design_agent_sessions` and `design_agent_artifacts` removed from Up(), Down(), and model snapshot
- ✅ **C3 Hardcoded height** — `height: 28px` replaced with `var(--pill-height-sm)` in all 3 locations; token defined in `fortress.css`

---

## Consistency Audit

**FipPortalDbContext.cs ↔ fip_dev schema:**
- Table: `user_microsoft_tokens` ✅
- PK: `entra_oid` (string, maxLength 128) ✅
- All column names snake_case — `access_token`, `refresh_token`, `expires_at`, `microsoft_email`, `created_at`, `updated_at` ✅
- Separate context (`FipPortalDbContext`) correctly isolated from `FaitV2DbContext` — no cross-contamination ✅

**ChatView.razor ↔ fortress.css:**
- `var(--pill-height-sm)` used in all pill height declarations ✅
- `--pill-height-sm: 28px` defined under `:root` in fortress.css ✅

---

## Focus Area Results

### 1. FipPortalDbContext.cs — Entity Mapping ✅

- Connection string wiring in `Program.cs`: reuses `keyRingDbHost/Port/User/Pass`, overrides only `Database` via `FIP_DB_NAME ?? "fip_dev"` — correct, consistent with existing pattern
- Registered as `AddDbContextFactory<FipPortalDbContext>` with retry-on-failure — correct

### 2. FipTokenProvider.cs — Claims, Expiry, Null Safety ✅

- OID resolution: tries short claim `"oid"` first, falls back to full URI `http://schemas.microsoft.com/identity/claims/objectidentifier` — correct two-claim pattern for MSAL tokens
- Three null guard layers: `HttpContext?.User` → `entraOid` null/empty → `tokenRecord == null`
- Expiry: `ExpiresAt < UtcNow + 5min` window with graceful `null` return (not throw)
- Uses `IDbContextFactory` with `await using` — correct disposal pattern

### 3. AddMcpTables Migration — design_agent tables stripped ✅

- `Up()`: Both `CreateTable` blocks + associated `CreateIndex` calls removed
- `Down()`: Both `DropTable` calls removed
- Remaining `mcp_servers` and `mcp_user_tokens` content intact and untouched

### 4. FaitV2DbContextModelSnapshot.cs — Entities removed ✅

- `DesignAgentArtifact` entity block removed
- `DesignAgentSession` entity block removed
- FK relationships (`Artifact → Session`, `Session → User`) removed
- Navigation property removed
- No orphaned references remain

### 5. ChatView.razor — height: 28px replacements ✅

All 3 instances replaced:
- `.pill` CSS block (inline style)
- `GetFortressKbStyle()` — enabled and disabled branches
- `GetPersonalKbStyle()` — enabled and disabled branches
- No remaining `28px` literals in the file

### 6. fortress.css — --pill-height-sm defined ✅

- `--pill-height-sm: 28px` added under `:root` in the "Component sizes" section, correctly positioned after shape variables

### 7. Quick Scan — Hardcoded values / raw HttpClient / broken DI ✅

- No hardcoded Bedrock model IDs, AWS account IDs, or regions introduced
- No raw `new HttpClient()` — existing `AddHttpClient("FipMcpClient")` registration untouched
- DI ordering correct: `IDbContextFactory<FipPortalDbContext>` registered before `FipTokenProvider` scoped registration

---

## Issues Found

None. Zero blocking issues.

---

## Positive Observations

- The `FipPortalDbContext` isolation pattern is clean — read-only context against `fip_dev` with no DbSet migrations in `FaitV2DbContext`. Correct architectural separation.
- The two-claim OID fallback in `FipTokenProvider` is more robust than FIRM's implementation and handles both short-form and full-URI claim formats.
- Null safety is thorough — three distinct null checks before the token is used, matching the spec's requirements.

---

_Ships. Good fix cycle, Tony._
