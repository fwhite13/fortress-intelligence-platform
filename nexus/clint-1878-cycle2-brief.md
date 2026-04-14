# CC Review Brief — ADO #1878 / #1879 / #1880 (Cycle 2)

You are performing an adversarial code review of `NewSpecWizard.razor` at commit 643fda4.
This is cycle 2. Cycle 1 returned NEEDS-CHANGES. Tony claims to have applied all 5 fixes.

## File to review
`/home/fredw/projects/fip/nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor`

Read the entire file. Then verify each fix below.

---

## Fix verification checklist

### FIX 1 & 2 — _hasChanges split + HandleSubmit dialog guard

**Verify:**
1. There is a property `_hasContentChanges` that includes narrative diff, file deletions, new uploads — and does NOT include `DiscoverySessionStatus.Answered`.
2. There is a property `_hasChanges` that is `_hasContentChanges || (isResume && session.Status == Answered)`.
3. In `HandleSubmit`, the re-discovery dialog (`_showRediscoveryConfirm = true`) is only set when `_hasContentChanges` is true, NOT when `_hasChanges` is true.
4. The Answered-only path (no content changes): sets `_regenPending = true` and does NOT call `SupersedeSessionAsync` and does NOT show the dialog.
5. Trace the Answered-only flow completely: first submit with `_regenPending=false` and `_hasContentChanges=false` → what happens step by step? Does it regen the spec without destroying answers? Does it eventually call `GenerateAsync`?
6. Does the Answered-only first-pass set `_regenPending = true` before falling through, and does it actually reach `GenerateAsync`? (There should be NO `return` statement after `_regenPending = true` in the Answered-only path.)
7. **Critical check**: After `_regenPending = true` is set in the Answered-only path, does execution fall out of the `if (_isResume && _hasChanges)` block and reach the spec generation path at the bottom? Trace the exact code path.

### FIX 3 — BackToStep2Discovery resets _showRediscoveryConfirm

**Verify:**
1. `BackToStep2Discovery()` sets `_showRediscoveryConfirm = false`.
2. No other state left dirty that could cause the dialog to re-appear spuriously.

### FIX 4 — Regen error catch resets _regenPending

**Verify:**
1. In the SECOND PASS regen block (the `else` branch inside `if (_isResume && _hasChanges)`) — the catch block resets BOTH `_regenPending = false` AND `_regenInProgress = false`.
2. Report exact lines where `_regenPending = false` appears in the catch.

### FIX 5 — Duplicate ApplyResumeChangesAsync removed

**Verify:**
1. `ApplyResumeChangesAsync()` is called in `ConfirmRediscovery()` — confirm this is present.
2. `ApplyResumeChangesAsync()` is NOT called in the second-pass regen `else` block inside `HandleSubmit`.
3. List ALL call sites of `ApplyResumeChangesAsync` in the file.

---

## Additional checks

### Regression check
1. Does the content-changed dialog path still work? i.e., if `_hasContentChanges` is true, does `HandleSubmit` still show the dialog?
2. After `ConfirmRediscovery()` is called (user confirms re-discovery), does the flow eventually land on the second-pass regen `else` branch on the next Submit?
3. Is there any scenario where `SupersedeSessionAsync` could be called on an Answered-only resume? (It must NOT be called in that case.)

### Code quality
1. Any new TODO/debug artifacts?
2. Any dangling state variable or unreachable code introduced?

---

## Report format

For each fix: VERIFIED ✅ or BROKEN ❌, with exact line numbers.

For the Answered-only flow trace: show the exact step-by-step execution path.

Flag any new issues not in the cycle 1 report.
