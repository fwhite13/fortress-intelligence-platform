# QA Report: NEXUS ADO #1804 — Submit Flow GenerateAsync Fix

### Verdict: ✅ PASS

---

### Environment
- **File:** `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor`
- **Commit:** `93181bc`
- **Method:** Code verification at HEAD
- **Test Type:** Static code analysis (no runtime — not applicable; change is logic-only in Blazor component)
- **Test Start:** 2026-04-13 19:09 EDT
- **Tester:** Natasha Romanoff (Black Widow / qa-analyst)

---

### Test Cases

| TC | Description | Verdict | Notes |
|----|-------------|---------|-------|
| TC1 | GenerateAsync called in normal flow path | ✅ PASS | Lines 684–705 |
| TC2 | Nested try/catch on status reset in both catch blocks | ✅ PASS | Lines 642–670 (regen), 684–704 (new submission) |
| TC3 | Skip-regen path untouched (no GenerateAsync call) | ✅ PASS | Lines 674–681 |

---

### Detailed Findings

#### TC1 — GenerateAsync called in normal flow path ✅ PASS

**Lines 683–705** — New submission path (after all early-return branches):

```csharp
// New submission (or resume with no prior spec) — generate spec
try
{
    await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Generating);  // line 686
    await SpecGenerationService.GenerateAsync(_submissionId.Value);                               // line 687
    // GenerateAsync sets status to AwaitingReview internally
}
catch (Exception ex) { ... }
Nav.NavigateTo($"/nexus/{_submissionId.Value}");  // line 705
```

Execution order verified:
1. `UpdateStatusAsync(Generating)` — line 686
2. `GenerateAsync(_submissionId.Value)` — line 687
3. `Nav.NavigateTo(...)` — line 705 (only reached if try block succeeds)

The old bug (bare `Nav.NavigateTo` without calling `GenerateAsync`) is **not present**.

---

#### TC2 — Nested try/catch on status reset ✅ PASS

**Regen path catch block (lines 649–664):**
```csharp
catch (Exception ex)
{
    Snackbar.Add($"Spec regeneration failed: {ex.Message}", Severity.Error);
    try                                                                          // ← nested try
    {
        await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Failed);
    }
    catch (Exception statusEx)                                                   // ← nested catch
    {
        Console.Error.WriteLine($"NEXUS: Failed to set submission {_submissionId.Value} to Failed after regen error: {statusEx.Message}");
    }
    ...
    return;
}
```

**New-submission path catch block (lines 690–704):**
```csharp
catch (Exception ex)
{
    Snackbar.Add($"Spec generation failed: {ex.Message}", Severity.Error);
    try                                                                          // ← nested try
    {
        await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.Failed);
    }
    catch (Exception statusEx)                                                   // ← nested catch
    {
        Console.Error.WriteLine($"NEXUS: Failed to set submission {_submissionId.Value} to Failed after GenerateAsync error: {statusEx.Message}");
    }
    ...
    return;
}
```

Both catch blocks have nested try/catch around `UpdateStatusAsync(Failed)`. A double-failure will log to `Console.Error` instead of leaving the submission stuck in `Generating`. ✅

---

#### TC3 — Skip-regen path untouched ✅ PASS

**Lines 674–681:**
```csharp
if (_isResume && !_hasChanges && _existingSpecDocument != null)
{
    // Skip-regen path: existing spec, no changes — persist narrative then promote Draft → AwaitingReview
    await SubmissionService.UpdateNarrativeAsync(_submissionId.Value, _narrativeText);
    await SubmissionService.UpdateStatusAsync(_submissionId.Value, SubmissionStatus.AwaitingReview);
    Nav.NavigateTo($"/nexus/{ResumeSubmissionId}");
    return;
}
```

Branch condition exactly matches spec: `_isResume && !_hasChanges && _existingSpecDocument != null`.  
No call to `GenerateAsync` in this branch. Returns early after navigating. ✅

---

### Summary

All three test cases pass. The fix is correctly implemented:

- **TC1:** The new-submission path now gates `Nav.NavigateTo` behind a successful `UpdateStatusAsync(Generating)` + `GenerateAsync()` call sequence — the core bug is fixed.
- **TC2:** Both catch blocks (regen path and new-submission path) wrap the `UpdateStatusAsync(Failed)` recovery call in a nested try/catch, preventing double-failure deadlock on `Generating` status.
- **TC3:** The skip-regen early-return branch is structurally unchanged and does not invoke `GenerateAsync`.

**Overall: PASS** — safe to mark ADO #1804 Done.
