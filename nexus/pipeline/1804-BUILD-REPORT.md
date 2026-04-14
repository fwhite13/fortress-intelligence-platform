# Build Report — ADO #1804

## What was built
Fixed the "normal flow" path in `HandleSubmit()` of `NewSpecWizard.razor` to call `SpecGenerationService.GenerateAsync()` before navigating, so new submissions trigger spec generation instead of landing in Draft status with no spec.

## Files changed
- `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` — replaced the bare `Nav.NavigateTo(...)` at the bottom of `HandleSubmit()` with a try/catch that: (1) sets status to `Generating`, (2) calls `GenerateAsync`, (3) catches failures and sets status to `Failed` with snackbar error, then navigates on success.

## Parallelization used
No — single-file change.

## CC sessions run
1 — CC Sonnet, single-shot. Executed cleanly with commit `e2482a5`.

## Acceptance criteria verification
- [x] Normal flow path now calls `UpdateStatusAsync(Generating)` + `GenerateAsync` before `NavigateTo` — confirmed in file at lines 676–691
- [x] Skip-regen path (`if (_isResume && !_hasChanges && _existingSpecDocument != null)`) intact and unchanged — confirmed at lines 667–674
- [x] `SubmissionStatus.Generating` enum value exists — confirmed in `Models/Enums/SubmissionStatus.cs`
- [x] `dotnet build` — 0 errors, 0 warnings

## Known edge cases / things Clint should scrutinize
- The new catch block sets `_isSubmitting = false` and returns — consistent with the second-pass catch pattern above it.
- `GenerateAsync` sets status to `AwaitingReview` internally (per existing comment pattern in the file) — no double-set on success path.
- The outer `catch (Exception ex)` for `HandleSubmit` as a whole is still intact at the very end for any unhandled throws before the new try/catch.

## How to test locally
1. Create a new submission through the wizard and click Submit on the final step.
2. Verify the detail page shows `Generating` status (not `Draft`).
3. Verify spec generation completes and status transitions to `AwaitingReview`.
4. Test resume path with an existing spec + no changes — should still skip regen and go to `AwaitingReview` directly.

## Commit
`e2482a5` — `fix(nexus#1804): call GenerateAsync in HandleSubmit normal flow path`

---

## Cycle 2 — Nested try/catch for UpdateStatusAsync(Failed)

### What was built
Wrapped both unguarded `UpdateStatusAsync(Failed)` calls in `HandleSubmit()` with nested try/catch blocks. If the status reset itself throws, the submission no longer gets stuck permanently in `Generating`.

### Files changed
- `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` — two catch blocks patched:
  1. Regen path (~line 649): `UpdateStatusAsync(Failed)` wrapped; `Console.Error.WriteLine` fallback logs the secondary exception.
  2. New submission path (~line 683): same treatment — `UpdateStatusAsync(Failed)` wrapped with `Console.Error.WriteLine` fallback.

### Logger note
No `ILogger` / `_logger` is injected in this Razor component. Used `Console.Error.WriteLine` as the fallback per spec guidance. Silent swallow was not chosen — at minimum the error goes to stderr where it will appear in CloudWatch.

### Parallelization used
No — single-file, single catch.

### CC sessions run
1 — CC Sonnet, single-shot. Both fixes applied cleanly.

### Acceptance criteria verification
- [x] Both `UpdateStatusAsync(Failed)` calls wrapped in nested try/catch — confirmed at lines 652–659 (regen) and 693–700 (new submission)
- [x] `Console.Error.WriteLine` used for secondary exception logging (no ILogger injection needed)
- [x] `_regenInProgress`, `_isSubmitting`, `StateHasChanged()`, `return` order unchanged in both blocks
- [x] `dotnet build` — 0 errors, 0 warnings

### Known edge cases / things Clint should scrutinize
- Both `Console.Error.WriteLine` fallbacks are fire-and-forget — the user gets the primary snackbar error and the UI resets correctly regardless of whether the status reset succeeded.
- ILogger injection would be cleaner; deferred to avoid scope creep on a low-risk targeted fix.

### How to test locally
1. Simulate `UpdateStatusAsync` throwing (e.g., mock exception in dev) after a `GenerateAsync` failure.
2. Verify the UI resets (`_isSubmitting = false`, snackbar shown), submission does NOT stay stuck in `Generating`.
3. Verify `Console.Error.WriteLine` output appears in stderr/CloudWatch with the submission ID.

### Commit
`93181bc` — `fix(nexus#1804): wrap UpdateStatusAsync(Failed) in nested try/catch — both regen and new-submission paths`
