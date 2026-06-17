# WI870 — Review Report: FAM OS Sprint 2
**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`
**Cycle:** 1 of 2
**Commit:** `315f728`
**Date:** 2026-03-19
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Summary

Sprint 2 is well-structured and mostly solid. All DI registrations, buildspec tags, MudChip T-types, and lifecycle wiring are correct. Two required defects must be fixed before ship:

1. `CloseOpportunityDialog` swallows `LifecycleValidationException` silently — no user feedback on close failure
2. `OpportunityWorkspace.OnParametersSetAsync` fires a double DB load on every initial render (standard Blazor gotcha)

Two minor issues noted (not blockers, suggest Sprint 3 backlog).

---

## Critical Checks

| Check | Status | Detail |
|-------|--------|--------|
| **A** — MudChip `T="string"` on all instances | ✅ PASS | All 4 MudChip instances carry `T="string"` (OpportunityWorkspace L29, MarketedPanel L24, NavMenu) |
| **B** — MudSelectItem Value types | ✅ PASS | CloseOpportunityDialog: `Value="@("...")"` (explicit string expressions); MarketedPanel: `Value="sub.Id"` (Guid) — both correct |
| **C** — `AddScoped<OpportunityService>()` in Program.cs | ✅ PASS | `Program.cs:110` confirmed |
| **D** — buildspec pushes `:dev-latest` AND `:latest` | ✅ PASS | post_build pushes `:dev-latest` (L16), then tags+pushes `:latest` (L17–18) |
| **E** — No files outside `famos/` modified | ✅ PASS | `git show --stat` filtered output — all 18 changed files are under `famos/` |
| **F** — `_Imports.razor` untouched | ✅ PASS | Not present in Sprint 2 diff |
| **G** — `LifecycleCommandService` untouched | ✅ PASS | Not modified; appears in commit message only |
| **H** — CloseOpportunityDialog surfaces error to user | ❌ **FAIL** | catch block is empty — error silently swallowed (see below) |
| **I** — OpportunityWorkspace double-load guard | ❌ **FAIL** | Both `OnInitializedAsync` and `OnParametersSetAsync` call `LoadAsync()` — double load on every init (see below) |
| **J** — MudChip in OpportunityWorkspace header has `T="string"` | ✅ PASS | `OpportunityWorkspace.razor:29` confirmed |
| **K** — All panels call `OnAdvanced.InvokeAsync()` after success | ✅ PASS | IntakePanel, MarketedPanel, QuotesReceivedPanel, ClientDecisionPanel (both paths), BindingPanel all confirmed. BoundPanel correctly has no `OnAdvanced` (terminal stage). |
| **L** — QuotesReceivedPanel handles empty Quotes | ⚠️ MINOR | Silent empty table — no message to user (non-crash, non-blocker) |
| **M** — MarketedPanel empty-state guard on carrier select | ⚠️ MINOR | Button disabled correctly, but no explanation message shown (non-blocker) |

---

## Required Fixes

### DEFECT-1: CloseOpportunityDialog — exception silently swallowed

**File:** `famos/src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor`
**Lines:** 44–47

**Current code:**
```csharp
catch (LifecycleValidationException)
{
    // Surface validation error in dialog — do not close
}
```

The comment says "Surface validation error" but the body is empty. No `ISnackbar` is injected, no error text field is set, no `<MudAlert>` is rendered. The dialog stays open with zero feedback to the user — they cannot know why the close operation failed.

**Required fix (choose one):**

Option A — Snackbar:
```csharp
// Inject at top:
[Inject] ISnackbar Snackbar { get; set; } = default!;

// In catch:
catch (LifecycleValidationException ex)
{
    Snackbar.Add(ex.Message, Severity.Error);
}
```

Option B — Inline error:
```csharp
// Add field:
private string? _error;

// In catch:
catch (LifecycleValidationException ex)
{
    _error = ex.Message;
}

// In markup, above buttons:
@if (_error is not null)
{
    <MudAlert Severity="Severity.Error" Class="mb-2">@_error</MudAlert>
}
```

---

### DEFECT-2: OpportunityWorkspace — double DB load on initial render

**File:** `famos/src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor`
**Lines:** 101–102

**Current code:**
```csharp
protected override async Task OnInitializedAsync() => await LoadAsync();
protected override async Task OnParametersSetAsync() => await LoadAsync();
```

Blazor calls **both** `OnInitializedAsync` and `OnParametersSetAsync` on first render of any component. This causes two sequential DB queries on every page open. Additionally, `OnParametersSetAsync` fires on every parent re-render (e.g. Snackbar state changes), causing spurious reloads throughout the session.

**Required fix:**
```csharp
private Guid _loadedId;

protected override async Task OnParametersSetAsync()
{
    if (Id == _loadedId) return;
    _loadedId = Id;
    await LoadAsync();
}
// Remove OnInitializedAsync override entirely
```

---

## Minor Issues (Suggest Sprint 3 Backlog)

### MINOR-1: QuotesReceivedPanel — silent empty state

**File:** `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/QuotesReceivedPanel.razor`

`MudTable Items="Opportunity.Quotes"` renders an empty table silently when no quotes have been recorded. The "Send Proposal" button is correctly disabled via `_recommendedId == Guid.Empty`, but there's no message explaining why. A newly-promoted opportunity landing here has no clear call to action.

**Suggested fix:** Add an empty-state row or `<MudAlert>` when `Opportunity.Quotes.Count == 0`: *"No quotes recorded yet. Use 'Record Quote' to add carrier responses."*

### MINOR-2: MarketedPanel — no empty-state explanation when no submissions exist

**File:** `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/MarketedPanel.razor`

If `Opportunity.Submissions` is empty, the MudSelect renders empty and "Record Quote" is permanently disabled (guard is correct). No message explains why. Similar UX dead-end as MINOR-1.

---

## Files Reviewed

1. `famos/src/FamOs.Web/Services/OpportunityService.cs` ✅
2. `famos/src/FamOs.Web/Components/Shared/SignalChip.razor` ✅
3. `famos/src/FamOs.Web/Components/Shared/OpportunityCard.razor` ✅
4. `famos/src/FamOs.Web/Components/Dialogs/OpportunityCreateDialog.razor` ✅
5. `famos/src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor` ❌ DEFECT-1
6. `famos/src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor` ❌ DEFECT-2
7. `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/IntakePanel.razor` ✅
8. `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/MarketedPanel.razor` ✅ (MINOR-2 noted)
9. `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/QuotesReceivedPanel.razor` ✅ (MINOR-1 noted)
10. `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/ClientDecisionPanel.razor` ✅
11. `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/BindingPanel.razor` ✅
12. `famos/src/FamOs.Web/Components/Pages/Opportunity/Panels/BoundPanel.razor` ✅
13. `famos/src/FamOs.Web/Components/Pages/Pipeline.razor` ✅
14. `famos/src/FamOs.Web/Components/Pages/Dashboard.razor` ✅
15. `famos/src/FamOs.Web/Program.cs` ✅
16. `famos/buildspec.yml` ✅

---

## Verdict: NEEDS-CHANGES

**2 required fixes. Return to Tony.**

Fix scope is tight — both are isolated, no architectural changes needed:
- DEFECT-1: ~5 lines in `CloseOpportunityDialog.razor`
- DEFECT-2: ~6 lines in `OpportunityWorkspace.razor`

No scope creep. Fix only these two. Resubmit for Cycle 2.
