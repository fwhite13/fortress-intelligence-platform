# Review Report — ADO#2821 (Cycle 2)

**Commit:** `ca777b2`
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-06
**Scope:** Targeted re-review of two C1 auth fixes

---

## Verdict: ✅ PASS

Both critical issues from Cycle 1 are correctly fixed. No regressions. No unintended changes. Ready to deploy.

---

## CC Review Summary

CC reviewed both files end-to-end with adversarial intent. All findings confirmed fixes are correct. No false positives surfaced. No new issues introduced alongside the fixes.

---

## Fix C1 — `VerifySubmissionAccessAsync` bypass — ✅ VERIFIED

**File:** `NexusArtifactsController.cs` (~L255–258)

```csharp
if (!string.Equals(submission.SubmittedBy, currentUpn, StringComparison.OrdinalIgnoreCase)
    && !User.IsInRole(NexusRoles.Admin)
    && !User.IsInRole(NexusRoles.Reviewer))
    return false;
```

- `NexusRoles.Reviewer` **present** — C1 fix confirmed ✅
- `NexusRoles.Admin` **still present** — no regression ✅
- Logic correct: bypass only when NOT owner AND NOT admin AND NOT reviewer ✅
- `VerifySubmissionAccessByArtifactSetAsync` is a one-line passthrough to this method — no duplicate ownership logic ✅
- All 6 call sites in the controller funnel through this single method ✅

---

## Fix C2 — `OnInitializedAsync` view guard — ✅ VERIFIED

**File:** `NexusArtifacts.razor` (`OnInitializedAsync`)

```csharp
var isAdmin = await UserContextService.IsNexusEditorAsync();
if (!string.Equals(submission.SubmittedBy, currentUpn, ...) && !isAdmin)
{
    _error = "You do not have permission to view this submission's artifacts.";
    return;
}
```

- `IsNexusEditorAsync()` called — not `IsAdminAsync()` — C2 fix confirmed ✅
- Variable name `isAdmin` is misleading but not a defect (nitpick only, non-blocking) ✅
- `UserContextService.IsNexusEditorAsync()` returns `Admin || Reviewer` — Reviewer now has view access ✅

---

## Scope Check — No Unintended Changes

- Commit touches exactly 2 files: controller + razor ✅
- `GetExternalDependencies` endpoint correctly retains Admin-only bypass (Reviewer NOT added — intentional design) ✅
- All other controller methods (`PatchTitle`, `PatchDescription`, `PatchAc`, `PatchParent`, `CreateWi`, `DeleteWi`) unchanged ✅
- No methods added or removed ✅

---

## Issues Found

| Severity | File | Issue |
|----------|------|-------|
| Nitpick | `NexusArtifacts.razor` | Variable `isAdmin` is named misleadingly after being assigned from `IsNexusEditorAsync()`. Non-blocking. |

---

## Cycle 1 Issues — Status

| Issue | Status |
|-------|--------|
| C1: NexusReviewer missing from `VerifySubmissionAccessAsync` bypass | ✅ Fixed |
| C2: `OnInitializedAsync` view guard used `IsAdminAsync()` instead of `IsNexusEditorAsync()` | ✅ Fixed |

---

_Hawkeye — you see what others miss._
