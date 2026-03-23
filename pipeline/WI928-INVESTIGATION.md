# WI#928 Investigation — Close Opportunity Bug

**Investigator:** Hawkeye (Clint Barton)  
**Date:** 2026-03-20  
**Method:** DB state verification + full code-path trace via grep/git log

---

## DB State

- **CloseReason column:** EXISTS (`INT NULL` on `opportunities`)
- **CloseNotes column:** EXISTS (`LONGTEXT NULL` on `opportunities`)
- **LastStageTransitionAt column:** EXISTS (`DATETIME NULL` on `opportunities`)

Migration gap hypothesis **ELIMINATED**. All three WI903 columns are present in Aurora dev. The Program.cs try/catch migration block ran successfully.

---

## Root Cause

**Two issues found. Issue 1 is the crash. Issue 2 is the silent exception swallower.**

### Issue 1 — CRASH: `MudSelect<T>` nullable enum type mismatch (render-time exception)

**File:** `src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor`  
**Line:** 11

```razor
<MudSelect @bind-Value="_reason" Label="Close Reason *" Required="true" Class="mb-3">
    <MudSelectItem Value="CloseReason.NotQuoted">...</MudSelectItem>
    ...
```

`_reason` is declared as `CloseReason?` (nullable). In MudBlazor v7, `MudSelect<T>` infers `T = CloseReason?` from the bound field. However, the `MudSelectItem` values are typed as `CloseReason` (non-nullable), creating a generic type mismatch (`CloseReason` vs `CloseReason?`). This causes an `InvalidOperationException` during component rendering.

**Comparison:** `AddContactDialog` (works) uses `private ContactType _contactType = ContactType.Primary` — **non-nullable enum with default value**. Items bound as `ContactType.X` match exactly.

The exception propagates back through `DialogService.ShowAsync<CloseOpportunityDialog>()` and then escapes uncaught from `CloseOpportunity()` in `OpportunityWorkspace`.

### Issue 2 — CIRCUIT KILL: No try/catch in `CloseOpportunity()` event handler

**File:** `src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor`  
**Lines:** 188–199

```csharp
private async Task CloseOpportunity()
{
    var dialog = await DialogService.ShowAsync<CloseOpportunityDialog>(...);
    var result = await dialog.Result;
    ...
}
```

**No try/catch.** In Blazor Server, an unhandled exception in an `async Task` event handler kills the Blazor circuit (SignalR connection drop → "Reconnecting..." spinner). Issue 1's exception has nowhere to land, causing the visible session disconnect. ParkOpportunity() (same file, line 173) has a try/catch — this is the established pattern. CloseOpportunity() was added without it.

### Issue 3 — SECONDARY: `GetUserIdAsync()` outside try/catch in dialog Submit()

**File:** `src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor`  
**Lines:** 47–58

```csharp
private async Task Submit()
{
    if (_reason == null) return;
    var userId = await UserSession.GetUserIdAsync();  // ← outside try/catch
    try
    {
        await Lifecycle.CloseOpportunityAsync(...);
        ...
    }
    catch (LifecycleValidationException ex) { ... }  // ← only catches LifecycleValidationException
}
```

If `GetUserIdAsync()` throws (e.g., auth state unavailable), the exception is uncaught in Submit() and propagates to MudBlazor's event dispatcher, potentially crashing the circuit on submit. Lower priority — fix as part of the same PR.

---

## Fix Required

### Fix 1 — Primary (CloseOpportunityDialog.razor) — THE CRASH

Change the `MudSelect` to use explicit `T="CloseReason?"` and cast item values to nullable:

**BEFORE (lines 11–18):**
```razor
<MudSelect @bind-Value="_reason" Label="Close Reason *" Required="true" Class="mb-3">
    <MudSelectItem Value="CloseReason.NotQuoted">Not Quoted — carrier(s) declined</MudSelectItem>
    <MudSelectItem Value="CloseReason.PriceTooHigh">Price Too High — client declined on premium</MudSelectItem>
    <MudSelectItem Value="CloseReason.LostToCompetitor">Lost to Competitor</MudSelectItem>
    <MudSelectItem Value="CloseReason.ClientDeclinedCoverage">Client Declined Coverage</MudSelectItem>
    <MudSelectItem Value="CloseReason.PolicyLapsed">Policy Lapsed — missed renewal window</MudSelectItem>
    <MudSelectItem Value="CloseReason.Other">Other</MudSelectItem>
</MudSelect>
```

**AFTER:**
```razor
<MudSelect T="CloseReason?" @bind-Value="_reason" Label="Close Reason *" Required="true" Class="mb-3">
    <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.NotQuoted)">Not Quoted — carrier(s) declined</MudSelectItem>
    <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.PriceTooHigh)">Price Too High — client declined on premium</MudSelectItem>
    <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.LostToCompetitor)">Lost to Competitor</MudSelectItem>
    <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.ClientDeclinedCoverage)">Client Declined Coverage</MudSelectItem>
    <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.PolicyLapsed)">Policy Lapsed — missed renewal window</MudSelectItem>
    <MudSelectItem T="CloseReason?" Value="@((CloseReason?)CloseReason.Other)">Other</MudSelectItem>
</MudSelect>
```

### Fix 2 — Defensive (OpportunityWorkspace.razor) — PREVENT FUTURE CIRCUIT KILLS

Wrap `CloseOpportunity()` in try/catch, matching the existing `ParkOpportunity()` pattern:

**BEFORE (lines 188–199):**
```csharp
private async Task CloseOpportunity()
{
    var dialog = await DialogService.ShowAsync<CloseOpportunityDialog>(
        "Close Opportunity",
        new DialogParameters { ["OpportunityId"] = Id });
    var result = await dialog.Result;
    if (result != null && !result.Canceled)
    {
        Snackbar.Add("Opportunity closed.", Severity.Info);
        Nav.NavigateTo("/pipeline");
    }
}
```

**AFTER:**
```csharp
private async Task CloseOpportunity()
{
    try
    {
        var dialog = await DialogService.ShowAsync<CloseOpportunityDialog>(
            "Close Opportunity",
            new DialogParameters { ["OpportunityId"] = Id });
        var result = await dialog.Result;
        if (result != null && !result.Canceled)
        {
            Snackbar.Add("Opportunity closed.", Severity.Info);
            Nav.NavigateTo("/pipeline");
        }
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Could not open Close dialog: {ex.Message}", Severity.Error);
    }
}
```

### Fix 3 — Secondary (CloseOpportunityDialog.razor) — GetUserIdAsync inside try/catch

Move `GetUserIdAsync()` inside the try/catch and broaden the catch to `Exception`:

**BEFORE (lines 47–58):**
```csharp
private async Task Submit()
{
    if (_reason == null) return;
    var userId = await UserSession.GetUserIdAsync();
    try
    {
        await Lifecycle.CloseOpportunityAsync(
            OpportunityId, _reason.Value, _notes.Trim(), userId);
        MudDialog.Close(DialogResult.Ok(true));
    }
    catch (LifecycleValidationException ex)
    {
        Snackbar.Add(ex.Message, Severity.Error);
    }
}
```

**AFTER:**
```csharp
private async Task Submit()
{
    if (_reason == null) return;
    try
    {
        var userId = await UserSession.GetUserIdAsync();
        await Lifecycle.CloseOpportunityAsync(
            OpportunityId, _reason.Value, _notes.Trim(), userId);
        MudDialog.Close(DialogResult.Ok(true));
    }
    catch (LifecycleValidationException ex)
    {
        Snackbar.Add(ex.Message, Severity.Error);
    }
    catch (Exception ex)
    {
        Snackbar.Add($"Failed to close opportunity: {ex.Message}", Severity.Error);
    }
}
```

---

## Files Tony Needs to Change

| File | Change |
|------|--------|
| `src/FamOs.Web/Components/Dialogs/CloseOpportunityDialog.razor` | Fix 1 (MudSelect T= + MudSelectItem T= + cast) + Fix 3 (try/catch) |
| `src/FamOs.Web/Components/Pages/Opportunity/OpportunityWorkspace.razor` | Fix 2 (try/catch in CloseOpportunity) |

**No DB changes required. No migrations required. Pure Razor/C# fix.**

---

## Risk Assessment

**Low** — Two isolated Razor component changes with no domain logic modifications.  
- No DB schema changes  
- No service layer changes  
- No EF Core model changes  
- Fix is surgical: two files, ~15 lines changed  
- Pattern exists in codebase (ParkOpportunity try/catch is the exact model to follow)  
- Risk of regression: minimal — CloseOpportunityDialog is currently non-functional anyway

---

## What Was NOT the Issue

- DB columns: all three present (WI903 migration ran successfully)  
- `IMudDialogInstance` → `MudDialogInstance` WI903 build fix: correct, type exists in MudBlazor 7.16  
- HubSpot fire-and-forget (WI908 fix): correct, moved outside ExecuteAsync  
- `LifecycleCommandService` DI registration: correctly `AddScoped`  
- `SignalResolver.Resolve`: handles closed opportunities gracefully  
- `CloseOpportunityAsync` transaction logic: clean, no issues  

---

*Clint out. Tony has a clean target.*
