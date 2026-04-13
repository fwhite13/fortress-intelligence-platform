# Build Report — ADO #1726
**Date:** 2026-04-13  
**Engineer:** Tony Stark  
**Commit:** `6030c7e`

---

## What was built
Separated `OnCompleted` and `OnSkipped` callbacks in `NewSpecWizard.razor`. Previously both pointed to `GoToStep3Confirm`, making it impossible for the wizard to distinguish a completed discovery (answers saved) from a skipped one. Added `HandleDiscoveryCompleted` for the answered path.

---

## Root Cause
`DiscoveryStep.razor` logic was sound — `HandleContinue()` correctly calls `SaveAnswersAsync`, and `HandleSkip()` correctly calls `SkipDiscoveryAsync`. The problem was upstream in the wizard: both `OnCompleted` and `OnSkipped` were bound to the same `GoToStep3Confirm` handler, so even when Continue was clicked and answers were saved, the wizard had no separate code path to handle it distinctly. (The logs showed skip because the *session* status was `Skipped` — meaning `CanContinue` was false on first entry, forcing users to click "Generate Spec Anyway".)

---

## Files changed
- `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor`
  - `OnCompleted="GoToStep3Confirm"` → `OnCompleted="HandleDiscoveryCompleted"`
  - Added `HandleDiscoveryCompleted()` method: sets `_activeStep = 3`, does NOT call skip

---

## CC sessions run
1 session, sequential (shared file with #1727)

---

## Acceptance criteria verification
- [x] `OnCompleted` bound to `HandleDiscoveryCompleted` — confirmed in diff
- [x] `OnSkipped` still bound to `GoToStep3Confirm` — confirmed in diff  
- [x] `HandleDiscoveryCompleted` sets `_activeStep = 3` without any skip/session logic — confirmed
- [x] `dotnet build` — 0 errors, 0 warnings

---

## Known edge cases / things Clint should scrutinize
- `HandleDiscoveryCompleted` is synchronous (`Task.CompletedTask`) — correct since answer saving already happened in `DiscoveryStep.HandleContinue()`. No state to persist here.
- The resume/regen path in `HandleSubmit` is unaffected — it calls `GoToStep2Discovery` directly, bypassing the DiscoveryStep callback.
- `CanContinue` in `DiscoveryStep` now correctly gates whether the Continue button is enabled (all blocking questions answered). With `OnParametersSet` from #1727 loading prior answers, this will resolve the root cause of users being forced to skip.

---

## How to test locally
1. Create a new submission through the NEXUS wizard
2. On Step 3 (Discovery), answer all blocking questions and click **Continue**
3. Verify step advances to Review (Step 4) without triggering `SkipDiscoveryAsync` in logs
4. Verify session status is `Answered` (not `Skipped`) in DB
