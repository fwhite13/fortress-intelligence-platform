# BUILD Assignment: ADO#2498 — Cycle 2 (Fix Only)

## Task
**Integrate IWiClassifier into ArtifactGenerationService — Review Cycle 2**
**ADO WI:** https://dev.azure.com/FortressAffinityGroup/4f20ca74-10f3-4707-b00b-04cd4e147909/_workitems/edit/2498

## Review result: NEEDS-CHANGES
Clint found 2 critical gaps in cycle 1. Fix ONLY these two issues — no scope creep.

---

## Fix 1 — C1: WorkItemRecord missing `ParentTitle` property

**Problem:** `AdoWorkItemDto` has `ParentTitle` and TC DTOs have it set correctly, but `WorkItemRecord.cs` has no `ParentTitle` property. The TC→parent Story link is lost at the entity boundary and never persisted.

**Fixes required:**

### 1a. Add `ParentTitle` to `Models/Entities/WorkItemRecord.cs`
```csharp
public string? ParentTitle { get; set; }
```

### 1b. Add EF column mapping in `Data/NexusDbContext.cs`
Follow the existing pattern for nullable string columns. Column name: `parent_title`, type: `VARCHAR(500) NULL` (titles can be long).

### 1c. Create a new EF Core migration
Migration name: `AddWorkItemRecordParentTitle`

This will add `parent_title VARCHAR(500) NULL` to `work_item_records`.

nexus-web runs migrations automatically on startup via `DatabaseInitializationService` — no manual apply needed, migration runs when the new image starts.

### 1d. Map in StubAdoService — BOTH methods
In `StubAdoService`, there are two places that create `WorkItemRecord` from DTO. In BOTH:
```csharp
record.ParentTitle = dto.ParentTitle;
```

Check whether `CreateWorkItemAsync` and `CreateWorkItemBatchAsync` both have mapping blocks — add to each.

---

## Fix 2 — C2: PredecessorTitles not mapped in StubAdoService

**Problem:** `WorkItemRecord.PredecessorTitles` exists (added in ADO#2497) and `AdoWorkItemDto` likely has `PredecessorTitles` too, but StubAdoService never maps `dto.PredecessorTitles → record.PredecessorTitles`. All predecessor data is silently dropped.

**Fix:** In BOTH `CreateWorkItemAsync` and `CreateWorkItemBatchAsync` in `StubAdoService`, add:
```csharp
record.PredecessorTitles = dto.PredecessorTitles;
```

Also check: does `AdoWorkItemDto` have a `PredecessorTitles` property? If not, add it:
```csharp
public List<string>? PredecessorTitles { get; set; }
```

And in `ArtifactGenerationService`, confirm the `predecessorTitles` field from the AI response JSON is being deserialized and set on the DTO. If the AI response model has this field, wire it. If it doesn't exist yet in the response model, add it as `List<string>?` with a null-safe default.

---

## What NOT to change
- Do not touch `ArtifactGenerationService.cs` classification logic — it passed review
- Do not touch `IWiClassifier` or `WiClassifierService`
- Do not change any other StubAdoService mappings that were passing
- Do not add new features beyond what's listed here

---

## ADO Updates (MANDATORY)
After fixing, add a comment to ADO WI #2498:
```
mcporter call devops.add_comment project="FAIT" id=2498 text="**[Tony Stark — BUILD cycle 2]**
Commit {hash}: fix C1 (WorkItemRecord.ParentTitle + migration) and C2 (PredecessorTitles mapping in StubAdoService). Build: SUCCEEDED."
```

## Build Report required
Append to or replace `/home/fredw/projects/fip/nexus/pipeline/ADO2498-BUILD-REPORT.md` with a cycle 2 section:
- Files modified
- New commit hash
- Build result
- CC invocation used
- Confirmation both C1 and C2 fixes are in place

## Notify Maria when done
When completely finished, run:
openclaw system event --text "ADO2498 BUILD C2 COMPLETE: ParentTitle + PredecessorTitles fixes" --mode now
