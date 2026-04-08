# WI #1662 — Build Report: Phase 3 Schema Anchor Migration

**Date:** 2026-04-08
**Agent:** Tony Stark
**Cycle:** 1

---

## 1. CC Invocation

Task executed interactively via CC agent in `/home/fredw/projects/fip/nexus/src/FortressNexus.Web/`.

---

## 2. SubmissionNarrativeSnapshot Decision

**Decision: SKIPPED** — no `SubmissionNarrativeSnapshot` column added.

**Rationale:** The Phase 3 resume wizard will load `Submission.NarrativeText` and `SubmissionFiles` directly from the DB when resuming a draft. Change detection compares live-loaded values against current wizard state. Snapshot storage adds complexity with no functional benefit.

---

## 3. Files Created / Modified

| File | Action |
|------|--------|
| `Models/Enums/DiscoverySessionStatus.cs` | Created — string constants class |
| `Data/NexusDbContextDesignTimeFactory.cs` | Created — design-time factory for EF tooling |
| `Migrations/20260408162324_AddPhase3ResumeChanges.cs` | Created — EF migration |
| `Migrations/20260408162324_AddPhase3ResumeChanges.Designer.cs` | Created — EF migration designer snapshot |
| `Migrations/NexusDbContextModelSnapshot.cs` | Modified — updated by EF tooling |

---

## 4. Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.84
```

**Result: PASS**

---

## 5. Design-Time Factory Note

`dotnet ef migrations add` failed without a design-time factory because:
- `AddDbContextFactory<NexusDbContext>` is registered as singleton
- `AddDbContext<NexusDbContext>` registers options as scoped
- EF tooling cannot resolve the context via the app host due to this DI validation error

Added `NexusDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NexusDbContext>` to unblock EF tooling. This file is used only at design time (migrations generation), never at runtime.

---

## 6. Migration Status — `dotnet ef migrations list`

```
20260331145806_InitialCreate
20260402040040_AddSubmissionFilesJunctionTable
20260407180206_AddDiscoveryConversation
20260408162324_AddPhase3ResumeChanges
```

> Note: "Pending status not shown" warning is non-fatal — EF tooling cannot connect to localhost DB at design time, which is expected in this environment.

---

## 7. Migration Content — Not Empty

The `AddPhase3ResumeChanges` migration is **NOT empty**. EF detected a type discrepancy between:
- The `NexusDbContextModelSnapshot` from migration `20260407180206_AddDiscoveryConversation` (which recorded Discovery entity IDs as `string`/`varchar(36)`)
- The current entity model (`DiscoverySession.Id`, `DiscoveryQuestion.Id`, `DiscoveryAnswer.Id` are typed as `Guid` in C#)

EF scaffolded `AlterColumn` operations to align the DB columns from `varchar(36)` → `char(36) COLLATE ascii_general_ci`.

Per WI instructions: **migration is NOT reverted — it stands.**

---

## 8. Migration SQL (`dotnet ef migrations script --idempotent` — Phase 3 section only)

```sql
START TRANSACTION;

-- AddPhase3ResumeChanges

ALTER TABLE `discovery_sessions` MODIFY COLUMN `id` char(36) COLLATE ascii_general_ci NOT NULL;

ALTER TABLE `discovery_questions` MODIFY COLUMN `discovery_session_id` char(36) COLLATE ascii_general_ci NOT NULL;

ALTER TABLE `discovery_questions` MODIFY COLUMN `id` char(36) COLLATE ascii_general_ci NOT NULL;

ALTER TABLE `discovery_answers` MODIFY COLUMN `discovery_question_id` char(36) COLLATE ascii_general_ci NOT NULL;

ALTER TABLE `discovery_answers` MODIFY COLUMN `id` char(36) COLLATE ascii_general_ci NOT NULL;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260408162324_AddPhase3ResumeChanges', '8.0.13');

COMMIT;
```

> **Impact assessment:** These `MODIFY COLUMN` operations change `varchar(36)` → `char(36) ascii_general_ci` on ID and FK columns in the three Discovery tables. Both types store 36-character UUIDs. In MySQL 8, `char(36)` with `ascii_general_ci` collation is the Pomelo-recommended mapping for EF Guid columns. The data is compatible — no data loss. Foreign key constraints do not need to be dropped/recreated for collation-only changes in MySQL 8 when using `MODIFY COLUMN`.

---

## 9. Git Commit

**Hash:** `d42d0ed`

```
WI#1662 Add Phase3 schema anchor migration and DiscoverySessionStatus constants
```

**Pushed:** `origin/main` ✓

---

## 10. ADO Comment

```
[Tony Stark — BUILD cycle 1] commit d42d0ed. WI#1662 complete — DiscoverySessionStatus constants class added (Pending/QuestionsReady/Answered/Skipped/Failed/Superseded), EF migration AddPhase3ResumeChanges scaffolded (non-empty: aligns Discovery entity Guid IDs from varchar(36) to char(36) in snapshot — per WI, not reverted), NexusDbContextDesignTimeFactory added to unblock EF tooling, build 0 errors, pushed to main.
```

---

## Cycle 2 — Constants Adoption

**Date:** 2026-04-08
**Agent:** Tony Stark
**Commit:** `90fa325`

### Issue Addressed
Clint (code-reviewer) flagged I1: `DiscoverySessionStatus.cs` constants class was 100% unadopted — all 17 raw string literals across 3 files still used magic strings.

### Files Changed

| File | Change |
|------|--------|
| `Services/Discovery/DiscoveryService.cs` | Added `using FortressNexus.Web.Models.Enums;`; replaced 13 raw string literals with `DiscoverySessionStatus.*` constants |
| `Components/Pages/NewSpecWizard.razor` | Replaced 2 raw literals in `is` pattern match with constants |
| `Components/Nexus/DiscoveryAnswersSummary.razor` | Replaced 1 raw literal `"Skipped"` comparison with constant |

### Namespace Note
`_Imports.razor` already contained `@using FortressNexus.Web.Models.Enums` — no per-file `@using` required in Razor files.

### Replacement Summary
- `"Pending"` → `DiscoverySessionStatus.Pending` (2 occurrences in DiscoveryService.cs)
- `"Answered"` → `DiscoverySessionStatus.Answered` (2 occurrences in DiscoveryService.cs)
- `"Skipped"` → `DiscoverySessionStatus.Skipped` (3 occurrences: 2 in DiscoveryService.cs, 1 in DiscoveryAnswersSummary.razor)
- `"Failed"` → `DiscoverySessionStatus.Failed` (4 occurrences: 3 in DiscoveryService.cs, 1 in NewSpecWizard.razor)
- `"QuestionsReady"` → `DiscoverySessionStatus.QuestionsReady` (3 occurrences: 2 in DiscoveryService.cs, 1 in NewSpecWizard.razor)

### Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:04.05
```

**Result: PASS**

### Git Commit

**Hash:** `90fa325`

```
chore(nexus#1662): adopt DiscoverySessionStatus constants in service and components
```

**Pushed:** `origin/main` ✓
