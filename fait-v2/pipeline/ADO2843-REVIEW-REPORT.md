# Review Report — ADO#2843

**WI:** FAIT v2: Aurora MySQL schema — users, main_assistants, projects, memory_topics, user_sessions  
**Commit:** `6bf0d14`  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1 of 2  
**Date:** 2026-05-06

---

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC invoked via:
```bash
cat /tmp/clint-brief-2843.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC performed full three-layer consistency audit (model ↔ DbContext ↔ migration) across all 5 entities. No false positives identified. Two confirmed issues escalated to Important, one schema design inconsistency flagged as Important (naming convention deviation), one Warning on CHAR vs VARCHAR.

---

### Spec Compliance Check

No developer brief path was specified in the WI. Reviewed against the WI description and the review checklist from the dispatch.

**Codebase Map (files expected):**
- `Data/Models/User.cs` — ✅ created
- `Data/Models/MainAssistant.cs` — ✅ created
- `Data/Models/Project.cs` — ✅ created
- `Data/Models/MemoryTopic.cs` — ✅ created
- `Data/Models/UserSession.cs` — ✅ created
- `Data/FaitV2DbContext.cs` — ✅ created
- `Data/FaitV2DbContextDesignTimeFactory.cs` — ✅ created
- `Data/Migrations/20260506224542_InitialSchema.cs` — ✅ created
- `Program.cs` — ✅ modified (DbContext registration)
- `FortressAI.V2.Web.csproj` — ✅ modified (added EF Design pkg)

**Spec compliance verdict:** ✅ COMPLIANT on scope. Two Important issues found; no Critical blockers.

---

### Consistency Audit

**Three-layer check (model ↔ DbContext ↔ migration):**

| Entity | Column Names | Types | Nullability | Indexes |
|--------|-------------|-------|------------|---------|
| User | ✅ match | ✅ match | ✅ match | ✅ match |
| MainAssistant | ✅ match | ✅ match | ✅ match | ✅ match |
| Project | ✅ match | ✅ match | ✅ match | ✅ match |
| MemoryTopic | ✅ match | ✅ match | ✅ match | ✅ match |
| UserSession | ✅ match | ✅ match | ✅ match | ✅ match |

All `[Column("...")]` attributes on models match `HasColumnName("...")` in DbContext. No three-layer mismatches.

**FK relationship check:**
- `main_assistants → users`: `HasOne/WithOne` ✅ (correctly 1:1; `User.MainAssistant` typed as `MainAssistant?` not `ICollection`) 
- `projects → users`: `HasOne/WithMany` ✅
- `memory_topics → users`: `HasOne/WithMany` ✅
- `user_sessions → users`: `HasOne/WithMany` ✅

---

### Critical Issues — 0

None.

---

### Important Issues — 2

#### I1: `user_sessions` table missing `created_at` / `updated_at`

- **File:** `Data/Models/UserSession.cs`, `Data/FaitV2DbContext.cs`, `Data/Migrations/20260506224542_InitialSchema.cs`
- **Issue:** All 4 other tables have `created_at` + `updated_at`. `user_sessions` has neither. It has `started_at` (semantically equivalent to `created_at`) and `last_active_at`, but these don't satisfy the schema convention used everywhere else.
- **Impact:** Any tooling, query, or audit that expects `created_at`/`updated_at` on all tables will fail silently for `user_sessions`. The build report claims all timestamps follow the same convention — this is inaccurate.
- **Fix:** Add `CreatedAt` and `UpdatedAt` to `UserSession.cs`, configure in DbContext, and generate a second migration — OR explicitly document that `user_sessions` uses `started_at`/`last_active_at` in lieu of standard timestamps and update the WI acceptance criteria accordingly. If the design intent is that `started_at = created_at` semantically, at minimum rename it for consistency.

```diff
// UserSession.cs — add after UserAgent property
+   [Column("created_at")]
+   public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
+
+   [Column("updated_at")]
+   public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
```

#### I2: Build report claims "CHAR(36) GUIDs" but migration generates `varchar(36)`

- **File:** `Data/Migrations/20260506224542_InitialSchema.cs` (all PK/FK columns), `Data/Migrations/FaitV2DbContextModelSnapshot.cs`
- **Issue:** The WI checklist and Tony's build report both state "All PKs are CHAR(36) GUIDs." The migration and snapshot show `varchar(36)` for all ID columns. Pomelo maps `HasMaxLength(36)` → `varchar(36)` by default; `char(36)` requires an explicit `.HasColumnType("char(36)")` in each entity configuration.
- **Impact:** Functionally equivalent for Aurora MySQL at this scale. However, the stated design contract is violated — if CHAR(36) was specified for performance reasons on indexed lookups, the current schema doesn't deliver that. More immediately, the build report is factually wrong.
- **Fix Option A (recommended):** Add explicit `.HasColumnType("char(36)")` to all PK and FK column configurations in `FaitV2DbContext.cs`, then regenerate the migration. This aligns implementation with spec.
- **Fix Option B:** Acknowledge varchar(36) is acceptable and update the WI/checklist to say "varchar(36)" instead of "CHAR(36)". Then this is a docs fix only.

---

### Warnings — 1

#### W1: Inconsistent "last modified" timestamp naming across tables

Three different naming conventions for the same concept across 5 tables:
- `updated_at` — users, main_assistants, projects ✅ standard
- `last_updated_at` — memory_topics ⚠️ deviation
- `last_active_at` — user_sessions ⚠️ deviation (and no `updated_at` at all — see I1)

Not blocking, but worth standardizing. Recommend normalizing to `updated_at` everywhere (memory_topics can keep `last_updated_at` as a separate semantic field if needed, alongside a standard `updated_at`).

---

### Nitpicks — 2

- **N1: Mixed S3/blob naming in `main_assistants`** — `soul_blob_path`, `memory_blob_path` (generic blob naming) alongside `workspace_s3_prefix` (AWS-specific naming) in the same entity. If all three point to S3, name them consistently: either all `_blob_path` or all `_s3_*`. Tony flagged this himself; a follow-up rename to `workspace_blob_prefix` is reasonable.

- **N2: `UpdatedAt` won't auto-update on EF saves** — All models initialize `UpdatedAt = DateTime.UtcNow` at construction, but EF Core won't update it automatically on subsequent saves. Without a `SaveChangesInterceptor` or explicit `entry.Entity.UpdatedAt = DateTime.UtcNow` before saves, this column will stay stale. Not a schema bug, but a time bomb for application logic. Add an interceptor before any service layer code lands.

---

### Positive Observations

- Three-layer consistency (model ↔ DbContext ↔ migration) is clean across all 5 entities. No silent column name drift.
- `GuidFormat=None` properly in appsettings.json AND DesignTimeFactory — correct and consistent.
- `MySqlServerVersion(8, 0, 28)` used in both Program.cs and DesignTimeFactory — no AutoDetect.
- `EnableRetryOnFailure(3)` in place.
- UNIQUE constraints on `entra_oid`, `email`, and `main_assistants.user_id` correctly in both DbContext and migration.
- All nullable columns correct across all three layers — no EF nullable trap.
- `HasOne/WithOne` for `main_assistants` is correct for 1:1 relationship; `User.MainAssistant` properly typed as `MainAssistant?`.
- Zero Cognito references. Zero Azure Blob confusion in column names.
- `IDesignTimeDbContextFactory<FaitV2DbContext>` pattern correct — `dotnet ef` tooling will work.
- Migration `Down()` drops tables in correct dependency order.

---

### What to Fix (NEEDS-CHANGES)

**Required before PASS:**

1. **I1 — `user_sessions` timestamps:** Either add `created_at` + `updated_at` to `UserSession` model + DbContext + new migration, OR document explicitly why this table deviates and get Fred's sign-off that `started_at` serves as `created_at` and no `updated_at` is needed.

2. **I2 — CHAR(36) vs varchar(36):** Pick a path — either add `.HasColumnType("char(36)")` to all PK/FK configs and regenerate the migration, OR accept varchar(36) and correct the WI checklist language. Need Fred's direction on which.

Both fixes are mechanical and non-risky. A new migration for I1 is required regardless of the I2 decision.
