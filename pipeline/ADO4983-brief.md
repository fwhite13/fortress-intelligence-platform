# CC Brief — ADO#4983: Move dev debug trigger from banner to nav footer

## Context
The dev banner click handler has been through multiple failed fix cycles. We're moving the debug trigger out of FipNavBar and into MainLayout's nav drawer footer. Both files have been pre-read and analyzed.

---

## File 1: `shared/FipShared/Components/FipNavBar.razor`

### Analysis (already done — act on this directly)
- `@inject IDialogService DialogService` (line 9) → **REMOVE** — only used in `OpenDebugDialog()` which is being removed
- `@inject ILogger<FipNavBar> Logger` (line 10) → **KEEP** — used in `OnInitialized()` and `OnParametersSet()` lifecycle methods
- `@inject NavigationManager NavManager` (line 11) → **KEEP** — used in waffle menu `MudMenuItem OnClick`
- `@using MudBlazor` → **KEEP** — used throughout component (MudIconButton, MudMenu, MudMenuItem, MudText, etc.)

### Changes to make:

**1. Remove the IDialogService injection line:**
Remove this exact line:
```
@inject IDialogService DialogService
```

**2. Replace the banner block** — change from:
```razor
@if (IsDev)
{
    <div class="fip-dev-banner">
        <button type="button" style="all:unset;cursor:pointer;width:100%;display:block;text-align:center;" title="Click for debug info" @onclick="OpenDebugDialog" @onclick:stopPropagation="true">⚠ Development Environment</button>
    </div>
}
```
To:
```razor
@if (IsDev)
{
    <div class="fip-dev-banner">⚠ Development Environment</div>
}
```

**3. Remove the entire `OpenDebugDialog()` method** from the `@code { }` block:
```csharp
    private async Task OpenDebugDialog()
    {
        Logger.LogInformation("[FipNavBar] OpenDebugDialog() called");
        try
        {
            await DialogService.ShowAsync<DevInfoDialog>("Dev Environment Info", new DialogOptions
            {
                MaxWidth = MaxWidth.Small,
                FullWidth = true,
                CloseOnEscapeKey = true,
                CloseButton = true
            });
            Logger.LogInformation("[FipNavBar] OpenDebugDialog() ShowAsync returned");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[FipNavBar] OpenDebugDialog() EXCEPTION");
        }
    }
```
Remove this method entirely — no replacement.

---

## File 2: `fait/src/FortressAI.Web/Components/Layout/MainLayout.razor`

### Analysis (already done — act on this directly)
- `@using FipShared.Components` is already on line 4 → `DevInfoDialog` (which lives in `FipShared.Components/Dialogs/`) is already accessible, no new using needed
- `@inject IDialogService DialogService` is already on line 12 → do NOT add it again
- `@inject Microsoft.AspNetCore.Hosting.IWebHostEnvironment HostEnv` is already on line 13 → do NOT add it again

### Changes to make:

**1. Replace the footer block** — change from:
```razor
                <div class="fip-drawer-footer">
                    <MudText Typo="Typo.caption">Fortress Intelligence Platform</MudText>
                </div>
```
To:
```razor
                <div class="fip-drawer-footer">
                    @if (HostEnv.IsDevelopment())
                    {
                        <button type="button" style="all:unset;cursor:pointer;" @onclick="OpenDevInfoDialog">
                            <MudText Typo="Typo.caption">Fortress Intelligence Platform</MudText>
                        </button>
                    }
                    else
                    {
                        <MudText Typo="Typo.caption">Fortress Intelligence Platform</MudText>
                    }
                </div>
```

**2. Add `OpenDevInfoDialog()` method** to the `@code { }` block.
Place it after the existing `private void Logout()` method and before the `public void Dispose()` method:
```csharp
    private async Task OpenDevInfoDialog()
    {
        await DialogService.ShowAsync<DevInfoDialog>("Dev Environment Info", new DialogOptions
        {
            CloseOnEscapeKey = true,
            CloseButton = true,
            MaxWidth = MaxWidth.Small
        });
    }
```

---

## Constraints
- Do NOT modify any other files
- Do NOT change any other logic in either file
- Do NOT add or remove any other using/inject statements
- Preserve all indentation/formatting consistent with surrounding code
- Make exactly the changes described — nothing more, nothing less

## Acceptance Criteria
1. `FipNavBar.razor`: `@inject IDialogService DialogService` is gone
2. `FipNavBar.razor`: Banner is `<div class="fip-dev-banner">⚠ Development Environment</div>` — no button, no onclick, no title
3. `FipNavBar.razor`: `OpenDebugDialog()` method is gone
4. `MainLayout.razor`: Footer has the dev-conditional button wrapping MudText
5. `MainLayout.razor`: `OpenDevInfoDialog()` method exists in `@code { }`
