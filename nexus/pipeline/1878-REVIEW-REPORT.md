# Review Report — ADO #1878 / #1879 / #1880

**Commit:** `e1a44e5`
**Reviewer:** Hawkeye (code-reviewer)
**Date:** 2026-04-14
**Cycle:** 1
**Risk:** High — core spec gen path

---

## Verdict: NEEDS-CHANGES

**1 blocker (C3/C7). 3 required fixes.**

---

## CC Review Summary

CC ran adversarial analysis across `NewSpecWizard.razor`, `DiscoveryService.cs`, `DiscoveryStep.razor`, `NexusDbContext.cs`, and `DiscoverySession.cs`. Six of seven checks found real issues. One was false-positive free (C4 — EF mapping clean). All findings confirmed real.

---

## Consistency Audit

| Check | Files | Result |
|-------|-------|--------|
| EF mapping `SkippedByUser` → `skipped_by_user` | `NexusDbContext.cs:169` | ✅ mapped |
| Migration creates column | `20260407180206_AddDiscoveryConversation.cs:35` | ✅ present |
| `_hasChanges` used in `HandleSubmit` guard | `NewSpecWizard.razor` | ⚠️ semantics wrong (see C3) |
| `ApplyResumeChangesAsync` call sites | `ConfirmRediscovery` + `HandleSubmit` second pass | ⚠️ called twice (C1) |

---

## Issues Found

### Critical / Blocker

#### C3 + C7 — `_hasChanges` Answered-only triggers re-discovery dialog — destroys existing answers

- **Files:** `NewSpecWizard.razor` — `_hasChanges` property (L290–295), `HandleSubmit` (L595–603), `ConfirmRediscovery()` (L686–697)
- **Category:** Correctness / UX / data-destructive

**Root cause:** `_hasChanges` is true whenever `_discoverySession.Status == Answered`, even on page load with zero user changes. In `HandleSubmit`, `_isResume && _hasChanges && !_regenPending` → shows `_showRediscoveryConfirm` dialog. User clicks "Re-run Discovery" → `ConfirmRediscovery()` → `SupersedeSessionAsync()` destroys their existing answers.

**Scenario:**
1. User has an answered-discovery submission that needs a regen
2. Opens wizard, makes **zero changes**, clicks Submit
3. Dialog: "Adding files or changing the narrative will re-run Discovery. Your existing answers will be cleared."
4. The only forward path is "Re-run Discovery" — which wipes their answers
5. They cannot regenerate with their existing answers

Tony intended Answered status to flag that regen is needed (#1878a in commit msg), but conflated "needs regen" with "needs re-discovery." The `Answered` condition should trigger regen, NOT re-discovery.

**Fix:** Split `_hasChanges`:

```diff
- private bool _hasChanges => _isResume && (
-     _narrativeText.Trim() != _originalNarrative.Trim() ||
-     _filesToDelete.Count > 0 ||
-     _uploadedFiles.Any(f => !_originalFileIds.Contains(f.Id)) ||
-     (_discoverySession != null && _discoverySession.Status == DiscoverySessionStatus.Answered)
- );
+ private bool _hasContentChanges => _isResume && (
+     _narrativeText.Trim() != _originalNarrative.Trim() ||
+     _filesToDelete.Count > 0 ||
+     _uploadedFiles.Any(f => !_originalFileIds.Contains(f.Id))
+ );
+
+ private bool _hasChanges => _hasContentChanges ||
+     (_isResume && _discoverySession?.Status == DiscoverySessionStatus.Answered);
```

Then in `HandleSubmit`, only show the re-discovery dialog for **content** changes:

```diff
  if (_isResume && _hasChanges)
  {
      if (!_regenPending)
      {
-         _showRediscoveryConfirm = true;
+         if (_hasContentChanges)
+         {
+             _showRediscoveryConfirm = true;
+             _isSubmitting = false;
+             StateHasChanged();
+             return;
+         }
+         // Answered-only: no content changed — fall through to regen with existing answers
+         // (continue to the second-pass regen logic below — set _regenPending first)
+         _regenPending = true;
```

**Severity: BLOCKER — data-destructive.**

---

### Important

#### C2 — `_showRediscoveryConfirm` not reset in `BackToStep2Discovery()`

- **File:** `NewSpecWizard.razor` — `BackToStep2Discovery()` (~L502)
- **Category:** Correctness / UX

`BackToStep2Discovery()` currently:
```csharp
private void BackToStep2Discovery()
{
    _isSubmitting = false;
    _activeStep = 2;
}
```

**Problem:** User clicks Submit (dialog appears, `_showRediscoveryConfirm = true`), then clicks Back instead of Cancel. `_showRediscoveryConfirm` is never cleared. When they return to Step 3, the confirmation dialog renders immediately before they've done anything.

**Fix:**
```diff
  private void BackToStep2Discovery()
  {
      _isSubmitting = false;
+     _showRediscoveryConfirm = false;
      _activeStep = 2;
  }
```

**Severity: Important — stale dialog on return.**

#### C6 — `_regenPending` not reset on regen error

- **File:** `NewSpecWizard.razor` — `HandleSubmit` error catch (~L619–632)
- **Category:** Correctness

After a regen failure, `_regenPending` stays `true`. Next Submit attempt skips the confirmation dialog and retries regen. If the user navigates Back to Step 2 after a failure and makes additional changes, the dialog won't appear on their next Submit.

**Fix:**
```diff
  catch (Exception ex)
  {
      Snackbar.Add($"Spec regeneration failed: {ex.Message}", Severity.Error);
      ...
+     _regenPending = false;
      _regenInProgress = false;
      _isSubmitting = false;
      StateHasChanged();
      return;
  }
```

**Severity: Important — incorrect behavior after error.**

---

### Nitpicks

#### C1 — Double `ApplyResumeChangesAsync()` call (harmless but messy)

`ApplyResumeChangesAsync()` is called in both `ConfirmRediscovery()` and the `_regenPending == true` branch of `HandleSubmit`. The second call is safe because `_filesToDelete.Clear()` prevents double-deletion and narrative update is idempotent. But it's a wasteful extra DB write.

**Fix:** Remove `await ApplyResumeChangesAsync()` from the second-pass branch — the work was already done in `ConfirmRediscovery()`.

**Not blocking.**

#### C5 — HandleSkip re-calls SkipDiscoveryAsync on already-skipped session

`DiscoveryStep.razor`: When `Session.SkippedByUser == true`, the fallback UI renders. Clicking Continue calls `HandleSkip()` → `SkipDiscoveryAsync()` again. Idempotent (sets same values), but wastes a DB write. Could add a guard: `if (!Session.SkippedByUser)` before calling the service. Not blocking.

---

## Passing Checks

#### C4 — EF mapping for `SkippedByUser` ✅

`NexusDbContext.cs:169` has `entity.Property(e => e.SkippedByUser).HasColumnName("skipped_by_user")`. Migration `20260407180206_AddDiscoveryConversation.cs` creates the `tinyint(1)` column. Model snapshot is current. **Clean.**

#### `BuildSpecContextAsync` `SkippedByUser` guard ✅

The change from `session.Status == Skipped` to `session.SkippedByUser` is correct and intentional. `SkippedByUser` distinguishes explicit user skips from system-triggered failures that also land in `Skipped` status.

#### `DiscoveryStep` render guard (`QuestionsReady`/`Answered` exclusion) ✅

The updated fallback condition correctly prevents `QuestionsReady` and `Answered` sessions from falling through to the fallback alert. Sessions with real questions now render the question list as expected.

---

## What Tony Must Fix Before Merge

| # | File | Fix |
|---|------|-----|
| 1 (BLOCKER) | `NewSpecWizard.razor` | Split `_hasChanges` → `_hasContentChanges` + `_hasChanges`; only show re-discovery dialog when `_hasContentChanges` is true |
| 2 | `NewSpecWizard.razor` | Add `_showRediscoveryConfirm = false` to `BackToStep2Discovery()` |
| 3 | `NewSpecWizard.razor` | Add `_regenPending = false` to regen error catch block |
| 4 (nitpick) | `NewSpecWizard.razor` | Remove duplicate `ApplyResumeChangesAsync()` from second-pass branch |

---

## Spec Fidelity

- **#1878 (`_hasChanges` includes Answered):** Partially correct — the intent is right (Answered should trigger regen), but the implementation conflates regen with re-discovery. The fix preserves the intent while removing the destructive behavior.
- **#1879 (confirm dialog instead of silent redirect):** Correct concept, correct implementation for the content-changed case. Broken only when Answered-only triggers it.
- **#1880 (render guard fixes):** ✅ Correct and clean.
- **#1878b (SkippedByUser guard in BuildSpecContextAsync):** ✅ Correct.

---

_Hawkeye — cycle 1 — 2026-04-14_
