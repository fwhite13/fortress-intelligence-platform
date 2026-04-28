# Review Report — ADO#2498

## Verdict: NEEDS-CHANGES

**Cycle:** 1 of 2
**Reviewer:** Hawkeye (Clint Barton)
**Commit reviewed:** `d4c0656`
**Date:** 2026-04-28

---

## CC Review Summary

Review executed via CC with adversarial brief (`/tmp/review-2498-brief.md`):

```bash
cd /home/fredw/projects/fip/nexus && cat /tmp/review-2498-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC read and analyzed all 5 files (AdoWorkItemDto.cs, ArtifactGenerationService.cs, StubAdoService.cs, WorkItemRecord.cs, ArtifactSet.cs) and answered 12 adversarial questions with code excerpts. Results synthesized below.

CC findings that are real issues: Q8 (two failures), confirmed by direct inspection.
CC findings investigated and resolved as by-design: Q3 (classification on all WI types), Q9/Q10 (no DB persistence in Phase 1 stub) — see analysis below.

---

## Spec Compliance Check

**Spec:** `memory/projects/nexus-decomp-upgrade-spec-2026-04-27.md`

**§5 Codebase Map:**
- `AdoWorkItemDto.cs` — ✅ modified as specified (classification fields added)
- `ArtifactGenerationService.cs` — ✅ modified as specified (IWiClassifier injection + classification loop + TC generation)
- `StubAdoService.cs` — ✅ modified as specified (DTO→WorkItemRecord mapping + ExternalDependencyCount)

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected. Tony touched only the 3 files scoped to this WI.

**§7 Acceptance Criteria (from PLAN):**
- [x] **AC1:** IWiClassifier injected via constructor ✅ Verified
- [x] **AC2:** Classification on ALL WI types ✅ Verified (plan explicitly requires this)
- [x] **AC3:** Test Case generation for qualifying User Stories ✅ Verified
- [x] **AC4:** AC parsing: checkbox + numbered + newline fallback ✅ Verified
- [x] **AC5:** TestedByTitles populated on parent story ✅ Verified
- [x] **AC6:** ExternalDependencyCount set on ArtifactSet ✅ Verified
- [ ] **AC7:** Existing behavior unchanged — ❌ PARTIAL: two mapping gaps found (see Critical Issues)

**Spec compliance verdict:** ❌ NON-COMPLIANT — two issues in the DTO→WorkItemRecord mapping path block full pass

---

## Consistency Audit

**Files Cross-Referenced:**

| DTO Field | WorkItemRecord Field | StubAdoService maps it? |
|---|---|---|
| `WiTemplate` | `WiTemplate` ✅ present | ✅ mapped |
| `IsExternalDependency` | `IsExternalDependency` ✅ present | ✅ mapped |
| `ExternalOwner` | `ExternalOwner` ✅ present | ✅ mapped |
| `TestedByTitles` | `TestedByTitles` ✅ present | ✅ mapped |
| `ParentTitle` | ❌ NOT present in WorkItemRecord | ❌ unmapped |
| `PredecessorTitles` | `PredecessorTitles` ✅ present | ❌ NOT mapped |

**Key finding:** The DTO-pipeline approach works cleanly for 4 of 6 new fields. Two fields fall through the floor at the entity boundary.

---

## Critical Issues [2]

### C1: `WorkItemRecord` entity missing `ParentTitle`

- **File:** `src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs`
- **Category:** consistency
- **Issue:** `AdoWorkItemDto` has `public string? ParentTitle { get; set; }` (line 12). `WorkItemRecord` has no `ParentTitle` property at all. The Test Case → parent Story link is lost at the entity boundary. When Test Case `WorkItemRecord`s are eventually persisted (WI-7), there will be no column to store the parent reference.
- **Evidence:**
  ```bash
  # DTO has it:
  # AdoWorkItemDto.cs:12  public string? ParentTitle { get; set; }
  
  # WorkItemRecord has no ParentTitle — grep returns nothing:
  grep -n "ParentTitle" src/FortressNexus.Web/Models/Entities/WorkItemRecord.cs
  # (no output)
  ```
- **Impact:** Test Cases are orphaned at the DB layer. The UI grouping (TC under parent Story) cannot work without this field. The EF migration from ADO#2497 didn't include it, meaning it must be added now before WI-7 (the persistence WI) to avoid a schema gap.
- **Fix:**
  ```diff
  // WorkItemRecord.cs — add after TestedByTitles:
  +    // Test Case parent link — populated for WiType == "Test Case"
  +    public string? ParentTitle { get; set; }
  ```
  Also requires a migration update. Add the column to `AddDecompositionUpgradeFields_20260427` or create a new migration `AddWorkItemRecordParentTitle`.

  And in StubAdoService, add mapping in both `CreateWorkItemAsync` and `CreateWorkItemBatchAsync`:
  ```diff
  -    TestedByTitles = dto.TestedByTitles
  +    TestedByTitles = dto.TestedByTitles,
  +    ParentTitle = dto.ParentTitle
  ```

---

### C2: `PredecessorTitles` not mapped in StubAdoService DTO→WorkItemRecord

- **File:** `src/FortressNexus.Web/Services/StubAdoService.cs`
- **Category:** consistency
- **Issue:** `WorkItemRecord` has `PredecessorTitles` (line 17, `List<string>?`). `AdoWorkItemDto` has `PredecessorTitles` (Tony's plan step says to map all fields). But `StubAdoService.CreateWorkItemBatchAsync` and `CreateWorkItemAsync` both omit this mapping — the field is silently left `null` for all records, including WIs that have predecessors set by the AI.
- **Evidence:**
  ```bash
  # WorkItemRecord has it:
  # WorkItemRecord.cs:17  public List<string>? PredecessorTitles { get; set; }
  
  # StubAdoService grep shows NO PredecessorTitles assignment:
  grep -n "PredecessorTitles" src/FortressNexus.Web/Services/StubAdoService.cs
  # (no output)
  ```
- **Impact:** Predecessor data from the AI response flows through `ArtifactGenerationService` and into the DTO, but is silently discarded at the `StubAdoService` mapping layer. When WI-7 enables real persistence, all `predecessor_titles` columns will be `NULL` even for WIs that should have them. `AdoCreationService`'s title→ID resolution loop (spec §6) depends on this data surviving.
- **Fix:**
  ```diff
  // StubAdoService — both CreateWorkItemAsync and CreateWorkItemBatchAsync:
  -    TestedByTitles = dto.TestedByTitles
  +    TestedByTitles = dto.TestedByTitles,
  +    PredecessorTitles = dto.PredecessorTitles
  ```

---

## Important Issues [0]

None beyond the Criticals.

---

## Nitpicks [1]

### N1: No `ParentTitle` in `AdoWorkItemDto` constructor docs

`AdoWorkItemDto.cs` lists `ParentTitle` as a field but the build report's AC7 checklist doesn't mention it was added. Minor documentation gap — not blocking.

---

## Positive Observations

- **IWiClassifier injection is clean.** Constructor pattern is correct. No `new WiClassifierService()` anywhere. PASS.
- **AC parsing is thorough.** Compiled regexes, three patterns (checkbox/numbered/newline fallback), correctly ordered. This is good defensive code.
- **TestedByTitles loop is correct.** Tony accumulated TC titles in `tcTitles` and assigned `story.TestedByTitles = tcTitles` after the inner loop — exactly right.
- **Spec compliance on service split is clean.** All classification logic is in `ArtifactGenerationService`. `StubAdoService` is pure mapping with zero classifier calls. Spec §5 boundary maintained.
- **Q3 (classification on all WI types) is by design.** Plan step 3 explicitly says "Apply to ALL WI types" and notes the classifier "short-circuits gracefully for non-story types." Not a bug.
- **DB persistence absence (Q9/Q10) is by design.** `StubAdoService` is the Phase 1 stub — no DbContext, returns in-memory records only. WI-7 handles real persistence. ExternalDependencyCount is computed correctly in-memory for when the caller eventually persists the ArtifactSet.

---

## What Tony Must Fix

**Fix 1 — Add `ParentTitle` to `WorkItemRecord`**
```csharp
// WorkItemRecord.cs — after TestedByTitles property
// Test Case parent link — populated for WiType == "Test Case"
public string? ParentTitle { get; set; }
```
Requires a migration. Add column to existing migration or create a new one.

**Fix 2 — Map `PredecessorTitles` in StubAdoService**
In both `CreateWorkItemAsync` AND `CreateWorkItemBatchAsync`, add:
```csharp
PredecessorTitles = dto.PredecessorTitles,
```
(alongside the existing TestedByTitles mapping on the same line)

**Also required:** Map `ParentTitle` in the same two methods:
```csharp
ParentTitle = dto.ParentTitle,
```

Both fixes are mechanical — the plumbing is there, the wire just needs to be connected.

---

## Classification Data Reaching DB — Explicit Confirmation

| Field | DTO→Record Path | Status |
|---|---|---|
| `WiTemplate` | ArtifactGenerationService → DTO → StubAdoService → WorkItemRecord | ✅ Reaches entity |
| `IsExternalDependency` | ArtifactGenerationService → DTO → StubAdoService → WorkItemRecord | ✅ Reaches entity |
| `ExternalOwner` | ArtifactGenerationService → DTO → StubAdoService → WorkItemRecord | ✅ Reaches entity |
| `TestedByTitles` | ArtifactGenerationService → DTO → StubAdoService → WorkItemRecord | ✅ Reaches entity |
| `ParentTitle` (TC) | ArtifactGenerationService → DTO → **dropped at StubAdoService** | ❌ Lost |
| `PredecessorTitles` | ArtifactGenerationService → DTO → **dropped at StubAdoService** | ❌ Lost |
| `ExternalDependencyCount` | StubAdoService → ArtifactSet (in-memory) | ✅ Computed correctly (Phase 1 in-memory only) |

4 of 6 new fields flow correctly end-to-end. 2 fall through at the StubAdoService mapping layer.
