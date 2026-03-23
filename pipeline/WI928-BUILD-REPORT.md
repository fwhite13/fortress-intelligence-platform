# Build Report — WI#928: Close Opportunity Blazor Crash

**Builder:** Tony Stark  
**Date:** 2026-03-20  
**Commit:** `ccf17e7`  
**Branch:** `main`  
**Risk Level:** Low  
**CC Invocation:** Direct file edits (surgical 3-fix, ~15 lines — CC pipe not required for precision edits of this scope)

---

## Summary

Fixed the Close Opportunity crash in FAM OS. The dialog was crashing at render time due to a MudBlazor v7 generic type mismatch between `CloseReason?` (nullable) bound field and non-nullable `CloseReason` item values. Added defensive try/catch in two locations to prevent future circuit kills.

---

## Changes Made

### File 1: `famos/src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor`

**Fix 1A — MudSelect nullable enum type mismatch (THE CRASH)**
- Added `T="CloseReason?"` to `<MudSelect>` tag
- Added `T="CloseReason?"` to all 6 `<MudSelectItem>` tags
- Cast all item values to nullable: `@((CloseReason?)CloseReason.X)`
- Resolves: `InvalidOperationException` during component rendering in MudBlazor v7

**Fix 1B — GetUserIdAsync() inside try/catch**
- Moved `var userId = await UserSession.GetUserIdAsync()` inside the try block
- Added second `catch (Exception ex)` clause after `LifecycleValidationException`
- If auth state is unavailable, the error now surfaces as a Snackbar instead of propagating up

### File 2: `famos/src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor`

**Fix 2 — Wrap CloseOpportunity() in try/catch**
- Wrapped entire `CloseOpportunity()` body in `try { } catch (Exception ex) { }`
- Catch logs error to Snackbar and calls `StateHasChanged()`
- Matches the existing `ParkOpportunity()` pattern in the same file (lines 175–187)
- Prevents unhandled async exceptions from killing the Blazor Server circuit (SignalR drop → reconnect spinner)

---

## Verification

```
# Fix 1A — T="CloseReason?" present on MudSelect + all MudSelectItems
11: <MudSelect T="CloseReason?" @bind-Value="_reason" ...>
12: <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.NotQuoted)">
13: <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.PriceTooHigh)">
14: <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.LostToCompetitor)">
15: <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.ClientDeclinedCoverage)">
16: <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.PolicyLapsed)">
17: <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.Other)">

# Fix 1B — GetUserIdAsync inside try, broadened catch
46: try
48: var userId = await UserSession.GetUserIdAsync()
53: catch (LifecycleValidationException ex)
57: catch (Exception ex)

# Fix 2 — CloseOpportunity() try/catch
188: private async Task CloseOpportunity()
190: try
202: catch (Exception ex)
```

---

## Self-Review Checklist

- [x] Fix 1A: `T="CloseReason?"` on `MudSelect` and all 6 `MudSelectItem` tags
- [x] Fix 1A: All item values cast to nullable `@((CloseReason?)...)`
- [x] Fix 1B: `GetUserIdAsync()` moved inside try block
- [x] Fix 1B: Catch broadened to `Exception ex` (second catch clause)
- [x] Fix 2: `CloseOpportunity()` wrapped in try/catch matching `ParkOpportunity()` pattern
- [x] Fix 2: Catch calls `StateHasChanged()` to keep UI consistent
- [x] No scope creep — only the 3 specified fixes
- [x] No DB changes, no migration changes, no service layer changes
- [x] Committed: `ccf17e7` — "WI928: fix Close Opportunity Blazor crash — MudSelect nullable enum T + try/catch"
- [x] Pushed: `origin/main`

---

## Risk Assessment

**Low.** Two isolated Razor component changes, no domain logic touched.  
- `CloseOpportunityDialog` was non-functional before this fix — zero regression risk
- `CloseOpportunity()` try/catch is purely defensive, matching established codebase pattern
- No EF Core, no migrations, no service layer, no API contracts changed

---

*Tony out. Clint's up.*
