# Review Report — ADO#2821
**NEXUS Decomp Tree Editor — inline hierarchy editor**
**Reviewer:** Hawkeye (Clint Barton) | **Review Cycle:** 1 | **Date:** 2026-05-06

---

## Verdict: FAIL

**Reason:** Two independent critical defects block NexusReviewer role from using any editing functionality — at both the page view layer and the API layer. Tony's claim that "Reviewers can edit any submission" is factually contradicted by the code.

---

## CC Review Summary

CC review confirmed all findings. No false positives. CC identified the same two critical defects independently:

1. `VerifySubmissionAccessAsync` in the controller only bypasses the submitter-ownership check for `NexusAdmin` — `NexusReviewer` is not exempted, causing Reviewer 403 on all 6 write endpoints.
2. The page-level view guard in `NexusArtifacts.razor` uses `&& !isAdmin` — Reviewer role is not checked, blocking Reviewers from loading the page entirely.

CC additionally confirmed: stopPropagation/blur works correctly, capturedIdx closure pattern is correct, TC Edit Mode visibility is correct, DbContextFactory pattern is correct, cascade title update has no stale references. These are all PASS.

---

## Spec Compliance Check

**Brief:** `memory/projects/nexus-tree-editor-spec-2026-05-06.md`

**§2 Codebase Map — Files Changed:**
- `Models/Entities/WorkItemRecord.cs` — ✅ `AcceptanceCriteria` property added
- `Data/NexusDbContext.cs` — ✅ `acceptance_criteria` column mapping added
- `Migrations/20260506000001_AddAcceptanceCriteriaToWorkItemRecord.cs` — ✅ Migration created
- `Services/UserContextService.cs` — ✅ `IsNexusEditorAsync()` added
- `Controllers/NexusArtifactsController.cs` — ✅ 6 endpoints added
- `Components/Pages/NexusArtifacts.razor` — ✅ Edit Mode + all editor controls added

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected

**§7 Authorization — KEY FAILURE:**
- ❌ "NexusAdmin OR NexusReviewer can Add/Edit/Delete/Move WIs on ANY submission" — **NOT MET**
  - `VerifySubmissionAccessAsync` only bypasses for Admin, not Reviewer
  - Page view guard only bypasses for Admin, not Reviewer

**§8 Acceptance Criteria:**
| AC | Status |
|----|--------|
| 1. Admin/Reviewer toggle Edit Mode | ❌ Blocked — Reviewer can't load page |
| 2. WI titles editable, persist on blur | ✅ Logic correct (blocked in practice by AC1) |
| 3. Description/AC expandable, persist | ✅ Logic correct |
| 4. Add WI at any level | ✅ Logic correct |
| 5. Delete with cascade confirmation | ✅ Logic correct |
| 6. Move ▾ dropdown, type-compatible targets | ✅ Implemented |
| 7. All badges preserved in Edit Mode | ✅ RenderTemplateBadge / RenderPredecessorBadges unchanged |
| 8. Post to ADO disabled in Edit Mode | ✅ `Disabled="@_editMode"` |
| 9. Non-editor cannot see Edit button or access API | ✅ Role gate present on all endpoints |
| 10. Save failure snackbar + revert | ✅ All save methods implement catch/revert |
| 11. Tree re-renders after add/delete/move | ✅ `StateHasChanged()` called correctly |

**Spec compliance verdict:** ❌ NON-COMPLIANT (blocks PASS) — §7 Authorization requirement is broken

---

## Consistency Audit

**Files Cross-Referenced:**
- `NexusRoles.cs` ↔ `UserContextService.cs` ↔ `NexusArtifactsController.cs` — ✅ Role constants consistent
- `WorkItemRecord.AcceptanceCriteria` ↔ `NexusDbContext` mapping ↔ Migration ↔ Razor split/join — ⚠️ Minor asymmetry (see Issue #3)
- `ParentTitle` hierarchy pattern — ✅ Consistent across controller helpers and Razor helpers
- `WiTemplateType.Standard` in controller `CreateWi` ↔ entity default — ✅ Match

**Undocumented Dependencies Found:**
- `IsNexusEditorAsync()` in `UserContextService` ↔ page guard in Razor → ⚠️ Page guard bypasses for Admin only, not using `IsNexusEditorAsync()` as it should

---

## Critical Issues [2]

### C1: VerifySubmissionAccessAsync blocks NexusReviewer
- **File:** `NexusArtifactsController.cs` (line 244–246)
- **Category:** Correctness / Authorization
- **Issue:** `VerifySubmissionAccessAsync` returns `false` for any user who is not the submission's `SubmittedBy` AND not `NexusAdmin`. `NexusReviewer` is not exempted. All 6 write endpoints call this method, so all write endpoints 403 any Reviewer who didn't submit that submission.
- **Evidence:**
  ```csharp
  if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
      && !User.IsInRole(NexusRoles.Admin))   // ← NexusReviewer missing
      return false;
  ```
- **Tony's claim:** "Reviewers can edit any submission they have Role access to (not restricted to 'their own'), since the ownership check only applies when the user is NOT an Admin." — **This is factually false. The bypass is Admin-only.**
- **Impact:** NexusReviewer role is completely locked out of all editing API endpoints on any submission they didn't submit.
- **Spec:** §7 "Add/Edit/Delete/Move: NexusAdmin OR NexusReviewer"
- **Fix:**
  ```diff
  - if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
  -     && !User.IsInRole(NexusRoles.Admin))
  + if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
  +     && !User.IsInRole(NexusRoles.Admin)
  +     && !User.IsInRole(NexusRoles.Reviewer))
  ```

### C2: Page view guard blocks NexusReviewer from loading the page
- **File:** `NexusArtifacts.razor` (line ~462)
- **Category:** Correctness / Authorization
- **Issue:** `OnInitializedAsync` checks submission ownership with `isAdmin` only. `IsNexusEditorAsync()` already exists and returns `Admin || Reviewer`, but it isn't used here. A Reviewer who didn't submit can't load the page at all — they get `_error = "You do not have permission to view..."`.
- **Evidence:**
  ```csharp
  var isAdmin = await UserContextService.IsAdminAsync();
  if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
      && !isAdmin)   // ← should be !isEditor or include Reviewer
  {
      _error = "You do not have permission to view this submission's artifacts.";
      return;
  }
  ```
- **Impact:** Reviewer can't even reach the artifacts page for submissions they didn't submit. Edit Mode is unreachable.
- **Fix:**
  ```diff
  - var isAdmin = await UserContextService.IsAdminAsync();
  - if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
  -     && !isAdmin)
  + var isEditor = await UserContextService.IsNexusEditorAsync();  // Admin OR Reviewer
  + if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
  +     && !isEditor)
  ```
  Note: `_isEditor` is set separately later in the method. Consolidate: set `_isEditor` before the check, then use it.

---

## Important Issues [0]

None.

---

## Nitpicks [2]

- **N1:** `GetChildren()` redundant filter (`NexusArtifacts.razor` line ~523) — `&& w.WorkItemType != "Test Case"` is dead code since no caller passes `"Test Case"` and the first condition already handles type. Not blocking.

- **N2:** AC split/join asymmetry (`NexusArtifacts.razor` lines ~503 and ~665) — Load uses `IsNullOrEmpty` (whitespace items kept in list), save uses `IsNullOrWhiteSpace` (whitespace items filtered). A whitespace-only AC item is visible in the list but disappears on save. Likely harmless in practice, but slightly inconsistent behavior. Recommend aligning both to `IsNullOrWhiteSpace`.

---

## Positive Observations

- The 5 flagged items Tony asked to verify are correctly implemented: stopPropagation/blur behavior, capturedIdx closure pattern, TC section Edit Mode visibility, DbContextFactory dispose pattern, cascade title update.
- All 6 endpoints have the role guard at the top (the role check is correct — the failure is downstream in `VerifySubmissionAccessAsync`).
- ParentTitle hierarchy architecture (no FK, recursive walk) is consistently implemented in both controller and Razor.
- AcceptanceCriteria newline-delimited split/join is structurally sound — the asymmetry (N2) is a nitpick.
- SaveTitle cascade on title rename correctly updates local `_workItems` ParentTitle refs AND the DB in one transaction.
- Delete confirmation dialog correctly shows child count from `GetAllDescendants()`.
- Revert-on-failure pattern is consistently applied across all 5 save methods.

---

## What to Fix (FAIL → NEEDS-CHANGES)

Tony needs exactly 2 code fixes:

**Fix 1 — `NexusArtifactsController.cs`, line 244:**
Add `&& !User.IsInRole(NexusRoles.Reviewer)` to the ownership bypass check.

```csharp
if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
    && !User.IsInRole(NexusRoles.Admin)
    && !User.IsInRole(NexusRoles.Reviewer))
    return false;
```

**Fix 2 — `NexusArtifacts.razor`, `OnInitializedAsync`:**
Replace `IsAdminAsync()` guard with `IsNexusEditorAsync()` (which already exists in UserContextService).

```csharp
var isEditor = await UserContextService.IsNexusEditorAsync();
if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
    && !isEditor)
{
    _error = "You do not have permission to view this submission's artifacts.";
    return;
}
// Later:
_isEditor = isEditor;  // reuse the already-awaited value; remove the second call
```

---

## Review Chain
| Stage | Status |
|-------|--------|
| Build (Tony) | ✅ Cycle 1 — Build succeeded, 0 errors |
| Review (Hawkeye) | ❌ Cycle 1 — FAIL (2 critical auth defects) |
| Security Scan (CodeSec) | Pending |
| Deploy (Black Widow) | Pending |
