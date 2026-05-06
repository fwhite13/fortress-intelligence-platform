# Review Report — ADO#2826

**Commit:** `84442254`
**Verdict:** NEEDS-CHANGES

---

## Spec Compliance Check

**What was built:** Defense-in-depth ownership guards on `UpdateStatusAsync`, `UpdateNarrativeAsync`, `SetActiveSpecDocumentAsync` — each takes `string callerUpn, bool isAdmin = false` and throws `UnauthorizedAccessException` if `!isAdmin && SubmittedBy != callerUpn`. All Wizard call sites updated.

**Files changed:** `ISubmissionService.cs`, `SubmissionService.cs`, `NewSpecWizard.razor` — correct scope.

**Spec compliance verdict:** ✅ COMPLIANT on scope and intent — one implementation gap found (see I1).

---

## CC Review Summary

CC ran adversarial checks across all 10 targeted verifications. 9/10 passed clean. One confirmed Important bug found: `isAdmin` resolved in `OnInitializedAsync` is not stored as a component field and therefore not propagated to the guarded service calls in `HandleSubmit` and `ApplyResumeChangesAsync`. Every other aspect — guard pattern correctness, null safety, call site completeness, SetActiveSpecDocumentAsync caller sweep, SpecGenerationService bypass confirmation, build clean — is verified correct.

---

## Consistency Audit

**Files cross-referenced:**
- `ISubmissionService.cs` ↔ `SubmissionService.cs` — ✅ all 3 new signatures match exactly
- `NewSpecWizard.razor` (8 call sites) ↔ `ISubmissionService.cs` — ✅ all updated with positional `upn`
- `SubmissionDetail.razor` → `Nav.NavigateTo($"/nexus/{Id}/resume")` — ⚠ admin reaches Resume Wizard; `_isAdmin` used in SubmissionDetail but NOT stored in NewSpecWizard (see I1)
- `SubmissionService.UpdateStatusAsync` guard vs `DeleteSubmissionAsync` guard — ✅ functionally identical
- `SpecGenerationService.cs` — ✅ touches `submission.Status` and `ActiveSpecDocumentId` directly via EF; zero calls to any of the 3 guarded methods

**Undocumented dependencies found:**
- `SubmissionDetail.razor` line 171 renders a Resume button for admins (line 247: `_submission.SubmittedBy == _currentUserUpn || _isAdmin`). This makes the admin-resume path real and reachable, not theoretical.

---

## Critical Issues — 0

None.

---

## Important Issues — 1

### I1: `isAdmin` Not Propagated to Service Calls — Admin Resume Path Throws

**File:** `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor`

**Issue:** `OnInitializedAsync` correctly allows admins to resume other users' Draft submissions:
```csharp
var currentUpn = await UserContextService.GetUpnAsync();
var isAdmin = await UserContextService.IsAdminAsync();
if (!isAdmin && submission.SubmittedBy != currentUpn)
{
    Nav.NavigateTo("/nexus");
    return;
}
```

But `isAdmin` is a **local variable** — there is no `private bool _isAdmin;` component field. `HandleSubmit` and `ApplyResumeChangesAsync` later call:
```csharp
var upn = await UserContextService.GetUpnAsync();  // = admin's own UPN
await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Generating, upn);
// SubmissionService: !false && adminUpn != ownerUpn → throws UnauthorizedAccessException
```

Same problem in `ApplyResumeChangesAsync` → `UpdateNarrativeAsync`.

**Why it's reachable:** `SubmissionDetail.razor` line 247 renders the Resume button when `_submission.SubmittedBy == _currentUserUpn || _isAdmin`. An admin looking at any Draft submission sees the Resume button and can click it. They pass the Wizard's init check, proceed through all steps, click Submit, and then hit `UnauthorizedAccessException` mid-flow — leaving the submission in a partial state.

**Fix:**
```diff
// Add field (with other private fields):
+private bool _isAdmin;

// In OnInitializedAsync (already has local var — promote it):
-var isAdmin = await UserContextService.IsAdminAsync();
-if (!isAdmin && submission.SubmittedBy != currentUpn)
+_isAdmin = await UserContextService.IsAdminAsync();
+if (!_isAdmin && submission.SubmittedBy != currentUpn)

// In HandleSubmit — all 5 UpdateStatusAsync/UpdateNarrativeAsync calls:
-await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Generating, upn);
+await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Generating, upn, _isAdmin);

// Repeat for all 7 other call sites (same pattern: add , _isAdmin at end)
```

Total change: 1 new field + promote 1 local var + add `, _isAdmin` to 8 existing service calls.

---

## Nitpicks — 1

**N1:** `DeleteSubmissionAsync` uses parameter name `callerIsAdmin` while the 3 new methods use `isAdmin`. No functional impact — parameter names on interface methods don't affect callers. Not blocking.

---

## Positive Observations

- **SubmittedBy null safety handled correctly.** `SubmittedBy` is initialized to `""`, never null. The `!=` comparison is safe. Empty-string edge case behaves correctly (rejects access).
- **`SetActiveSpecDocumentAsync` sweep clean.** Zero active callers found outside the interface/implementation — prophylactic update is safe and correct.
- **All 8 call sites in NewSpecWizard.razor correctly updated.** `upn` fetched before first use in each method scope, positional passing correct throughout. The previously-missing `upn` in `ApplyResumeChangesAsync` is fixed.
- **SpecGenerationService correctly bypasses guards.** Direct EF mutation is the right approach for trusted server-side spec generation. No callerUpn needed there.
- **Build clean.** 0 errors, 1 pre-existing CS8601 warning in `FileStorageService.cs` (unrelated to this commit).

---

## What to Fix

**Only one file needs changes: `NewSpecWizard.razor`**

1. Add `private bool _isAdmin;` to the component fields block (near the other `private bool` fields, ~line 260).
2. In `OnInitializedAsync`, promote the local `isAdmin` variable to assign `_isAdmin`:
   ```csharp
   _isAdmin = await UserContextService.IsAdminAsync();
   if (!_isAdmin && submission.SubmittedBy != currentUpn)
   ```
3. In `HandleSubmit` and `ApplyResumeChangesAsync`, add `, _isAdmin` as the final argument to all 8 `SubmissionService` calls that accept `isAdmin`.

No service layer changes needed. No interface changes needed. Wizard-only fix, 8 call sites.

---

## Acceptance Criteria Verification

- [x] All 3 method signatures in `ISubmissionService` updated consistently ✅
- [x] Ownership guard pattern matches `DeleteSubmissionAsync` ✅
- [x] `SubmittedBy` null check not needed (non-nullable, default `""`) ✅
- [ ] All call sites pass correct `callerUpn` — ✅ UPN is correct, but **`isAdmin` not forwarded** — blocks full PASS
- [x] `SetActiveSpecDocumentAsync` — no callers confirmed ✅
- [x] `ArtifactGenerationService.DecomposeAndPersistAsync` bypasses service methods (direct EF) — confirmed ✅
- [x] `isAdmin = false` default compiles cleanly for all callers ✅
- [x] Build compiles clean (0 errors) ✅
