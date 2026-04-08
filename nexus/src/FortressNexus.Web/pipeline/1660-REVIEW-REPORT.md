# Review Report — WI #1660 — Skip-regen path

**Reviewer:** Hawkeye (code-reviewer)
**Commit:** `137ea83`
**Cycle:** 1 of 2
**Date:** 2026-04-08
**Risk:** Medium

---

### Verdict: ✅ PASS

---

## Spec Compliance Check

**§ What was built:** Skip-regen branch in `HandleSubmit` — `_isResume && !_hasChanges && _existingSpecDocument != null` → `UpdateStatusAsync(AwaitingReview)` + navigate to submission detail.

**Note on status name:** Brief said `PendingReview`, codebase uses `SubmissionStatus.AwaitingReview`. Tony used the actual constant — consistent with what `GenerateAsync` sets. Correct call.

**§ Files modified:** `Components/Pages/NewSpecWizard.razor` — only file in diff. ✅ In scope.

**§ Acceptance criteria:**
- [x] New branch fires only when all three conditions are true ✅
- [x] `SubmissionStatus.AwaitingReview` typed constant used ✅
- [x] No regen calls in the new branch ✅
- [x] Navigation routes to submission detail ✅
- [x] Existing branches (regen path, normal flow) untouched ✅

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files cross-referenced:**
- `NewSpecWizard.razor` ↔ `SubmissionDetail.razor` — route `/nexus/{Id:int}` matches navigation `$"/nexus/{ResumeSubmissionId}"` ✅
- `NewSpecWizard.razor` ↔ `Services/SubmissionService.cs` — `UpdateStatusAsync` signature and `AwaitingReview` usage ✅
- `Models/Enums/SubmissionStatus.cs` — `AwaitingReview` member confirmed present ✅

**Navigation consistency note (Nitpick):** Skip-regen branch uses `ResumeSubmissionId` (route parameter) while all other navigation in `HandleSubmit` uses `_submissionId.Value` (local field). These are provably equivalent in resume context (`_submissionId` is set from `ResumeSubmissionId.Value` during load). No functional impact. Not a bug.

---

## Critical Issues: 0

---

## Important Issues: 0

---

## Nitpicks: 1

**N1:** Navigation uses `ResumeSubmissionId` instead of `_submissionId.Value` (`NewSpecWizard.razor`, skip-regen block, ~line 552)
- The skip-regen branch is the only place in `HandleSubmit` that navigates using the route parameter directly rather than the resolved local `_submissionId`.
- No functional impact — values are equivalent — but inconsistent with the pattern everywhere else in the method.
- Not blocking. Consider using `_submissionId.Value` for consistency in a future cleanup pass.

---

## Positive Observations

- Branch condition is exactly right — all three guards present, no shortcuts.
- `return` after navigation prevents any inadvertent fallthrough.
- Enum used correctly; no magic strings.
- `UpdateStatusAsync` commits via `SaveChangesAsync()` before navigation fires.
- Null guard on not-found in `UpdateStatusAsync` logs a warning rather than throwing — silent failure on navigation is pre-existing behavior shared across all callers, not a regression introduced here.
- Edge case (`_isResume=true`, `_hasChanges=false`, `_existingSpecDocument=null`) correctly falls through to normal flow — no spec gen called, navigates to submission detail via `_submissionId.Value`.

---

## CC Review Summary

CC read the full `HandleSubmit` method, `UpdateStatusAsync`, and confirmed the `SubmissionDetail.razor` route. All six targeted checks came back clean. The one item flagged (C4) was confirmed non-functional — a style inconsistency in which variable name is used for navigation, not a value mismatch. CC verdict: PASS.

---

## What to Fix

Nothing required. N1 (navigation variable naming) is a future cleanup suggestion only.

---

_Hawkeye — eyes on every line._
