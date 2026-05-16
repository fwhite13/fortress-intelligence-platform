# NEXUS: Incremental Artifact Decomposition

**Status:** Implementation-ready  
**Author:** Jarvis  
**Date:** 2026-05-15

---

## Problem

The current decomposition pipeline is all-or-nothing. `DecomposeAndPersistAsync` calls `GenerateWorkItemsAsync`, waits for the full result (1A skeleton + all 1B enrichment batches + Call 2 TC scan), then writes everything to the DB in one shot. If anything fails mid-way — including the SigV4 expiry we just fixed — you get zero artifacts and the submission stays at `Approved` with the Decompose button available again.

For large specs (181 items = 8 enrichment batches), the user stares at a spinner for 10+ minutes with no feedback and no safety net.

---

## Goal

Persist artifacts incrementally so partial progress is never lost and the user sees feedback as work completes.

---

## Design

### New `SubmissionStatus` value: `Decomposing`

Add `Decomposing` to the `SubmissionStatus` enum (between `Approved` and `ArtifactsCreated`). The submission flips to `Decomposing` immediately when Call 1A completes and the skeleton is persisted. It stays there through all 1B batches and the TC scan, then flips to `ArtifactsCreated` at the end.

This means:
- The Decompose button is hidden while status is `Decomposing` (prevents double-runs)
- The UI can show a progress indicator based on `ArtifactSet.Status` / work item count
- If the process crashes mid-1B, the submission stays `Decomposing` with partial data visible — admin can inspect or reset

### New `ArtifactSetStatus` value: `Enriching`

`ArtifactSet.Status` already has `Pending / InProgress / Success / PartialFailure / Failed`. Add `Enriching` (after skeleton is persisted, before enrichment completes). Sequence: `Pending` → `InProgress` (after 1A) → `Enriching` (after skeleton persisted, during 1B) → `Success` (after TC scan).

### `WorkItemRecord`: add `IsEnriched` flag (bool, default false)

Skeleton items are persisted with `IsEnriched = false`. After each 1B batch, the corresponding records are updated in-place with description/AC/etc. and `IsEnriched = true`. This lets the UI distinguish stub vs enriched items and gives a clear progress signal (`N of M items enriched`).

---

## Implementation Plan

### 1. DB schema changes (EF Core migration)

**`SubmissionStatus` enum** — add `Decomposing = 7` (append to end, don't renumber)

**`ArtifactSetStatus` enum** — add `Enriching = 5` (append to end)

**`WorkItemRecord`** — add `bool IsEnriched` column (default `false`)

Create and apply migration: `AddIncrementalDecompFields`

### 2. Refactor `ArtifactGenerationService.cs`

Move DB persistence out of `DecomposeAndPersistAsync` and into `GenerateWorkItemsAsync` incrementally. The method signature change: `GenerateWorkItemsAsync` needs access to the DB and the submission/artifactSet context. 

**Option A (preferred):** Collapse `GenerateWorkItemsAsync` + `DecomposeAndPersistAsync` into a single method `DecomposeAndPersistAsync` that owns the full flow and does incremental saves. Remove the now-redundant `GenerateWorkItemsAsync` from the interface (or keep it as a thin wrapper returning the final list for test compatibility — see below).

**New flow in `DecomposeAndPersistAsync`:**

```
1. Set submission.Status = Decomposing → SaveChanges
2. Create ArtifactSet (Status=InProgress) → SaveChanges  [get artifactSet.Id]
3. Call 1A (skeleton)
4. Map skeleton DTOs → WorkItemRecords with IsEnriched=false
5. Bulk insert WorkItemRecords → SaveChangesAsync
6. Set artifactSet.Status = Enriching → SaveChanges
7. For each 1B batch (25 items):
   a. Call Bedrock enrichment for batch
   b. For each enriched DTO, find matching WorkItemRecord by Title + ArtifactSetId
   c. Update Description, AcceptanceCriteria, DeveloperBrief, WiTemplate, IsExternalDependency, ExternalOwner
   d. Set IsEnriched = true
   e. SaveChangesAsync   ← persist after every batch
8. Run SanitizePersonNames on all records (reload from DB or track in memory)
9. Classify WiTemplate + IsExternalDependency on any remaining unenriched items
10. Call 2 (TC scan) — on the full enriched set
11. Persist TC WorkItemRecords with IsEnriched=true
12. Update artifactSet: Status=Success, ExternalDependencyCount=N → SaveChanges
13. Set submission.Status = ArtifactsCreated → SaveChanges
14. Log [DECOMP_PERSIST] summary
15. Return artifactSet
```

**Error handling:**
- If 1A throws: set submission.Status = Approved (rollback to allow retry), artifactSet.Status = Failed, log error, rethrow
- If a 1B batch throws: log warning, continue with remaining batches (partial enrichment is acceptable — IsEnriched=false items are visible as stubs)
- If all 1B batches fail: set artifactSet.Status = PartialFailure, submission.Status = ArtifactsCreated (still navigable — user has skeleton)
- If TC scan throws: already wrapped in try/catch, continue without TCs (existing behavior)

### 3. Title-matching for 1B batch updates

The 1B enrichment returns a JSON array in the same order as the batch input. Match returned items back to `WorkItemRecord` rows by **index within the batch** (reliable, since we control the input and the model is instructed not to add/remove/reorder). Fallback: match by title if index is out of range.

Store the batch's `WorkItemRecord.Id` list before the Bedrock call so you can zip them to the response without a DB round-trip.

### 4. UI changes (`SubmissionDetail.razor`)

- Add `Decomposing` to `GetStatusColor` → `Color.Warning` (same as `Generating`)
- Hide the Decompose button when `status == Decomposing` (already only shown for `Approved` — adding `Decomposing` exclusion is implicit, but add an explicit guard)
- Update the spinner/status text: while `_isGeneratingWorkItems`, poll the DB every 5s and show `"Decomposing: N of M items enriched"` based on `IsEnriched` count vs total. Use a `CancellationToken` tied to component disposal.
- Add `Decomposing` to the status chip color switch

### 5. `IArtifactGenerationService` interface

`GenerateWorkItemsAsync(int specDocumentId)` — keep it on the interface but it can now be a thin wrapper that calls the internal pipeline and returns the full DTO list (used by tests). The interface change to `DecomposeAndPersistAsync` signature doesn't change (already takes `submissionId, specDocumentId, callerUpn, adoProjectName`).

---

## Files to change

| File | Change |
|------|--------|
| `Models/Enums/SubmissionStatus.cs` | Add `Decomposing = 7` |
| `Models/Enums/ArtifactSetStatus.cs` | Add `Enriching = 5` |
| `Models/Entities/WorkItemRecord.cs` | Add `bool IsEnriched { get; set; }` |
| `Services/ArtifactGenerationService.cs` | Full refactor of persist flow (see above) |
| `Components/Pages/SubmissionDetail.razor` | Status chip, Decompose button guard, progress text |
| `Migrations/` | New migration `AddIncrementalDecompFields` |

---

## Constraints

- Do NOT modify `BedrockService.cs`
- Do NOT modify the JSON output schema (TC scan output, work item DTO shape)
- Do NOT modify the TC scan (Call 2) logic — only move when it executes
- `EnrichBatchSize = 25` constant stays; do not hardcode
- All model IDs remain env-var overridable via `FortressAI:ModelId` — no LiteLLM aliases
- EF migration must be additive only — no column renames or drops
- Build must pass with 0 errors before committing

---

## Acceptance Criteria

1. Decompose button disappears immediately when decompose is triggered (status flips to `Decomposing`)
2. After Call 1A, skeleton work items are visible in the DB (stub rows with `IsEnriched=false`)
3. After each 1B batch, the corresponding rows are updated and `IsEnriched=true`
4. If the process is interrupted after 1B batch 3 of 8, the submission stays `Decomposing` with 75 enriched + 106 stub records visible
5. A successful full run ends with `submission.Status = ArtifactsCreated` and all work items `IsEnriched=true` (except any unenriched stubs if a batch silently failed)
6. Build passes, migration applies cleanly, no regressions on existing decomp flow
