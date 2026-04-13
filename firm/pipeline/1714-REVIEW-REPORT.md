## Review Report — ADO #1714: Org Context Wiki for AI Prompt Injection

### Verdict: ✅ PASS

**Cycle:** 1 | **Reviewer:** Hawkeye | **Date:** 2026-04-13

---

### CC Review Summary

CC reviewed all 10 files (5 new, 5 modified) against 14 targeted checks covering the MEMORY rule for HasColumnType, DDL idempotency, SQL safety, prompt injection format/position, error handling, admin auth, tenant ID resolution, Blazor admin gating, signature compatibility, and DI registration.

All 6 Critical checks and all 4 Important checks passed. CC surfaced 4 nitpick-level observations, all confirmed as low-risk. Zero blocking issues.

---

### Spec Compliance Check

**§2 Codebase Map — All 10 files verified:**
- `Models/FirmOrgContext.cs` — ✅ Created
- `Services/IOrgContextService.cs` — ✅ Created
- `Services/OrgContextService.cs` — ✅ Created
- `Controllers/OrgContextController.cs` — ✅ Created
- `Components/Pages/OrgContext.razor` — ✅ Created
- `Data/FirmDbContext.cs` — ✅ Modified (OrgContexts DbSet + entity config)
- `Data/DatabaseInitializationService.cs` — ✅ Modified (CREATE TABLE added)
- `Services/TeamsGraphService.cs` — ✅ Modified (SummarizeAsync signature + injection)
- `Controllers/MeetingsApiController.cs` — ✅ Modified (ReprocessSummary injects org context)
- `Program.cs` — ✅ Modified (DI registration)

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**Column name alignment (MEMORY rule — mandatory for every new entity):**

| EF Property | HasColumnName | DDL column | Match |
|-------------|---------------|------------|-------|
| `Id` | `"id"` | `id BIGINT AUTO_INCREMENT PRIMARY KEY` | ✅ |
| `EntraTenantId` | `"entra_tenant_id"` | `entra_tenant_id VARCHAR(36) NOT NULL` | ✅ |
| `WikiContent` | `"wiki_content"` | `wiki_content TEXT NULL` | ✅ |
| `UpdatedAt` | `"updated_at"` | `updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP` | ✅ |
| `UpdatedBy` | `"updated_by"` | `updated_by VARCHAR(256) NULL` | ✅ |

**UNIQUE KEY name:** DDL has `UNIQUE KEY uk_tenant (entra_tenant_id)`, EF config has `.HasDatabaseName("uk_tenant")` — ✅ match.

**SummarizeAsync callers — all 3 pass `orgWikiContent`:**
- `TeamsGraphService.ProcessVttForMeetingAsync` → ✅
- `TeamsGraphService.FetchAndProcessTranscriptAsync` → ✅
- `MeetingsApiController.ReprocessSummary` → ✅

---

### Critical Issues: 0

All critical checks pass.

---

### Important Issues: 0

All important checks pass.

---

### Nitpicks: 4

#### N1: Empty catch in MeetingsApiController — no logging
- **File:** `Controllers/MeetingsApiController.cs` (~line 794)
- **Issue:** `catch { /* org context is non-critical */ }` — no exception variable, no log. If the org context fetch fails during a manual reprocess, there's zero trace. Inconsistent with TeamsGraphService which logs `LogWarning(orgEx, "[TeamsGraph] Could not load org context...")`.
- **Fix:**
  ```csharp
  catch (Exception orgEx)
  {
      _logger.LogWarning(orgEx, "[MeetingsApi] Could not load org context for reprocess summary — continuing");
  }
  ```
- Not blocking.

#### N2: TeamsGraphService bypasses IOrgContextService — 3 query sites
- **File:** `Services/TeamsGraphService.cs` (lines ~213 and ~363)
- **Issue:** Both `ProcessVttForMeetingAsync` and `FetchAndProcessTranscriptAsync` query `db.OrgContexts.FirstOrDefaultAsync(...)` directly instead of calling `IOrgContextService.GetContextAsync`. The query logic is now duplicated in 3 places (service + 2 direct callers). Future changes to lookup behavior (caching, soft-delete, active flag) require 3 edits.
- **Fix:** Inject `IOrgContextService` into `TeamsGraphService` and replace both direct queries with `await _orgContextService.GetContextAsync(tenantId)`. The service already has its own try/catch returning null — non-fatal behavior is preserved.
- Not blocking. Worth a follow-up PR.

#### N3: Theoretical tenant ID mismatch (I1)
- **File:** `Controllers/OrgContextController.cs` `GetTenantId()` vs `Services/TeamsGraphService.cs` org context fetch
- **Issue:** The controller writes using `tid` claim (with config fallback), while TeamsGraphService reads using `_config["Firm:GraphTenantId"]` directly. In a correctly configured single-tenant deployment these are identical. If they ever diverge, org context would silently not be found during summarization. Build report acknowledges this; config fallback is the primary path.
- **Fix:** Add a startup assertion or log if `tid` claim (when present) differs from `Firm:GraphTenantId` config. Low operational priority.
- Not blocking.

#### N4: No StateHasChanged() in OrgContext.razor SaveAsync finally
- **File:** `Components/Pages/OrgContext.razor` `SaveAsync` finally block
- **Issue:** `_saving = false` set in `finally` with no `StateHasChanged()`. Blazor's EventCallback re-render will fire on handler completion so the button re-enables in practice. Still inconsistent with the established FIRM/FAIT pattern of explicit `StateHasChanged()` in finally blocks (see MEMORY.md — FORMS v2 Sprint 5).
- **Fix:** Add `StateHasChanged();` at end of the finally block.
- Not blocking.

---

### Positive Observations

- **MEMORY rule adherence (C1):** No `HasColumnType("char(36)")` anywhere in the FirmOrgContext entity config. Only `HasMaxLength(36)` used for `entra_tenant_id`. This was the primary gotcha called out in the review spec.
- **SQL safety (C3):** `UpsertContextAsync` uses parameterized `{0}/{1}/{2}` — no raw string interpolation of user content. Clean.
- **Prompt position (C4):** Org context block is prepended *before* the persona instruction, not appended after the transcript. The model sees org context first — correct for system context injection.
- **Non-fatal design (C5):** Two try/catch blocks in TeamsGraphService + a third in OrgContextService itself = layered defense. If any of them fail, summarization proceeds without context.
- **Auth (C6):** `[Authorize]` on controller class + server-side `IsAdmin()` OID check = correct dual-gate. PUT returns 403 for non-admins.
- **Blazor auth pattern (A5):** Uses `AuthenticationStateProvider` — no `IHttpContextAccessor` anti-pattern.
- **DDL idempotency (C2):** `CREATE TABLE IF NOT EXISTS`, in `extraTables` (not `alterStatements`), correct error handling. Clean migration path.

---

### Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| New DB table `firm_org_context` with idempotent DDL | ✅ Verified |
| EF entity with correct column mappings (no HasColumnType) | ✅ Verified |
| GET /api/org-context accessible to all authenticated users | ✅ Verified |
| PUT /api/org-context restricted to admin (403 for non-admins) | ✅ Verified |
| Org context injected into SummarizeAsync with clear delimiters | ✅ Verified |
| Org context prepended BEFORE persona instruction | ✅ Verified |
| Non-fatal: DB failure doesn't block summarization | ✅ Verified |
| All SummarizeAsync callers updated | ✅ Verified (3 callers) |
| Admin UI at /org-context with read-only view for non-admins | ✅ Verified |
| DI registration as Scoped (consistent with other services) | ✅ Verified |

---

_Hawkeye — cycle 1 complete. Ships._
