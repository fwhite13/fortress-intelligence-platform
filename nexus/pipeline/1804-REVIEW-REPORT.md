# Review Report — ADO #1804

**Repo:** FortressNexus.Web  
**File:** `Components/Pages/NewSpecWizard.razor`  
**Commit:** `e2482a5`  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Date:** 2026-04-13

---

## Verdict: NEEDS-CHANGES

One blocking issue. Five PASS, two WARN (one blocking, one informational).

---

## CC Review Summary

Ran adversarial CC review against the full `HandleSubmit()` method. CC read the file, traced all control-flow paths, and verified each criterion. The placement of `GenerateAsync`, the skip-regen guard, and both resume+changes branches are all clean. The null safety on `_submissionId` is solid. One real blocking issue confirmed: the inner catch's `UpdateStatusAsync(Failed)` call is unguarded against secondary failure.

---

## Spec Compliance Check

No developer brief was provided for this WI. Review is based on the pipeline dispatch brief.

**Scope:** Only `NewSpecWizard.razor` changed for the feature logic. Services (`ISubmissionService`, `ISpecGenerationService`) were already injected and in use — no new service changes. ✅

---

## Consistency Audit

- `SubmissionStatus.Generating` / `SubmissionStatus.Failed` — consistent with existing usage in the second-pass regen branch (lines 644, 652). ✅  
- `SpecGenerationService.GenerateAsync(_submissionId.Value)` — same call signature as the second-pass regen branch. ✅  
- `Nav.NavigateTo($"/nexus/{_submissionId.Value}")` — consistent with second-pass regen navigation. ✅  
- Snackbar pattern `$"Spec generation failed: {ex.Message}"` — consistent with pre-existing patterns throughout the file. ✅

---

## Issues Found

| Severity | File | ~Line | Issue | Fix |
|----------|------|-------|-------|-----|
| **Important** | NewSpecWizard.razor | 686 | `UpdateStatusAsync(Failed)` in inner catch is unguarded — if it throws, submission stays stuck in `Generating` | Wrap in nested try/catch (see below) |
| Nitpick | NewSpecWizard.razor | 685 | `ex.Message` may surface AI/backend internals (Bedrock endpoint URLs, quota messages) | Pre-existing pattern; log server-side, show generic message (tech debt ticket) |

---

## Critical Issues — 0

None.

---

## Important Issues — 1

### I1: `UpdateStatusAsync(Failed)` unguarded in inner catch

**File:** `NewSpecWizard.razor` (~line 683–690)  
**Category:** Correctness / error handling  

**Issue:** If `UpdateStatusAsync(Generating)` succeeds, `GenerateAsync` throws, and then `UpdateStatusAsync(Failed)` in the catch *also* throws, the exception escapes the inner catch, gets caught by the outer catch at line 693, and surfaces `"Submit failed: {ex.Message}"`. The submission record is left permanently stuck in `Generating` status with no automated recovery path.

This isn't theoretical — a transient DB connection failure or a timeout during the status update would trigger it.

**Current code:**
```csharp
catch (Exception ex)
{
    Snackbar.Add($"Spec generation failed: {ex.Message}", Severity.Error);
    await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Failed); // ← unguarded
    _isSubmitting = false;
    StateHasChanged();
    return;
}
```

**Fix:**
```csharp
catch (Exception ex)
{
    Snackbar.Add($"Spec generation failed: {ex.Message}", Severity.Error);
    try
    {
        await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Failed);
    }
    catch
    {
        // Best-effort — if this also fails, submission status stays as Generating.
        // Outer catch will surface the error; a manual status reset may be required.
    }
    _isSubmitting = false;
    StateHasChanged();
    return;
}
```

**Note:** The pre-existing second-pass regen branch (lines 649–657) has the identical vulnerability. Out of scope for this change but worth a follow-up ticket.

---

## Nitpicks — 1

**N1:** `ex.Message` in the snackbar (`"Spec generation failed: {ex.Message}"`, line 685) is consistent with the existing pattern throughout the file (see lines 394, 421, 651, 695) but for an AI/LLM service call, `ex.Message` could expose Bedrock endpoint details or quota messages to the UI. Pre-existing debt — not introduced by this change. Suggest a follow-up tech debt ticket to sanitize generation service exceptions and log server-side instead. Not blocking.

---

## Criterion Verification

| # | Criterion | Result | Notes |
|---|-----------|--------|-------|
| C-1 | `GenerateAsync` placement / fallthrough paths | ✅ PASS | All 3 early-return branches (re-discovery, regen, skip-regen) exit before reaching new code |
| C-2 | Status sequencing (`Generating` before `GenerateAsync`, `Failed` in catch) | ⚠️ WARN | Sequencing correct; catch unguarded against secondary DB failure → **blocking** |
| C-3 | Skip-regen path untouched | ✅ PASS | `return` present at line 673; `GenerateAsync` not called |
| C-4 | `_isResume && _hasChanges` branches untouched | ✅ PASS | Both passes return before new code |
| C-5 | Scope (only `.razor` changed) | ✅ PASS | No service-layer changes |
| I-6 | `_submissionId` null safety | ✅ PASS | Null guard at line 574; `_submissionId` not reassigned before new code |
| I-7 | Error message quality | ⚠️ WARN | Pre-existing pattern, low risk, not a regression; nitpick only |

---

## What to Fix

**Tony — one change required before merge:**

In the inner catch block of the new normal-flow try/catch (~line 683), wrap the `UpdateStatusAsync(Failed)` call in its own try/catch. See the fix diff in I1 above. This prevents a secondary DB failure from leaving the submission permanently stuck in `Generating` status.

The same pattern exists in the second-pass regen branch (pre-existing) — can address in a follow-up if you want to keep scope tight here.

---

_Review by Hawkeye — Cycle 1 — 2026-04-13_

---

## Cycle 2 — Verdict: PASS

**Commit:** `93181bc`  
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-04-13  
**Fix reviewed:** Wrap `UpdateStatusAsync(Failed)` in nested try/catch — both regen and new-submission catch blocks

---

### CC Review Summary

Ran adversarial CC review against `HandleSubmit()` in `NewSpecWizard.razor`. CC read the file, verified both patch locations, confirmed placement of cleanup assignments, and ran scope check. All four CRITICAL criteria pass cleanly. No false positives.

---

### Criteria Verification

| # | Criterion | Result | Notes |
|---|-----------|--------|-------|
| **C-1** | Both locations fixed | ✅ PASS | Location A (~line 649): regen catch. Location B (~line 690): generation catch. Both have nested try/catch. |
| **C-2** | Nested catch logs with context | ✅ PASS | Each `Console.Error.WriteLine` includes `_submissionId.Value`, `statusEx.Message`, and path-specific label (`"regen error"` vs `"GenerateAsync error"`) |
| **C-3** | `_isSubmitting = false` + `StateHasChanged()` execute unconditionally | ✅ PASS | Both assignments are AFTER the closing brace of the nested try/catch in both locations. `_regenInProgress = false` also correctly placed in Location A. |
| **C-4** | No regressions from Cycle 1 (C-1 through C-5) | ✅ PASS | All five cycle 1 checks confirmed still holding. |
| **I-5** | Scope: only `NewSpecWizard.razor` | ✅ PASS | `git show --name-only 93181bc` confirms single-file changeset. |

---

### Issues Found

None. The fix is correct, complete, and well-scoped.

---

### Positive Observations

- Both log messages include a `NEXUS:` prefix, submission ID, and the distinct path label — exactly enough to diagnose which catch block fired and which submission is affected without needing to correlate stack traces.
- Cleanup assignments (`_isSubmitting`, `_regenInProgress`, `StateHasChanged`) are correctly positioned in both locations — a subtle placement error would have left the form permanently locked if the nested catch fired.
- Scope discipline: zero collateral changes.

---

_Review by Hawkeye — Cycle 2 — 2026-04-13_
