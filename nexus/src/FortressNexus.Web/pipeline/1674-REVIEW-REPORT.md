# Review Report — WI #1674 — Narrative persist on step advance (resume mode)

**Reviewer:** Hawkeye (code-reviewer)  
**Date:** 2026-04-08  
**Commit:** `4ba528c`  
**Cycle:** 1  
**Risk:** low-medium

---

### Verdict: ✅ PASS

---

## Spec Compliance Check

**Brief source:** `pipeline/1674-BUILD-REPORT.md`

**§ Codebase Map:**
- `Components/Pages/NewSpecWizard.razor` — ✅ modified as specified (+13 lines)
- Pipeline docs bundled in same commit — ✅ documentation only, no functional impact

**§ Out of Scope:**
- ✅ No service files, model files, or other razor files touched. Only functional change is the 13-line insertion.

**§ Acceptance Criteria:**
- [x] Narrative edit persisted to DB when advancing from Files → Discovery in resume mode ✅ Verified — `UpdateNarrativeAsync` called in `GoToStep2Discovery()` before `_activeStep = 2`
- [x] `UpdateNarrativeAsync` not re-implemented — uses existing service method ✅ Confirmed — calls existing `SubmissionService.UpdateNarrativeAsync`
- [x] Fix is guard-gated (`_isResume && _submissionId.HasValue`) — no impact on new-submission flow ✅ Guard confirmed sound
- [x] `dotnet build` — 0 errors, 0 warnings ✅ Syntactic analysis clean; build report confirms
- [x] Minimal change — one insertion point, no other methods touched ✅ Confirmed

**Spec compliance verdict:** ✅ COMPLIANT

---

## Consistency Audit

**Files Cross-Referenced:**
- `NewSpecWizard.razor` ↔ `ISubmissionService.cs` — ✅ `UpdateNarrativeAsync(int, string)` signature matches call site exactly
- `NewSpecWizard.razor` ↔ `SubmissionService.cs` — ✅ Implementation takes `(int submissionId, string narrativeText)`, looks up by `FindAsync(submissionId)`, correct

**Field binding verification:**
- `_narrativeText` — ✅ `@bind-Value="_narrativeText"` at line 52, declared `private string _narrativeText = ""` at line 222. Tony used the correct field name.

**Undocumented Dependencies:**
- `UpdateNarrativeAsync` is also called at lines 485 and 602 (submit paths) — ✅ consistent usage, no conflict with the new call at line 411

---

## Critical Issues: 0

None found.

---

## Important Issues: 0

None found.

---

## Nitpicks: 0

None.

---

## CC Review Summary

CC read `NewSpecWizard.razor`, `ISubmissionService.cs`, and `SubmissionService.cs`. All critical checks returned clean:

| Check | Result |
|-------|--------|
| Variable name (`_narrativeText` is correct bound field) | ✅ CORRECT |
| Placement (after `CreateAsync`, before `_activeStep = 2`) | ✅ CORRECT |
| Guard condition (`_isResume && _submissionId.HasValue`) | ✅ SOUND |
| `UpdateNarrativeAsync` signature match | ✅ CORRECT |
| Non-fatal catch (Snackbar warning, step advance not blocked) | ✅ ACCEPTABLE |
| Scope (only `NewSpecWizard.razor` functional change) | ✅ CLEAN |
| Build / syntactic | ✅ PASSES |

No false positives dismissed — CC found no issues worth flagging.

---

## Placement Detail

Full ordering in `GoToStep2Discovery()`:
1. File uploads (early in method)
2. `if (_submissionId == null)` → `CreateAsync` (new-sub path only; no-op in resume mode since `_submissionId` is set in `OnInitializedAsync`)
3. **Persist narrative block** ← NEW CODE fires here
4. `_activeStep = 2;`
5. `StateHasChanged()`

Ordering is correct. In resume mode, `_submissionId` is always populated before this method runs.

---

## Non-fatal Catch Rationale

The catch allows step advance to continue after a save failure, showing a Snackbar warning. This is acceptable because:
1. Pattern is consistent with file-upload error handling elsewhere in the same method
2. The narrative is still in-memory — it will be persisted again at final submit via `ApplyResumeChangesAsync`
3. Hard-blocking navigation on a non-critical save failure would be worse UX than a warning

---

## Positive Observations

- Tony correctly identified that `GoToStep2()` (Step 0→1) is sync/validation-only and needs no fix — surgical diagnosis.
- Reuse of the existing `UpdateNarrativeAsync` service method is correct — no new service code or interface changes needed.
- Guard condition `_isResume && _submissionId.HasValue` is airtight: both flags are co-set in `OnInitializedAsync` and nothing in `GoToStep2Discovery()` can invalidate `_submissionId` for a resume session.
