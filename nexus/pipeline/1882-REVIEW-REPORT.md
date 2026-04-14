# Review Report — ADO #1882

**Commit:** 7f1c3d5
**Reviewer:** Hawkeye
**Cycle:** 1
**Verdict:** PASS

---

## Checks

| # | Check | Result |
|---|-------|--------|
| 1 | Terminal status list complete | ✅ |
| 2 | SupersedeSessionAsync idempotent for already-Superseded | ✅ |
| 3 | _discoverySession null path to InitiateDiscovery | ✅ |
| 4 | No double-supersede from ConfirmRediscovery | ✅ |
| 5 | Active session path unchanged | ✅ |
| 6 | Exception handling acceptable | ⚠️ |

---

## Check Detail

### Check 1 — Terminal status list complete ✅

`DiscoverySessionStatus.cs` defines 6 constants. All are correctly classified:

| Status | Type | In guard? |
|--------|------|-----------|
| Pending | Active (in progress) | ❌ excluded ✅ |
| QuestionsReady | Active (ready for user) | ❌ excluded ✅ |
| Answered | Active (answered) | ❌ excluded ✅ |
| Skipped | Terminal | ✅ included ✅ |
| Failed | Terminal | ✅ included ✅ |
| Superseded | Terminal | ✅ included ✅ |

No orphaned status, no gap.

---

### Check 2 — SupersedeSessionAsync idempotent for already-Superseded ✅

`DiscoveryService.cs:190–204` — `SupersedeSessionAsync` has no status filter on its query. For an already-Superseded session it:
- Finds the session by ID ✅
- Sets `Status = Superseded` (EF no-op) ✅
- Sets `UpdatedAt = DateTime.UtcNow` (harmless) ✅
- `SaveChangesAsync` completes without error ✅

The `catch` is technically redundant for the re-supersede path but is correct defensive coverage for DB connectivity failures.

---

### Check 3 — _discoverySession null path to InitiateDiscovery ✅

After guard nulls `_discoverySession`:
- `if (_discoverySession == null)` → true → `Task.Run` fires ✅
- `InitiateDiscoveryAsync` is awaited inside the task ✅
- 60s poll loop (same as fresh session) runs correctly ✅
- `finally` block calls `InvokeAsync(StateHasChanged)` — no race condition with UI ✅

---

### Check 4 — No double-supersede from ConfirmRediscovery ✅

`ConfirmRediscovery()` sets `_activeStep = 2` directly. This triggers only a re-render of `@if (_activeStep == 2)` — it does **not** invoke `GoToStep2Discovery()`.

Verified: no `OnAfterRender`, `OnParametersSet`, or reactive observer in this file retriggers `GoToStep2Discovery` on step change. The button at line 140 is the only entry point for that method (user gesture only). No double-supersede path exists.

---

### Check 5 — Active session path unchanged ✅

For `Pending`, `QuestionsReady`, `Answered`:
- Guard condition `Status is Skipped or Failed or Superseded` → false → skipped
- `if (_discoverySession == null)` → false → `else` branch → `_discoveryLoading = false`
- Else comment explicitly names these statuses. Behavior identical to pre-patch.

---

### Check 6 — Exception handling ⚠️

`catch { /* non-fatal — proceed with new session */ }` is intentionally silent. Correct behavior: if archiving the stale session fails, null the ref and proceed — the orphaned record is harmless.

**Gap:** No logging. However, this pattern is consistent throughout the file — no other catch sites in this component log either (lines 343, 486, 737). Not a regression introduced by this change; a pre-existing observability gap across the component.

---

## Adversarial Checks (additional)

### Thread Safety ⚠️ — Pre-existing, not introduced here
The "Next: Discovery" button is only disabled during `_isUploading`, not during `_discoveryLoading`. Rapid double-clicks before `StateHasChanged` fires could queue two `GoToStep2Discovery` invocations, potentially firing two `InitiateDiscoveryAsync` calls. Blazor Server processes circuit events serially but not atomically across the full async method.

- **Impact:** Low probability; not data-corrupting (poll loop settles on latest session)
- **Mitigation available:** `if (_discoveryLoading) return;` at method entry
- **Not blocking:** Pre-existing gap, not introduced by this change. Worth a follow-up WI.

### Loading state recovery ✅
`try { SupersedeSessionAsync } catch { }` → falls through to Task.Run → Task.Run has `finally { _discoveryLoading = false; InvokeAsync(StateHasChanged); }`. No stuck spinner path.

### ConfirmRediscovery awaits InitiateDiscoveryAsync synchronously ✅
`ConfirmRediscovery` awaits `InitiateDiscoveryAsync` on the Blazor context (not in a Task.Run). `InitiateDiscoveryAsync` does a fast DB write then fires Bedrock generation as its own fire-and-forget Task.Run — it does not block the caller on the LLM call. The only effect is that session creation is guaranteed committed before the poll loop starts. Functionally equivalent to GoToStep2Discovery's approach; marginally safer (avoids race where first poll tick returns null).

---

## Issues Found

None blocking. Three pre-existing warnings noted:

| # | Warning | Severity | Status |
|---|---------|----------|--------|
| W1 | Silent catch on SupersedeSessionAsync — no log | Low | Pre-existing pattern |
| W2 | No double-click guard on "Next: Discovery" button | Low-Medium | Pre-existing gap |
| W3 | ConfirmRediscovery vs. GoToStep2Discovery: inconsistent async style for InitiateDiscoveryAsync | Cosmetic | Pre-existing |

---

## Verdict Rationale

All six correctness checks pass. The terminal-status guard correctly identifies exactly the three statuses that require re-initiation (Skipped, Failed, Superseded), `SupersedeSessionAsync` is idempotent under double-call, the null-path to `InitiateDiscoveryAsync` is correctly wired, `ConfirmRediscovery` cannot cause a double-supersede because it reaches step 2 via direct assignment not via `GoToStep2Discovery`, active sessions (Pending, QuestionsReady, Answered) continue to reach the else branch unaffected, and the loading state is always cleaned up via the Task.Run finally block. The silent catch is consistent with the file's established pattern. No correctness defects were found. The fix is targeted, minimal-footprint, and solves the stated root cause.
