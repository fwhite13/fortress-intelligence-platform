# Review Report — ADO #1656 — `_hasChanges` Change Detection

**Reviewer:** Hawkeye  
**Cycle:** 1 of 2  
**Commit:** `fda199c` (diff from `b044cec`)  
**File:** `Components/Pages/NewSpecWizard.razor`  
**Risk:** Medium  
**Date:** 2026-04-08

---

## Verdict: ✅ PASS

---

## Spec Compliance Check

**All changes in `Components/Pages/NewSpecWizard.razor` only.** Single file, correct scope.

**Acceptance criteria:**
- [x] `_hasChanges` is an expression-body computed property — ✅ Verified
- [x] Trim symmetry on narrative comparison — ✅ Verified
- [x] `_pendingFiles` correctly represents only new files — ✅ Verified (FileUploadZone `InitialFiles` does not fire `OnFilesSelected` on init)
- [x] New-submission short-circuit (`_isResume == false` → `_hasChanges = false`) — ✅ Verified
- [x] Confirm step notices guarded by `_isResume` — ✅ Verified

**Spec compliance verdict:** ✅ COMPLIANT

---

## CC Review Summary

Claude Code performed adversarial analysis against all 8 review focus items. All 5 critical items passed. Both important items passed. One additional finding surfaced: dead code (`_originalFileIds`).

---

## Consistency Audit

- `_pendingFiles` (new files) ↔ `_uploadedFiles` (original files) — ✅ distinct lists, no overlap
- `FileUploadZone.InitialFiles` → `OnParametersSet` internal display only, does NOT invoke `OnFilesSelected` — ✅ verified by reading component source
- `_isResume = true` — exactly one write path (line 310, inside resume block) — ✅ confirmed by grep

---

## Critical Issues — 0

None found.

---

## Important Issues — 0

None found.

---

## Nitpicks — 1

### N1: `_originalFileIds` is dead code
- **File:** `NewSpecWizard.razor` (lines 314–318)
- **Issue:** `_originalFileIds` is populated during resume init but never read anywhere in the file. `_hasChanges` uses `_filesToDelete.Count` and `_pendingFiles.Count` for file change detection — `_originalFileIds` plays no role. It appears to be scaffolding for a future, more granular check (e.g., detecting file replacements vs. net-new additions).
- **Impact:** None at runtime — change detection is correct without it. Adds misleading complexity.
- **Fix:** Either remove it or add a comment like `// Reserved for WI #XXXX — granular file change detection`.
- **Blocking?** No. Track in a future cleanup WI.

---

## Detailed Check Results

| # | Check | Result | Notes |
|---|-------|--------|-------|
| C1 | `_hasChanges` expression body | ✅ PASS | `private bool _hasChanges =>` at line 248 — recomputes on every access |
| C2 | Trim symmetry | ✅ PASS | `_narrativeText.Trim() != _originalNarrative.Trim()` — both sides trimmed |
| C3 | `_pendingFiles` false positive risk | ✅ PASS | `FileUploadZone.OnParametersSet` updates internal display only; `OnFilesSelected` only fires on user interaction |
| C4 | `_isResume` short-circuit | ✅ PASS | One write path (line 310); new-submission returns at line 257, never sets flag |
| C5 | Confirm step guard | ✅ PASS | Outer `@if (_isResume)` wraps both branches; no notice leaks |
| I6 | `_hasChanges` side-effect-free | ✅ PASS | Pure field reads and property accesses only |
| I7 | Zero-original-files edge case | ✅ PASS | `_pendingFiles.Count > 0` correctly signals `true` when user adds file to a no-file submission |
| I8 | Severity choice | ✅ OK | Warning for regeneration, Info for no-change — appropriate; Success could be argued but not required |

---

## Positive Observations

- Expression-body property is the right pattern here — no risk of stale reads mid-session as the user navigates steps.
- `FileUploadZone.InitialFiles` contract was an important thing to verify — correctly separated display-init from event-fire. This was a realistic false-positive vector and it's handled correctly.
- Short-circuit with `_isResume &&` keeps the property semantically clean and safe for the new-submission path.
- UI guard `@if (_isResume)` wrapping both branches is correct — no edge-case leakage.

---

## What Ships

`fda199c` is clear for pipeline progression. The `_originalFileIds` dead code is a minor cleanup item, not a blocker.
