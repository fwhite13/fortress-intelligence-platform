# Build Report — NEXUS Phase 3 WIs #1650-#1658

---

## Cycle 2 — 2026-04-08

**Commit:** `01934922d60406c6fa1dbd3290c2e391fcef68dc`  
**Branch:** main  
**Verdict:** NEEDS-CHANGES (from Cycle 1 review) → fixed

### What was built

3 reviewer-requested fixes across `ISubmissionService`, `SubmissionService`, and `SubmissionDetail.razor`:

1. **Server-side ownership guard on `DeleteSubmissionAsync`** (WI #1651 — Critical)  
   Changed signature to `(int id, string callerUpn, bool callerIsAdmin)`. Added pre-flight check:
   - 404 if submission not found  
   - 400 if status is not Draft  
   - 401 if caller is not admin and is not the submitter  
   Full-graph load only proceeds after auth passes.

2. **Fixed `_historicalSpecs` filter** (WI #1652 — Important)  
   Replaced `.Skip(1)` (position-based, fragile) with `.Where(d => d.Id != _submission.ActiveSpecDocumentId)` (ID-based, correct).  
   Edge case decision: if `ActiveSpecDocumentId` is null, all spec docs appear in Version History accordion — correct behavior since no active spec means nothing to exclude.

3. **Try/catch in `HandleDeleteSubmissionAsync`** (WI #1651 — Important)  
   Wrapped delete call with targeted `UnauthorizedAccessException` handler (user-visible Snackbar error) and general `Exception` handler (Logger + generic Snackbar). Added `@inject ILogger<SubmissionDetail> Logger` to component.

### Files changed

- `src/FortressNexus.Web/Services/ISubmissionService.cs` — Updated `DeleteSubmissionAsync` signature
- `src/FortressNexus.Web/Services/SubmissionService.cs` — Added ownership/status guard, updated signature
- `src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor` — Logger inject, `_historicalSpecs` filter fix, `HandleDeleteSubmissionAsync` try/catch

### Build result

`dotnet build` — **0 errors, 0 warnings**

### CC sessions

1 CC session (Sonnet), sequential (all 3 fixes touch overlapping files, serialized)

### Acceptance criteria

- [x] `ISubmissionService.DeleteSubmissionAsync` signature updated with `callerUpn` + `callerIsAdmin`
- [x] `SubmissionService` guard throws `KeyNotFoundException`, `InvalidOperationException`, `UnauthorizedAccessException` as specified
- [x] `_historicalSpecs` uses ID comparison, not `.Skip(1)`
- [x] Null `ActiveSpecDocumentId` edge case handled (all docs shown as historical)
- [x] `HandleDeleteSubmissionAsync` has try/catch with user-visible Snackbar feedback
- [x] `ILogger<SubmissionDetail>` injected
- [x] `dotnet build` — 0 errors
- [x] Committed: `01934922`

### Known edge cases / things Clint should scrutinize

- The guard does two DB hits (FindAsync for auth, then full Include for deletion). This is intentional — auth check on a lightweight record before paying the cost of the full graph load. Acceptable trade-off.
- `HandleDeleteSubmissionAsync` uses `_submission!.Id` (null-forgiving) — safe because the delete button is only rendered when `_submission is not null`.

### How to test locally

1. Log in as non-owner, navigate to a Draft submission you don't own → Delete button should not render (UI guard intact)
2. If you bypass UI and call service directly as non-owner → `UnauthorizedAccessException`
3. Try deleting a non-Draft submission → `InvalidOperationException`
4. Delete a Draft you own → navigates to `/nexus`, success Snackbar
5. Check Version History with active spec pointing to a non-latest version → active spec excluded, all older versions shown

---

## Cycle 3 — 2026-04-08

**Commit:** `655ef24`  
**Branch:** main  
**Verdict:** NEEDS-CHANGES (from Cycle 2 review) → fixed

### What was built

2 reviewer-requested one-liner fixes in `SubmissionDetail.razor`:

1. **Swapped `Snackbar.Add` / `Nav.NavigateTo` order in delete success path** (WI #1651)  
   `Nav.NavigateTo("/nexus")` was called before `Snackbar.Add("Submission deleted.")`. In Blazor Server, navigation tears down the component before the Snackbar renders — toast never showed. Fixed by calling `Snackbar.Add` first, then `Nav.NavigateTo`.

2. **Replaced remaining inline `Skip(1)` in "Previous Version" expansion panel** (WI #1652)  
   The `_historicalSpecs` fix in Cycle 2 was applied to `LoadSubmissionAsync` but the inline expression at line ~151 (Generating state "Previous Version" panel) was missed. Replaced `_submission.SpecDocuments.OrderByDescending(d => d.Version).Skip(1).FirstOrDefault()` with `_historicalSpecs.FirstOrDefault()`. `_historicalSpecs` is already populated as all specs excluding the active one, ordered descending — `.FirstOrDefault()` is correct and simpler.

### Files changed

- `src/FortressNexus.Web/Components/Pages/SubmissionDetail.razor` — 2 line swaps, no other changes

### Build result

`dotnet build` — **0 errors, 0 warnings**

### CC sessions

1 CC session (Sonnet), 2 surgical fixes in a single pass

### Acceptance criteria

- [x] `Snackbar.Add` fires before `Nav.NavigateTo` in delete success path
- [x] Inline `Skip(1)` in Generating state "Previous Version" panel replaced with `_historicalSpecs.FirstOrDefault()`
- [x] `dotnet build` — 0 errors
- [x] Committed: `655ef24`

### Known edge cases / things Clint should scrutinize

- CC used `_historicalSpecs.FirstOrDefault()` (simpler) rather than the explicit `_specDocuments.Where(...)` pattern from the brief. Both are equivalent since `_historicalSpecs` is already filtered and ordered. Confirmed correct by reviewing field population in `LoadSubmissionAsync`.

---
