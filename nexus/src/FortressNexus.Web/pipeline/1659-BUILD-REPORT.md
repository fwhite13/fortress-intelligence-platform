# Build Report — WI #1659
## Spec Regen Path: New SpecDocument at Version+1

**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-04-08  
**Commit:** `d222b7d`  
**Build:** ✅ 0 errors, 0 warnings

---

## CC Invocation

```bash
cd ~/projects/fip/nexus/src/FortressNexus.Web && cat /tmp/tony-1659-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

Single CC session, sequential (two dependent files).

---

## Pre-Change State: Versioning in `GenerateAsync`

**`SpecGenerationService.GenerateAsync` hardcoded `Version = 1`: YES**

Prior to this WI, every new `SpecDocument` insert used `Version = 1` regardless of how many prior docs existed for the submission. `RegenerateAsync` computed `nextVersion` correctly but called `GenerateAsync` internally, which wrote `Version = 1` first — then `RegenerateAsync` patched it after the fact. Both the versioning fix and the `HandleSubmit` regen wire-up depend on `GenerateAsync` doing the right thing now.

---

## Files Modified

### `Services/SpecGenerationService.cs`
**What changed:** In `GenerateAsync`, replaced the hardcoded `Version = 1` in the `SpecDocument` initializer with a MAX+1 query computed before the insert:

```csharp
var nextVersion = await _db.SpecDocuments
    .Where(s => s.SubmissionId == submissionId)
    .Select(s => (int?)s.Version)
    .MaxAsync() ?? 0;
nextVersion += 1;
```

- First submission for a submission → `MAX` returns null → `nextVersion = 1` (correct default)
- Resume regen → `MAX` returns existing max → `nextVersion = maxExisting + 1` (no overwrite)
- Prior `SpecDocument` rows are never deleted or modified

### `Components/Pages/NewSpecWizard.razor`
Three changes:

1. **`@inject ISpecGenerationService SpecGenerationService`** — injected spec gen service into wizard
2. **`private bool _regenPending = false`** — added field to track whether the user has been sent through new discovery and is now on the second Submit (regen pass)
3. **`HandleSubmit` — `_isResume && _hasChanges` branch rewritten** — two-pass pattern:

   - **First pass** (`_regenPending == false`): Supersedes old session, initiates new discovery, sets `_regenPending = true`, navigates user back to Step 2. Returns early — no spec touched.
   - **Second pass** (`_regenPending == true`): Calls `SubmissionService.UpdateStatusAsync(Generating)`, then `SpecGenerationService.GenerateAsync(submissionId)` — which inserts a new `SpecDocument` at `Version = MAX+1` and sets status to `AwaitingReview` internally. On error: sets `Failed`, shows snackbar. On success: navigates to submission detail.
   - `// TODO(WI #1661)` comment placed at the regen call for future background job + progress UI.

---

## Acceptance Criteria Verification

- [x] `SpecDocument` inserted at `Version = MAX(existing)+1` — versioning query in `GenerateAsync` confirmed
- [x] Prior `SpecDocument` rows not deleted or modified — `GenerateAsync` only adds, never deletes
- [x] `HandleSubmit` resume+changes branch wired: first pass → re-discovery; second pass → `GenerateAsync`
- [x] `SubmissionStatus.Generating` / `SubmissionStatus.AwaitingReview` used (enum values, not raw strings)
- [x] `// TODO(WI #1661)` present in regen path
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] `SpecDocument.Id` is `int` — no Guid/char(36) changes made

---

## Known Edge Cases / Things Clint Should Scrutinize

1. **`GenerateAsync` sets `AwaitingReview` internally** — the `HandleSubmit` second-pass calls `UpdateStatusAsync(Generating)` first, then `GenerateAsync` sets `AwaitingReview` on success internally. The explicit pre-call status set is belt-and-suspenders (shows "Generating" state to any observers during the synchronous call). No double-save issue — they're separate `SaveChangesAsync` calls.

2. **`_regenPending` not persisted** — it's an in-memory flag on the Blazor component. If the user closes the tab mid-flow (after first pass, before second pass), `_regenPending` resets to `false` on re-load. The new discovery session is already initiated in the DB, so on reload the user lands fresh. This is acceptable for WI #1659 scope; WI #1661 (background job) will handle durable state.

3. **`RegenerateAsync` still has the version patch** — after this fix, `GenerateAsync` now returns a doc already at the correct version, so `RegenerateAsync`'s post-assignment (`newSpec.Version = nextVersion`) will overwrite the already-correct value with the same value (computed the same way). Harmless but redundant — left as-is to avoid scope creep; can be cleaned in a future WI.

---

## How to Test Locally

1. Create a new submission and confirm it generates a `SpecDocument` at `Version = 1`
2. Resume that submission, change the narrative
3. Hit Submit → wizard goes back to Discovery
4. Complete discovery → hit Submit again
5. Confirm: new `SpecDocument` row inserted at `Version = 2`, old row untouched
6. Confirm submission status transitions: `Generating` → `AwaitingReview`
