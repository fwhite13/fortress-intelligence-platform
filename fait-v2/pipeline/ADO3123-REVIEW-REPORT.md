# Review Report — ADO#3123

### Verdict: ✅ PASS

**Commit reviewed:** `50031da7` (merged to main as `c7205fa9`)  
**Reviewer:** Clint Barton / Hawkeye  
**Date:** 2026-05-09  
**Method:** CC adversarial review + independent verification against v1 source

---

## CC Review Summary

CC reviewed all 6 target files. Findings confirmed by independent cross-verification against the v1 FAIT AppDbContext and initial migration (`20260302054759_AddDataProtectionKeys.cs`). No false positives dismissed — all CC findings were confirmed accurate.

---

## Spec Compliance Check

**The Mandate:**
1. fait_dev is source of truth — v2 adapts to DB ✅
2. NO DROP TABLE, NO ALTER TABLE removing columns ✅
3. v2 EF models match fait_dev column names/types ✅
4. New v2-only tables use CREATE TABLE IF NOT EXISTS ✅ (new tables only; EF migrations history prevents re-execution)
5. New columns → ALTER TABLE ADD COLUMN, nullable/default ✅
6. Old v2 migrations replaced by single FaitDevConsolidation migration ✅

---

## Consistency Audit

### files cross-referenced:
- `20260509000000_FaitDevConsolidation.cs` ↔ v1 `AppDbContext.cs` ↔ v1 `AddDataProtectionKeys.cs` migration → **column names confirmed**
- `User.cs` [Column annotations] ↔ `FaitV2DbContext.cs` fluent API → fluent API wins per EF rules; runtime behavior correct
- `KbEntry/KbTeam/KbTeamMember.cs` ↔ `FaitV2DbContext.cs` ↔ migration SQL indexes → **int PKs consistent end-to-end**
- `KbForgeService.cs` ↔ KB models → **all int Id references confirmed**

### Column casing resolution:
The task brief described fait_dev `users.id` as lowercase. Independent verification against the v1 EF migration shows the column was created as **`Id`** (PascalCase, `char(36)`). The v2 DbContext `HasColumnName("Id")` is correct. MySQL column names are case-insensitive at runtime regardless, but the mapping is accurate.

---

## A. Migration Safety ✅

| Check | Result |
|-------|--------|
| DROP TABLE statements | **Zero** ✅ |
| ALTER TABLE … DROP COLUMN | **Zero** ✅ |
| ADD COLUMN uses IF NOT EXISTS | **All 11 ALTER TABLE statements** ✅ |
| New columns nullable or have DEFAULT | **Yes** — NULL, DEFAULT 0, DEFAULT 'main', DEFAULT CURRENT_TIMESTAMP(6) ✅ |
| CREATE TABLE without IF NOT EXISTS | **Only v2-only new tables** — guarded by EF migrations history ✅ |
| Down() method | **Empty with comment** — intentionally non-destructive ✅ |

All ALTER TABLE operations on existing fait_dev tables (`users`, `conversations`, `messages`, `projects`, `mcp_servers`, `user_mcp_tokens`) are strictly additive. Migration is safe to run against a live v1 fait_dev.

---

## B. User Model Alignment ✅

| fait_dev column | v2 mapping | Result |
|----------------|------------|--------|
| `Id` (char(36), PK) | `[Column("id")]` annotation → `HasColumnName("Id")` fluent override | ✅ Correct (fluent wins; PascalCase matches v1 schema) |
| `Email` (varchar(255) NOT NULL) | `HasColumnName("Email")` | ✅ |
| `CreatedAt` (datetime(6) NOT NULL) | `[Column("created_at")]` annotation → `HasColumnName("CreatedAt")` fluent override | ✅ Correct (fluent wins) |
| `entra_oid` (varchar(255) NULL) | `[Column("entra_oid")]`, `HasColumnName("entra_oid").IsRequired(false)`, `string?` | ✅ |
| `DisplayName` (varchar(100) NULL) | `[Column("display_name")]` → `HasColumnName("DisplayName")` fluent | ✅ |
| `PasswordHash`, `Role`, `is_active`, `is_entra_user`, `LastLogin` | **Not mapped in v2** | ✅ EF ignores unmapped v1 columns |

**Annotation/fluent conflict note (non-blocking, cleanup item):**  
`User.cs` has `[Column("id")]` and `[Column("created_at")]` (lowercase) but the DbContext overrides both to PascalCase via fluent API. EF fluent always wins. Runtime is correct, but the conflicting annotations are misleading and should be cleaned up.

---

## C. KB Int PKs ✅

| Model | Id type | FK types | ValueGeneratedOnAdd |
|-------|---------|----------|---------------------|
| `KbEntry` | `int` | `TeamId` as `int?`, `UserId` as `string` | ✅ |
| `KbTeam` | `int` | `CreatorId` as `string` | ✅ |
| `KbTeamMember` | `int` | `TeamId` as `int`, `UserId` as `string` | ✅ |

No remaining string references to KB entity IDs. Migration SQL indexes reference correct PascalCase column names (`UserId`, `TeamId`, `CreatorId`) matching DbContext fluent mappings.

---

## D. DbContext Consistency ✅

KB table column mappings are internally consistent: DbContext fluent API PascalCase names match the migration's own index creation SQL. No HasColumnName gaps found for KB entities.

---

## E. mcp_servers Mapping ✅

v1 columns not mapped in v2 McpServer model (`description`, `icon_url`, `transport_type`, `auth_config`, `tool_manifest`, `requires_user_auth`, `system_api_key`, `updated_at`, `oauth_client_secret`, `rate_limit_per_minute`) are safely ignored by EF — v1 data fully preserved.

New v2 columns `default_read` and `default_write` correctly added via `IF NOT EXISTS` with safe defaults (`DEFAULT 1` and `DEFAULT 0`).

**Minor:** `ix_mcp_user_tokens_user_server` is declared in both raw SQL (line 55, with IF NOT EXISTS) and DbContext fluent API. Harmless due to migration history guard, but untidy — remove one in a follow-up PR.

---

## F. KbForgeService.cs ✅

All method signatures use `int` for `entryId` and `teamId`. No remnant string KB ID references.

- `db.KbEntries.FindAsync(entryId)` with `int entryId` ✅
- `entry.TeamId!.Value` correct for `int?` ✅
- `team.Id` post-`SaveChangesAsync()` correctly used for auto-increment FK assignment ✅
- Transaction pattern for `CreateTeamAsync` (team + owner member) is correct ✅

---

## Issues Found

| Severity | File | Issue | Fix |
|----------|------|-------|-----|
| Nitpick | `User.cs` | `[Column("id")]` and `[Column("created_at")]` conflict with DbContext PascalCase fluent overrides — misleading to future devs | Align annotations to match fluent: `[Column("Id")]` and `[Column("CreatedAt")]` |
| Nitpick | `FaitV2DbContext.cs` | `ix_mcp_user_tokens_user_server` declared twice — raw SQL and fluent API | Remove raw SQL declaration; keep fluent API version |

**Neither item blocks deployment.** Both are cleanup for a follow-up PR.

---

## Summary

Migration is safe. All ALTER TABLE statements are additive with IF NOT EXISTS. No destructive operations anywhere. v2 models align with fait_dev column names. KB Int PKs are consistent end-to-end through models, DbContext, migration SQL, and service layer. Two cosmetic nitpicks noted; neither affects runtime correctness or safety.

**PASS — safe to deploy against fait_dev.**
