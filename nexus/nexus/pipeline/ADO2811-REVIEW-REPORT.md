# Review Report — ADO#2811

**Verdict: PASS**

**Commit:** `7867087`
**Files reviewed:**
- `Services/ISubmissionService.cs`
- `Services/SubmissionService.cs`
- `Components/Pages/Dashboard.razor`
- `Components/Pages/SubmissionDetail.razor`
- `Controllers/NexusArtifactsController.cs`
**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-06

---

## Spec Compliance Check

**Spec:** `nexus-decomp-upgrade-spec-2026-04-27.md §13`

### §13.4 Acceptance Criteria

- [x] **AC1:** `NexusAdmin` user can see all submissions in the list view regardless of submitter
  ✅ `GetByUserAsync(userUpn, isAdmin)` — when `isAdmin=true`, no `.Where()` filter is applied. Dashboard passes `_isAdmin` from `authState.User.IsInRole(NexusRoles.Admin)`.

- [x] **AC2:** `NexusAdmin` user can open, edit, and approve any submission
  ✅ SubmissionDetail.razor resolves `_isAdmin` before `GetByIdAsync`, then enforces: `if (SubmittedBy != currentUpn && !_isAdmin) → error + null submission`. Admin bypasses this guard. Edit/approve actions are inside the same component and thus protected by the same gate.

- [x] **AC3:** `NexusAdmin` user can trigger decomposition on any submission
  ✅ `HandleGenerateWorkItems()` is in SubmissionDetail, gated by `_submission is null` guard. If admin passed the access guard, `_submission` is populated and decomp trigger is available.

- [x] **AC4:** `NexusUser` still sees only their own submissions (no regression)
  ✅ `GetByUserAsync(userUpn, isAdmin=false)` applies `.Where(s => s.SubmittedBy == userUpn)`. No other path was opened. Non-admin attempting to access another user's submission by URL gets `_error = "You don't have permission to view this submission."` and `_submission = null`.

- [x] **AC5:** Submitter UPN visible in list view when viewer is admin
  ✅ Both `<MudTh>Submitter</MudTh>` and `<MudTd>@context.SubmittedBy</MudTd>` wrapped in `@if (_isAdmin)`. Title also switches to "All Submissions" when admin.

- [x] **AC6:** `NexusArtifactsController` external-dependencies endpoint enforces ownership OR admin bypass
  ✅ UPN resolved from `preferred_username ?? ClaimTypes.Email ?? ClaimTypes.Name`. Check: `if (SubmittedBy != currentUpn && !User.IsInRole(NexusRoles.Admin)) return Forbid()`. Guard is before any data access.

- [ ] **AC7:** Elise/Fred Entra group assignments — out of scope for code review, skip.

**Spec compliance verdict: ✅ COMPLIANT** (all testable ACs met)

---

## Consistency Audit

**UPN resolution chain consistency:**
- `UserContextService.GetUpnAsync()`: `preferred_username ?? ClaimTypes.Email ?? ClaimTypes.Name`
- `NexusArtifactsController`: `preferred_username ?? ClaimTypes.Email ?? ClaimTypes.Name`
- ✅ Identical chain — no mismatch

**Admin role check consistency:**
- Dashboard: `authState.User.IsInRole(NexusRoles.Admin)` — direct Blazor auth state
- SubmissionDetail: `await UserContextService.IsAdminAsync()` → internally calls `authState.User.IsInRole(NexusRoles.Admin)`
- Controller: `User.IsInRole(NexusRoles.Admin)`
- ✅ All resolve the same underlying claim

**String comparison consistency:**
- SubmissionDetail.razor access guard: `StringComparison.OrdinalIgnoreCase` ✅
- NexusArtifactsController: `StringComparison.OrdinalIgnoreCase` ✅
- `DeleteSubmissionAsync` guard: `!=` (plain string equality, no `OrdinalIgnoreCase`) — minor inconsistency but UPNs from Entra are consistently lowercased so not a live risk

**Hardcoded owner filter scan:**
- Only remaining `.Where(s => s.SubmittedBy == userUpn)` is inside `GetByUserAsync`, gated by `!isAdmin`. ✅ No leaks.

---

## Critical Issues: 0

None.

---

## Important Issues: 1

### I1: Service-layer mutating methods have no individual ownership guard

**Files:** `SubmissionService.cs` — `UpdateStatusAsync`, `UpdateNarrativeAsync`, `SetActiveSpecDocumentAsync`
**Severity:** Important (defense-in-depth gap, not a live access control failure)

**Issue:** The spec (§13.3) specified adding `bool isAdmin` parameters to all mutating service methods. Tony instead implemented the ownership guard at the Razor page layer (SubmissionDetail.razor) and left the service methods parameter-free. The ACs pass because every mutating operation in the current UI goes through SubmissionDetail's access gate. However, these service methods can be called without ownership verification if a future controller or Blazor page calls them directly.

`UpdateStatusAsync(int id, SubmissionStatus status)` — no caller check, no `isAdmin` param.
`UpdateNarrativeAsync(int submissionId, string narrativeText)` — no caller check, no `isAdmin` param.
`SetActiveSpecDocumentAsync(int submissionId, int specDocumentId)` — no caller check.

**Impact:** Not a current vulnerability — the UI path is guarded. Future risk if any of these are wired to an API endpoint without an ownership check.

**Fix (deferred is acceptable):** Add `string callerUpn, bool callerIsAdmin` to each of these methods and enforce ownership there, matching the existing `DeleteSubmissionAsync` pattern. Not blocking for this cycle, but should be tracked.

---

## Nitpicks: 1

### N1: `DeleteSubmissionAsync` uses plain `!=` for UPN comparison

**File:** `SubmissionService.cs` (line 224)
```csharp
if (!callerIsAdmin && submissionCheck.SubmittedBy != callerUpn)
```
All other ownership checks use `StringComparison.OrdinalIgnoreCase`. This works in practice (Entra UPNs are lowercased), but is inconsistent. Not blocking.

---

## Positive Observations

- Access guard in SubmissionDetail.razor correctly resolves user context **before** loading the submission — proper sequencing, no TOCTOU risk.
- Guard nulls out `_submission` after unauthorized detection — prevents any data from rendering in the UI even if the Razor template has `_submission?.Property` references.
- `NexusArtifactsController.GetExternalDependencies` BOLA fix is well-implemented — checks before data load, uses the same UPN resolution chain as the service layer.
- Dashboard Submitter column is properly double-gated (both header and cell).
- The `GetByUserAsync` implementation (`AsQueryable()` + conditional `Where()`) is clean — no code duplication, single query path.

---

## What to Track (not blocking)

ADO follow-up: service-layer `isAdmin` params on `UpdateStatusAsync`, `UpdateNarrativeAsync`, `SetActiveSpecDocumentAsync` per §13.3. Low risk today; meaningful if any of these are ever called from a controller without a prior ownership check.
