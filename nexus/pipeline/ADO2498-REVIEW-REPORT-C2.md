# Review Report — ADO#2498 Cycle 2

### Verdict: PASS

**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 2 of 2
**Commit reviewed:** `a965b58`
**Date:** 2026-04-28

---

## CC Review Invocation

Review executed via CC with adversarial brief (`/tmp/review-2498-c2-brief.md`):

```bash
cd /home/fredw/projects/fip/nexus && cat /tmp/review-2498-c2-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC read all 7 changed files and performed 5 adversarial verification tasks (22 individual checks). All 22 passed. Results synthesized below.

---

## Spec Compliance Check

**Spec:** `memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`

**Cycle 2 change scope (verified against `git show a965b58 --name-only`):**

| File | Expected | Status |
|------|----------|--------|
| `Models/Entities/WorkItemRecord.cs` | C1: add `ParentTitle` | ✅ Present |
| `Data/NexusDbContext.cs` | C1: add EF mapping for `parent_title` | ✅ Present |
| `Migrations/20260428131416_AddWorkItemRecordParentTitle.cs` | C1: new migration | ✅ Present |
| `Migrations/20260428131416_AddWorkItemRecordParentTitle.Designer.cs` | C1: auto-generated | ✅ Present |
| `Migrations/NexusDbContextModelSnapshot.cs` | C1: snapshot update | ✅ Present |
| `Models/DTOs/AdoWorkItemDto.cs` | C2: add `PredecessorTitles` | ✅ Present |
| `Services/StubAdoService.cs` | C1+C2: map both fields in both methods | ✅ Present |
| Pipeline docs (5 files) | Expected pipeline artifacts | ✅ Expected |

**Out of Scope:**
- ✅ No out-of-scope changes detected. 12 files changed, all within the expected set.

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**C1 Fix — ParentTitle end-to-end chain:**

| Layer | Check | Result |
|-------|-------|--------|
| `WorkItemRecord.cs:24` | `public string? ParentTitle { get; set; }` — nullable string | ✅ |
| `NexusDbContext.cs:166` | `HasColumnName("parent_title").HasMaxLength(500)` — no `.IsRequired()` | ✅ |
| Migration `Up()` | `name: "parent_title"`, `type: "varchar(500)"`, `nullable: true` | ✅ |
| Migration `Down()` | `DropColumn("parent_title", "work_item_records")` | ✅ |
| `StubAdoService.CreateWorkItemAsync:60` | `ParentTitle = dto.ParentTitle,` | ✅ |
| `StubAdoService.CreateWorkItemBatchAsync:83` | `ParentTitle = dto.ParentTitle,` | ✅ |

**EF Config vs Migration consistency:**

| Attribute | NexusDbContext | Migration | Match |
|-----------|---------------|-----------|-------|
| Column name | `"parent_title"` | `"parent_title"` | ✅ |
| Max length | `HasMaxLength(500)` | `maxLength: 500` | ✅ |
| Column type | implied `varchar(500)` | `varchar(500)` | ✅ |
| Nullable | no `.IsRequired()` | `nullable: true` | ✅ |

**C2 Fix — PredecessorTitles end-to-end chain:**

| Layer | Check | Result |
|-------|-------|--------|
| `AdoWorkItemDto.cs:20` | `public List<string>? PredecessorTitles { get; set; }` | ✅ |
| `StubAdoService.CreateWorkItemAsync:61` | `PredecessorTitles = dto.PredecessorTitles` | ✅ |
| `StubAdoService.CreateWorkItemBatchAsync:84` | `PredecessorTitles = dto.PredecessorTitles` | ✅ |

**Cycle 1 regression check — fields that were passing must still pass:**

| Field | CreateWorkItemAsync | CreateWorkItemBatchAsync | Status |
|-------|---------------------|--------------------------|--------|
| `WiTemplate` | ✅ L56 | ✅ L79 | No regression |
| `IsExternalDependency` | ✅ L57 | ✅ L80 | No regression |
| `ExternalOwner` | ✅ L58 | ✅ L81 | No regression |
| `TestedByTitles` | ✅ L59 | ✅ L82 | No regression |
| `ExternalDependencyCount` | N/A (single-item) | ✅ L87 | No regression |

---

## Critical Issues [0]

None. Both C1 and C2 fixes are correct and complete.

---

## Important Issues [0]

None.

---

## Nitpicks [0]

None.

---

## C1 Fix Confirmation

✅ **ParentTitle is fixed end-to-end.**

- `WorkItemRecord.cs` has `public string? ParentTitle { get; set; }` (line 24)
- `NexusDbContext.cs` maps it as `parent_title VARCHAR(500) NULL` (line 166, no `.IsRequired()`)
- Migration `AddWorkItemRecordParentTitle.Up()` adds `parent_title varchar(500) nullable` to `work_item_records`
- Migration `.Down()` drops the column cleanly
- `StubAdoService.CreateWorkItemAsync` maps `ParentTitle = dto.ParentTitle` (line 60)
- `StubAdoService.CreateWorkItemBatchAsync` maps `ParentTitle = dto.ParentTitle` (line 83)

The Test Case → parent Story link now survives the entity boundary. When WI-7 enables real persistence, the column will be there.

---

## C2 Fix Confirmation

✅ **PredecessorTitles is fixed end-to-end.**

- `AdoWorkItemDto.cs` has `public List<string>? PredecessorTitles { get; set; }` (line 20)
- `StubAdoService.CreateWorkItemAsync` maps `PredecessorTitles = dto.PredecessorTitles` (line 61)
- `StubAdoService.CreateWorkItemBatchAsync` maps `PredecessorTitles = dto.PredecessorTitles` (line 84)

Predecessor data set by `ArtifactGenerationService` now flows through the full chain: AI response → DTO → `WorkItemRecord`. The data is present when `AdoCreationService`'s title→ID resolution loop (spec §6) runs in WI-7.

---

## Full DTO→Entity Field Tracing (Final State)

| DTO Field | WorkItemRecord Field | StubAdoService maps it? | Notes |
|---|---|---|---|
| `WiTemplate` | `WiTemplate` ✅ | ✅ both methods | No change from C1 |
| `IsExternalDependency` | `IsExternalDependency` ✅ | ✅ both methods | No change from C1 |
| `ExternalOwner` | `ExternalOwner` ✅ | ✅ both methods | No change from C1 |
| `TestedByTitles` | `TestedByTitles` ✅ | ✅ both methods | No change from C1 |
| `ParentTitle` | `ParentTitle` ✅ | ✅ both methods | **Fixed in C2** |
| `PredecessorTitles` | `PredecessorTitles` ✅ | ✅ both methods | **Fixed in C2** |
| — | `ExternalDependencyCount` | ✅ computed in Batch | In-memory, Phase 1 by-design |

6 of 6 classification fields flow correctly end-to-end. C1+C2 are fully closed.

---

## Positive Observations

- **Clean commit scope.** Tony touched exactly the 7 files needed — entity, EF config, migration (2 files), snapshot, DTO, and service. No collateral changes.
- **EF config and migration are in sync.** `HasMaxLength(500)` in EF → `varchar(500)` in migration; nullable both ways. No column drift.
- **Both methods covered.** In both C1 and C2, Tony correctly updated BOTH `CreateWorkItemAsync` and `CreateWorkItemBatchAsync`. The cycle 1 issue was precisely that the mapping was missing — it's now present in both paths.
- **Regression-free.** All 5 cycle 1 passing fields still map correctly. Tony didn't disturb the existing wiring.

---

*Review by Hawkeye — 2026-04-28. CC invoked per mandatory review protocol.*
