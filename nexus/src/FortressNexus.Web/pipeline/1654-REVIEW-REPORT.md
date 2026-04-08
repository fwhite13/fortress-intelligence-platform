# Review Report — WI #1654
## NEXUS Phase 3 — Pre-populate existing files in resume mode with soft-delete UI

**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `41b49ca`
**Cycle:** 1 of 2
**Risk:** Medium
**Date:** 2026-04-08

---

### Verdict: PASS ✅

Core implementation is correct. Two Important items flagged — both are downstream consequences of the intentional WI #1655 deferral scope, not bugs in this WI's target behavior. Soft-delete UI works as designed.

---

## Spec Compliance Check

**§2 Codebase Map:**
- `Components/Pages/NewSpecWizard.razor` — ✅ modified as specified
- `Components/Pages/FileUploadZone.razor` — ✅ not touched (correct per spec)

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected. No service, DB, or API calls were introduced.

**§7 Acceptance Criteria:**
- [x] `_filesToDelete` HashSet introduced — ✅ Verified (`private HashSet<int> _filesToDelete = new()`)
- [x] `TotalFileCount` computed property — ✅ Verified, formula is correct
- [x] `RemoveExistingFile(int fileId)` — ✅ Verified, ONLY adds to `_filesToDelete`, no service calls
- [x] Step 1 UI renders existing files list in resume mode — ✅ Verified with `_isResume` guard
- [x] Narrative pre-population handled by #1653 — ✅ No changes needed, confirmed
- [x] `FileUploadZone.razor` not modified — ✅ Confirmed

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files Cross-Referenced:**
- `NewSpecWizard.razor` — self-consistent; `_filesToDelete` is declared, used in filter, and counted — nowhere else
- No cross-file consistency issues. This WI touches one file.

**Undocumented Dependencies:**
- `_uploadedFiles` serves dual purpose (DB-loaded existing files in resume mode; upload results in new-submission mode). The `_isResume` guard correctly disambiguates. No undocumented coupling issue.

---

## CC Review Summary

Ran Claude Code CLI against the full file with a 10-question adversarial brief. CC read the file directly and answered each question with code quotes.

**Confirmed clean:**
- `RemoveExistingFile` is data-safe (Q1) ✅
- Optimistic UI removal works via Blazor's automatic `StateHasChanged()` on event handlers; no manual call needed (Q2) ✅
- `TotalFileCount` is arithmetically correct across all edge cases (Q3) ✅
- `_isResume` guard is sufficient to protect new-submission flow (Q5) ✅
- `_uploadedFiles.Count > 0` condition is correct and unambiguous (Q6) ✅
- Razor markup is clean — no syntax issues, no reserve variable collisions (Q7) ✅
- `aria-label="Remove file"` is present on the × button (Q8) ✅
- Review step display correct; text change "selected" → neutral "file(s)" is an improvement (Q9) ✅
- `_filesToDelete` is never passed to any service layer (Q10) ✅

**Dismissed as false positive:**
- Redundant `var capturedFile = existingFile;` — harmless in C# 5.0+ foreach; no closure bug without it, but also no harm with it.

**Confirmed as real issues (see below):**
- `TotalFileCount` inflation in resume mode if user drops new files
- `HandleSubmit` is a silent stub in resume mode with no WI #1655 comment

---

## Important Issues [2]

### I1 — Misleading `TotalFileCount` in resume mode when user drops new files
- **File:** `NewSpecWizard.razor` — `TotalFileCount` property + `GoToStep2Discovery`
- **Category:** UX / correctness
- **Issue:** In resume mode, `_submissionId` is populated from `OnInitializedAsync`. In `GoToStep2Discovery`, the upload block is guarded by `_submissionId == null` — so newly dropped files (`_pendingFiles`) in resume mode are silently never uploaded. However, `TotalFileCount` still adds `_pendingFiles.Count`, so the Review step will display an inflated file count (e.g. "5 file(s)" when only 3 are persisted). The user sees no warning.
- **Impact:** Misleading Review step in resume mode; user believes more files are included than will actually be saved. WI #1655 must address this when it implements resume-mode upload.
- **Fix options (choose one for WI #1655):**
  - Option A: Exclude `_pendingFiles.Count` from `TotalFileCount` when in resume mode: `_isResume ? _uploadedFiles.Count(f => !_filesToDelete.Contains(f.Id)) : _pendingFiles.Count`
  - Option B: Disable/hide `FileUploadZone` in resume mode until WI #1655 implements the upload path
  - Option C: Add a visible note in the UI ("New files will be uploaded on submit") — only valid once WI #1655 actually wires up the upload

### I2 — `HandleSubmit` is a silent stub in resume mode with no deferral comment
- **File:** `NewSpecWizard.razor` — `HandleSubmit` method (~line 437)
- **Category:** Maintainability / correctness intent
- **Issue:** In resume mode, `HandleSubmit` navigates away without saving any changes. Title edits, narrative edits, `_filesToDelete` contents, and any new `_pendingFiles` are silently discarded. The only WI #1655 deferral comment is in `RemoveExistingFile`; `HandleSubmit` has no such comment. A developer maintaining this code has no signal that the method is intentionally incomplete.
- **Impact:** Low production risk for this WI (the whole save path is deferred), but high confusion risk for WI #1655 implementation.
- **Fix:** Add comment to `HandleSubmit`:
  ```csharp
  // TODO(WI #1655): In resume mode, persist title/narrative edits,
  // process _filesToDelete (soft-delete from DB/S3), and upload _pendingFiles
  ```

---

## Nitpicks [3]

- **N1:** `var capturedFile = existingFile;` inside foreach — redundant in C# 5.0+. `existingFile` is already correctly scoped per iteration. Not a bug. (`NewSpecWizard.razor` ~line 88)
- **N2:** `aria-label="Remove file"` is generic. A screen reader cycling through N buttons hears "Remove file" N times without knowing which file. Consider `aria-label="@($"Remove {capturedFile.OriginalFileName}")"` for specificity.
- **N3:** `_existingSpecDocument` is populated in `OnInitializedAsync` but never referenced in markup or any method. Dead state — forward scaffold presumably for WI #1656. Low noise but adds a DB query with no current consumer.

---

## Positive Observations

- **`RemoveExistingFile` is surgically clean.** Two lines: `HashSet.Add()` + a comment. No creep, no premature implementation. Exactly what the spec called for.
- **`TotalFileCount` ternary is well-structured.** The `_isResume ? ... : 0` pattern avoids the footgun of counting `_uploadedFiles` in new-submission mode.
- **The `_isResume` guard on the existing files block is tight.** `_isResume` starts false and is only set in the resume branch — no way to accidentally show the block in new-submission flow.
- **Closure capture comment is honest.** Tony left clear breadcrumb comments pointing to WI #1655 so future work knows where to pick up.

---

## What to Fix Before Shipping WI #1655

1. Add WI #1655 comment to `HandleSubmit` (I2 — easy, 2 lines)
2. Resolve `TotalFileCount` inflation for resume + new files (I1 — pick one of the three options)
3. Make `aria-label` file-specific on the × button (N2 — nice to have, not blocking)

---

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| `_filesToDelete` HashSet — soft-delete tracking | ✅ Verified |
| `TotalFileCount` = (existing − deleted) + pending | ✅ Verified, formula correct |
| `RemoveExistingFile` — no service calls, UI only | ✅ Verified, SAFE |
| Existing files list shown in resume mode only | ✅ Verified, `_isResume` guard solid |
| New-submission flow unchanged | ✅ Verified, zero impact |
| `FileUploadZone.razor` not modified | ✅ Verified |
