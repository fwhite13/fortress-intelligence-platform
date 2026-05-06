# Build Report — ADO#2821 — Cycle 2

**Agent:** Tony Stark — BUILD  
**Commit:** ca777b2  
**Date:** 2026-05-06  

---

## Fixes Applied

### Fix C1 — NexusArtifactsController.cs
- **File:** `nexus/src/FortressNexus.Web/Controllers/NexusArtifactsController.cs`
- **Location:** `VerifySubmissionAccessAsync`, line ~246
- **Change:** Added `&& !User.IsInRole(NexusRoles.Reviewer)` to the ownership bypass condition
- **Before:** Only Admin could bypass ownership check
- **After:** Admin and Reviewer both bypass ownership check

### Fix C2 — NexusArtifacts.razor
- **File:** `nexus/src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor`
- **Location:** `OnInitializedAsync`, line ~589
- **Change:** `UserContextService.IsAdminAsync()` → `UserContextService.IsNexusEditorAsync()`
- **Before:** View guard blocked Reviewers who aren't the submitter (only Admins passed)
- **After:** Any Nexus editor (including Reviewers) can view any submission's artifacts

---

## Scope Confirmation

Only 2 files changed in this commit:
- `nexus/src/FortressNexus.Web/Components/Pages/NexusArtifacts.razor` — 2 lines modified
- `nexus/src/FortressNexus.Web/Controllers/NexusArtifactsController.cs` — 2 lines modified (1 line added, 1 reindented)

No other files touched.

---

## Status
- Build: SUCCEEDED (compile-time only — no test suite run)
- Commit: ca777b2
- Staged for Clint review
