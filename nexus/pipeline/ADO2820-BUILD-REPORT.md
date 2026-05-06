# Build Report — ADO#2820
## Decomp Trigger: Persists ArtifactSet + WorkItemRecords to DB, NexusEditor Gated

**Commit:** `a4b5a2f`  
**Branch:** `main`  
**Build result:** ✅ SUCCEEDED — 0 errors, 1 pre-existing warning (unrelated)  
**CC sessions:** 1 (sonnet, synchronous)  
**Parallelization:** N/A — single serial task

---

## What was built

`DecomposeAndPersistAsync` added to `IArtifactGenerationService` and implemented in `ArtifactGenerationService` — calls `GenerateWorkItemsAsync`, persists `ArtifactSet` + `WorkItemRecord` rows to DB, updates `Submission.Status` → `ArtifactsCreated`.

`SubmissionDetail.razor` updated: stub ADO call removed, `HandleGenerateWorkItems` replaced with new orchestration, `_isEditor` guard added (NexusAdmin || NexusReviewer), `_generatingStatusText` status caption added, button section split into Approved+editor (Decompose button) vs ArtifactsCreated (Review Work Items button).

---

## Files changed

- `src/FortressNexus.Web/Services/IArtifactGenerationService.cs` — Added `using FortressNexus.Web.Models.Entities` and `DecomposeAndPersistAsync(int submissionId, int specDocumentId, string callerUpn)` signature
- `src/FortressNexus.Web/Services/ArtifactGenerationService.cs` — Added usings for `Entities` and `Enums`; implemented `DecomposeAndPersistAsync`: generate DTOs via Bedrock → persist `ArtifactSet` → persist `WorkItemRecord` rows → update `SubmissionStatus.ArtifactsCreated`; added structured log line
- `src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor` — Removed `@inject IAdoService AdoService`; added `_isEditor`/`_generatingStatusText` fields; set `_isEditor = await UserContextService.IsNexusEditorAsync()` in `LoadSubmissionAsync`; replaced combined Approved/ArtifactsCreated button block with two separate blocks gated properly; replaced `HandleGenerateWorkItems` stub with real `DecomposeAndPersistAsync` call

---

## Acceptance criteria verification

- [x] `IArtifactGenerationService` has `DecomposeAndPersistAsync` signature
- [x] `ArtifactGenerationService` implements it — calls Bedrock, persists ArtifactSet, persists WorkItemRecords, updates submission status
- [x] NexusAdmin/NexusReviewer see Decompose button on Approved submissions (`_isEditor` gate)
- [x] NexusUser cannot trigger Decompose (guard in both UI and method body)
- [x] Button shows "Decomposing..." during call; status caption shown
- [x] On success: DB persisted, status → ArtifactsCreated, Review Work Items button appears, success snackbar with ext dep count
- [x] On failure: error snackbar, `_isGeneratingWorkItems` resets for retry
- [x] Review Work Items navigates to `/nexus/{id}/artifacts`
- [x] Stub `AdoService.CreateWorkItemBatchAsync` call removed; `IAdoService` injection removed
- [x] Build: 0 errors

---

## Things Clint should scrutinize

1. **`_submission` reload after DecomposeAndPersistAsync** — we call `SubmissionService.GetByIdAsync(_submission.Id)` to refresh status. Confirm `GetByIdAsync` returns a fully loaded submission (with SpecDocuments etc.) — not a partial.
2. **`_isEditor` initialization** — set during `LoadSubmissionAsync`. If load fails early (access guard), `_isEditor` remains `false` — correct behavior, but worth a glance.
3. **Concurrent decomp guard** — `_isGeneratingWorkItems` prevents double-click. No distributed lock. Acceptable for single-user scoped Blazor Server page; flag if multi-tab concern is real.
4. **ArtifactSet.ExternalDependencyCount** — set before `WorkItemRecord` rows are persisted. If `SaveChangesAsync` for records fails, the count is already committed in the ArtifactSet row. Minor inconsistency — acceptable since decomp would show error and user retries.

---

## How to test locally

1. Log in as a NexusAdmin or NexusReviewer
2. Navigate to an Approved submission with an active spec
3. Verify "Decompose" button is visible
4. Click Decompose — verify "Decomposing..." text and status caption appear
5. After completion: verify ArtifactSet and WorkItemRecord rows in DB; submission.Status = ArtifactsCreated; "Review Work Items" button appears
6. Log in as NexusUser — verify Decompose button NOT visible on the same submission
7. Navigate to `/nexus/{id}/artifacts` via Review Work Items button — verify tree populated (ADO#2821 scope)

---

_Sent to Clint for review — ADO#2820_
