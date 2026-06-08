# CC Brief: ADO#4937 — Fix dev banner click via MudButton

## Context
File: `/home/fredw/projects/fip/shared/FipShared/Components/FipNavBar.razor`

The dev banner currently uses a bare `<div @onclick>` which does not reliably dispatch through the Blazor SignalR circuit. The fix is to replace it with a `<MudButton>`.

Additionally, `OpenDebugDialog()` currently uses `Console.WriteLine` for logging. It must use `ILogger<FipNavBar>` instead (injected via `@inject`).

## Current state (around line 73)
```razor
@if (IsDev)
{
    <div class="fip-dev-banner" @onclick="async () => await OpenDebugDialog()" @onclick:stopPropagation="true" style="cursor: pointer;" title="Click for debug info">⚠ Development Environment</div>
}
```

Current `@code` block has:
- `Console.WriteLine("[FipNavBar] OpenDebugDialog() called")` — needs to become ILogger
- `Console.WriteLine($"[FipNavBar] OnInitialized: IsDev={IsDev}")` — needs to become ILogger
- `Console.WriteLine($"[FipNavBar] OnParametersSet: IsDev={IsDev}")` — needs to become ILogger
- `Console.Error.WriteLine(...)` in catch block — needs to become ILogger

## Changes Required

### 1. Add ILogger inject directive at the top (after existing @inject lines)
Add this line after the last `@inject` line:
```razor
@inject ILogger<FipNavBar> Logger
```

### 2. Replace the dev banner div with MudButton
Replace:
```razor
@if (IsDev)
{
    <div class="fip-dev-banner" @onclick="async () => await OpenDebugDialog()" @onclick:stopPropagation="true" style="cursor: pointer;" title="Click for debug info">⚠ Development Environment</div>
}
```

With:
```razor
@if (IsDev)
{
    <MudButton Variant="Variant.Text" Class="fip-dev-banner" OnClick="OpenDebugDialog" Style="cursor:pointer;width:100%;text-transform:none;" title="Click for debug info">⚠ Development Environment</MudButton>
}
```

### 3. Replace Console.WriteLine calls in @code block with ILogger calls

Replace `OpenDebugDialog()` method:
```csharp
private async Task OpenDebugDialog()
{
    Console.WriteLine("[FipNavBar] OpenDebugDialog() called");
    try
    {
        await DialogService.ShowAsync<DevInfoDialog>("Dev Environment Info", new DialogOptions
        {
            MaxWidth = MaxWidth.Small,
            FullWidth = true,
            CloseOnEscapeKey = true,
            CloseButton = true
        });
        Console.WriteLine("[FipNavBar] OpenDebugDialog() ShowAsync returned");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[FipNavBar] OpenDebugDialog() EXCEPTION: {ex}");
    }
}
```

With:
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

Replace `OnInitialized()`:
```csharp
protected override void OnInitialized()
{
    Console.WriteLine($"[FipNavBar] OnInitialized: IsDev={IsDev}");
}
```
With:
```csharp
protected override void OnInitialized()
{
    Logger.LogDebug("[FipNavBar] OnInitialized: IsDev={IsDev}", IsDev);
}
```

Replace `OnParametersSet()`:
```csharp
protected override void OnParametersSet()
{
    Console.WriteLine($"[FipNavBar] OnParametersSet: IsDev={IsDev}");
}
```
With:
```csharp
protected override void OnParametersSet()
{
    Logger.LogDebug("[FipNavBar] OnParametersSet: IsDev={IsDev}", IsDev);
}
```

## File to edit
`/home/fredw/projects/fip/shared/FipShared/Components/FipNavBar.razor`

## Constraints
- Do NOT modify any other files
- Do NOT change the CSS class name `fip-dev-banner` — it must be preserved on the MudButton's Class attribute
- Do NOT change the dialog logic or dialog options
- Do NOT change the `HandleSignOut`, `IsVisible`, or any other methods
- Do NOT change any parameters or component structure outside the specified changes

## Verification
After making changes, verify:
1. `@inject ILogger<FipNavBar> Logger` is present in directives
2. The `@if (IsDev)` block uses `<MudButton>` not `<div @onclick>`
3. No `Console.WriteLine` or `Console.Error.WriteLine` remain in the file
4. The MudButton has `Class="fip-dev-banner"` to preserve CSS styling
