# CC Review Brief — ADO #1884

You are performing an adversarial code review. Read the specified files, then answer each check below with evidence from the code.

## Commit
50ed7b0 — single method change in NewSpecWizard.razor

## Files to read

1. `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` — the changed file
2. `nexus/src/FortressNexus.Web/Services/Discovery/DiscoveryService.cs` — verify GetSessionAsync includes ThenInclude
3. Look at the step 3 markup section in NewSpecWizard.razor for null guard on _discoverySession

## Changed method (what Tony built)

```csharp
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

## Checks — answer each one with evidence from the actual code

### Check 1: async Task signature safe for Blazor EventCallback?
- Find where HandleDiscoveryCompleted is wired up in the razor markup (look for OnCompleted=, OnContinue=, or similar EventCallback wiring)
- Confirm it's assigned to an EventCallback<> parameter (not invoked directly)
- EventCallback supports async Task — confirm the signature matches

### Check 2: _activeStep = 3 always fires?
- Verify _activeStep = 3 is OUTSIDE the try/catch block and OUTSIDE the if (_submissionId.HasValue) block
- If GetSessionAsync throws, does execution reach _activeStep = 3? YES it must.
- If _submissionId is null, does _activeStep = 3 still fire? YES it must.

### Check 3: GetSessionAsync loads answers via ThenInclude?
- Open DiscoveryService.cs
- Find GetSessionAsync method
- Verify it does .Include(s => s.Questions).ThenInclude(q => q.Answer) (or equivalent)
- If it does NOT have ThenInclude, that is a CRITICAL bug — answers will be null in step 3

### Check 4: No double-fetch conflict with resume path?
- Find LoadSubmissionAsync in NewSpecWizard.razor
- It also calls GetSessionAsync — confirm this is called at a different lifecycle point (e.g., OnInitializedAsync or similar)
- Confirm there's no race condition or conflict between LoadSubmissionAsync and HandleDiscoveryCompleted

### Check 5: _discoverySession null guard in step 3 markup?
- Find the step 3 section in the razor markup (look for _activeStep == 3 or step index 3)
- Verify there's a @if (_discoverySession != null) guard before DiscoveryAnswersSummary component
- If DiscoveryAnswersSummary is rendered without a null guard and _discoverySession is null, it will throw a NullReferenceException

### Check 6: No unnecessary StateHasChanged added?
- Search the new/changed code for StateHasChanged() calls
- Blazor automatically re-renders after an async EventCallback completes — explicit StateHasChanged is unnecessary and indicates misunderstanding
- Flag as warning if present (not blocking)

## Report format

For each check, provide:
- PASS/FAIL/WARN
- Evidence: exact line numbers or code snippets from the files
- If FAIL: why it's a problem and what the fix should be

Be adversarial. Don't assume things are correct because Tony says so. Read the actual code.
