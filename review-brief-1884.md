# CC Review Brief — ADO #1884

You are performing an adversarial code review of a single-method change in a Blazor Server app.

## What changed

File: `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor`

The method `HandleDiscoveryCompleted` was changed from a sync stub to an async fetch:

```csharp
// BEFORE
private Task HandleDiscoveryCompleted()
{
    _activeStep = 3;
    return Task.CompletedTask;
}

// AFTER
private async Task HandleDiscoveryCompleted()
{
    if (_submissionId.HasValue)
    {
        try
        {
            _discoverySession = await DiscoveryService.GetSessionAsync(_submissionId.Value);
        }
        catch
        {
            // Non-fatal
        }
    }
    _activeStep = 3;
}
```

## Files to read

1. `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` — full file
2. `nexus/src/FortressNexus.Web/Services/DiscoveryService.cs` (or wherever `GetSessionAsync` is defined — search if needed)
3. Any interface file for `IDiscoveryService` (search for `GetSessionAsync`)

## Checks to perform — answer each explicitly

### Check 1: async Task signature safe for Blazor EventCallback
- Search `NewSpecWizard.razor` for all usages/bindings of `HandleDiscoveryCompleted`
- Is it wired as an `EventCallback` or `EventCallback<T>`? (These support async Task fine)
- Is it ever called as a sync delegate or passed to something that doesn't support async? If yes, flag it.
- **Pass criterion:** `HandleDiscoveryCompleted` is only wired to EventCallback / called with await — no sync delegate usage.

### Check 2: `_activeStep = 3` always fires
- Verify that `_activeStep = 3` is OUTSIDE the try/catch block and OUTSIDE the `if (_submissionId.HasValue)` block
- It must execute even if: (a) `_submissionId` is null, (b) `GetSessionAsync` throws
- **Pass criterion:** `_activeStep = 3` is unconditional, after all conditional logic.

### Check 3: GetSessionAsync loads answers via ThenInclude
- Find the implementation of `GetSessionAsync` in DiscoveryService
- Verify it does `.Include(s => s.Questions).ThenInclude(q => q.Answer)` (or equivalent eager loading)
- If it only does `.Include(s => s.Questions)` WITHOUT ThenInclude for Answer, flag as Critical (answers will still be null)
- **Pass criterion:** ThenInclude on Answer (or equivalent) confirmed.

### Check 4: No double-fetch conflict with resume path
- Search `NewSpecWizard.razor` for `LoadSubmissionAsync` (or similar resume path)
- Does it also call `GetSessionAsync`? If so, is it in a different lifecycle (OnInitializedAsync, a button handler) — i.e., NOT called concurrently with `HandleDiscoveryCompleted`?
- **Pass criterion:** No concurrent fetch possible; different lifecycle stages.

### Check 5: Null guard on `_discoverySession` in step 3 markup
- In `NewSpecWizard.razor`, find the markup for step 3 (where `_activeStep == 3`)
- Is there a null check like `@if (_discoverySession != null)` before rendering `DiscoveryAnswersSummary` (or equivalent component)?
- **Pass criterion:** Null guard present on any component/loop that dereferences `_discoverySession`.

### Check 6: No unnecessary StateHasChanged added
- Verify `HandleDiscoveryCompleted` does NOT call `StateHasChanged()` or `InvokeAsync(StateHasChanged)`
- Blazor auto-renders after awaited EventCallback completion — explicit StateHasChanged is unnecessary and can cause double-renders
- **Pass criterion:** No StateHasChanged call in HandleDiscoveryCompleted. (⚠️ if present but not wrong)

## What to report

For each check: state ✅ PASS or ❌ FAIL with evidence (file path, line number, relevant code snippet).

If you find any other issues not in the checklist (logic errors, null dereferences, missing error handling, pattern violations), report them as bonus findings.

Be adversarial. Don't assume the code is correct just because it compiles.
