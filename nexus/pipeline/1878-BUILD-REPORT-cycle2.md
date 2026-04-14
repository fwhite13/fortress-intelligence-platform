# Build Report — ADO #1878 / #1879 / #1880 (Cycle 2)

**Commit:** `643fda4`
**Branch:** main
**Build:** dotnet build → 0 errors

## Files changed

| File | Change |
|------|--------|
| `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` | 4 bug fixes — 17 insertions, 9 deletions |

## Fixes applied

### FIX 1 — Split `_hasChanges` into `_hasContentChanges` + `_hasChanges` (line ~287)
- `_hasContentChanges`: narrative diff, file deletions, new uploads — does NOT include Answered status
- `_hasChanges`: `_hasContentChanges || (isResume && session.Status == Answered)` — backward-compatible for UI alert
- Root cause eliminated: Answered-only resume no longer routes through `_showRediscoveryConfirm`

### FIX 2 — HandleSubmit: dialog guard now gated on `_hasContentChanges` (line ~598)
- Old code: `if (!_regenPending)` always set `_showRediscoveryConfirm = true` → data-destructive
- New code: only shows dialog when `_hasContentChanges` is true; Answered-only path sets `_regenPending = true` and falls through to regen
- Non-destructive forward path now exists for Answered-only resume

### FIX 3 — BackToStep2Discovery: resets `_showRediscoveryConfirm = false` (line ~504)
- Prevents stale dialog state when user navigates back from Step 3 to Step 2 and returns

### FIX 4 — Regen error catch: adds `_regenPending = false` (line ~633)
- Without this, a regen failure left `_regenPending = true`, making subsequent Submit skip the dialog even with content changes
- Now: failure resets both `_regenPending` and `_regenInProgress`

### FIX 5 — Remove duplicate `ApplyResumeChangesAsync()` in second-pass regen (line ~619)
- `ConfirmRediscovery()` already calls `ApplyResumeChangesAsync()` before routing to the regen path
- Second call in the regen try-block removed → prevents double-delete of flagged files

## CC invocation
```
cat /tmp/tony-1878-cycle2-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

## Self-review checklist
- [x] `_hasContentChanges` excludes Answered status
- [x] `_hasChanges` uses `_hasContentChanges` as base
- [x] `HandleSubmit` only shows dialog when `_hasContentChanges`
- [x] Answered-only path sets `_regenPending = true` and falls through
- [x] `BackToStep2Discovery` resets `_showRediscoveryConfirm`
- [x] Error catch resets `_regenPending`
- [x] `ApplyResumeChangesAsync` called only once per flow (in `ConfirmRediscovery`)
- [x] `dotnet build` → 0 errors

## Clean files (untouched, cycle 1 verified)
- `Services/Discovery/DiscoveryService.cs` ✅
- `Components/Nexus/DiscoveryStep.razor` ✅
- `NexusDbContext.cs` / `DiscoverySession.cs` ✅
