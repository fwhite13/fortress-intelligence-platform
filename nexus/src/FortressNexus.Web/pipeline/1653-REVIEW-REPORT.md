# Review Report — WI #1653

**NewSpecWizard ResumeSubmissionId Route + OnInitializedAsync Resume Mode**
**Commit:** `ad07edf` | **Cycle:** 1 of 2 | **Reviewer:** Hawkeye | **Date:** 2026-04-08

---

### Verdict: ✅ PASS

---

## Spec Compliance Check

**§2 Files Modified:**
- `Components/Pages/NewSpecWizard.razor` — ✅ modified as specified
- `Services/UserContextService.cs` — ✅ modified as specified

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected. Commit touches only the two specified files.

**§7 Acceptance Criteria:**
- [x] New route `/nexus/{ResumeSubmissionId:int}/resume` added above `/nexus/new` — ✅ Verified (line 1)
- [x] `[Parameter] int? ResumeSubmissionId` added — ✅ Verified (line ~189)
- [x] `OnInitializedAsync` guard: null ResumeSubmissionId returns immediately — ✅ Verified (first statement, line 208)
- [x] Auth guard: owner OR admin can resume — ✅ Verified (lines 220–228)
- [x] Snapshot fields `_originalNarrative` + `_originalFileIds` set at init — ✅ Verified (lines 262–266)
- [x] `IsAdminAsync()` added to `UserContextService` — ✅ Verified

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Cross-file checks performed:**

| Check | Result |
|-------|--------|
| `GetUpnAsync()` return value ↔ `submission.SubmittedBy` write path | ✅ Both use `preferred_username` via same `UserContextService.GetUpnAsync()` |
| `IsAdminAsync()` pattern ↔ `IsReviewerAsync()` / `Dashboard.razor` / `SpecService.cs` | ✅ All use `IsInRole(NexusRoles.*)` — consistent |
| `NexusRoles.Admin = "NexusAdmin"` ↔ all usage sites | ✅ Consistent across codebase |
| Route `/nexus/{ResumeSubmissionId:int}/resume` ↔ `/nexus/{Id:int}` (SubmissionDetail) | ✅ Different segment depth — no conflict |
| Route suffix `/resume` ↔ `/review` (NexusReview) | ✅ Different literal suffix — no conflict |
| `_originalNarrative` source ↔ `_narrativeText` source | ✅ Both assigned from `submission.NarrativeText` — independent copies |
| `GetByIdAsync` Include chain ↔ wizard access patterns | ✅ All navigations eagerly loaded |

**No undocumented dependencies or sync mismatches found.**

---

## Critical Issues — 0

All six critical checks passed.

### C1: Auth Guard — ✅ CLEAN
`submission.SubmittedBy` is written via `UserContextService.GetUpnAsync()` at submission creation. The resume guard reads via the same `GetUpnAsync()`. Same claim, same format, correct `!=` comparison. Zero UPN/OID mismatch risk.

### C2: Null Submission / 404 Path — ✅ CLEAN
Null check at line 212 has a hard `return` inside the `if` block. `submission` is only accessed after the null block ends — compiler flow analysis confirms no NRE risk.

### C3: Guard Order — ✅ CLEAN
`if (ResumeSubmissionId is null) return;` is the **first statement** in `OnInitializedAsync`. DB calls are unreachable when null.

### C4: Snapshot Integrity — ✅ CLEAN
`_originalNarrative` and `_originalFileIds` are private fields (no public setters, not bindable), set at the end of `OnInitializedAsync` directly from `submission.*`, and never modified afterward. Baseline is correct and immutable post-init.

### C5: New-Submission Path Untouched — ✅ CLEAN
Single `OnInitializedAsync` override. Early return at line 208 fully protects the new-submission flow. No regression.

### C6: Route Conflicts — ✅ CLEAN
`/nexus/{int}/resume` (2 segments post-`/nexus/`) does not conflict with `/nexus/{int}` (1 segment) or `/nexus/{int}/review` (different literal). Blazor router resolves unambiguously.

---

## Important Issues — 0

### I7: IsAdminAsync Consistency — ✅ CLEAN
Identical pattern to `IsReviewerAsync()`, Dashboard.razor admin check, and SpecService.cs. No divergence found.

### I8: Async Correctness — ✅ CLEAN
All four service calls in `OnInitializedAsync` are properly `await`-ed. No `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`.

### I9: CS0414 `_isResume` — ✅ EXPECTED
`_isResume` is declared, set to `true` at init, and never read. This is intentional scaffold for WI #1656 change detection. The comment confirms it. Not a wiring omission.

### I10: GetByIdAsync Include Coverage — ✅ CLEAN
`SubmissionService.GetByIdAsync` includes: `MockupFile`, `SubmissionFiles → ThenInclude(UploadedFile)`, `SpecDocuments`. All navigations accessed in the wizard are eagerly loaded.

---

## Nitpicks — 3

| # | File | Area | Issue |
|---|------|------|-------|
| N1 | `NewSpecWizard.razor` | `_originalFileIds` | `Where(id => id > 0)` guard on non-nullable `int` FK — defensively harmless but unnecessary |
| N2 | `NewSpecWizard.razor` | `_uploadedFiles` assignment | `.Select(sf => sf.UploadedFile!)` + `.Where(f => f is not null)` — `!` and `Where` are contradictory signals; minor style inconsistency, not a defect |
| N3 | `NewSpecWizard.razor` | Lines 228–229 | Duplicate comment "Populate wizard fields from the submission" — copy-paste leftover |

None blocking.

---

## Positive Observations

- **Guard order is textbook correct.** Null check first, not found check second, auth check third. Exactly right.
- **Snapshot vars are clean.** Private fields, set once, never mutated. WI #1656 change detection will have a solid baseline.
- **GetByIdAsync already loads everything needed.** Tony didn't have to add any new includes — the existing query covered the resume-mode data requirements.
- **Route design is clean.** `/resume` suffix cleanly disambiguates from the existing `/nexus/{id}` SubmissionDetail. Mirrors the `/nexus/{id}/review` pattern already in the codebase.
- **Discovery session failure is non-fatal.** Correct decision — a missing discovery session shouldn't block resuming a submission.

---

## Summary

Clean commit. Tony got all the hard things right: guard order, null safety, auth comparison, snapshot integrity, and route design. Three minor nitpicks are all cosmetic. CS0414 on `_isResume` is expected and confirmed benign.

**Verdict: PASS — ready to merge.**
