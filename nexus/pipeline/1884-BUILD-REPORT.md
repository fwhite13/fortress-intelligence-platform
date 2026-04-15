# Build Report — ADO #1884

**Commit:** `50ed7b0`
**Branch:** main
**Build:** dotnet build → 0 errors (1 pre-existing warning, unrelated)

## Files changed
| File | Change |
|------|--------|
| `nexus/src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor` | HandleDiscoveryCompleted re-fetches session with answers before advancing to step 3 |

## Root cause
`HandleDiscoveryCompleted()` was a synchronous method that simply set `_activeStep = 3`. The `_discoverySession` field was last populated by the background poll loop in `GoToStep2Discovery`, which exits as soon as `session.Status == QuestionsReady` — at which point no answers exist yet. When the user fills in answers and clicks Continue, `DiscoveryStep.HandleContinue()` saves answers to the DB and fires `OnCompleted` → `HandleDiscoveryCompleted()`. But `_discoverySession` was still the stale QuestionsReady snapshot with `q.Answer == null` on every question. Step 3's `DiscoveryAnswersSummary` rendered `q.Answer?.AnswerText ?? "[Not answered]"` for every question.

## Fix applied
Changed `HandleDiscoveryCompleted` from `private Task` to `private async Task`. Before setting `_activeStep = 3`, the method now calls `DiscoveryService.GetSessionAsync(_submissionId.Value)` (which does `.Include(s => s.Questions).ThenInclude(q => q.Answer)`) to re-fetch the session with all answers populated. The call is wrapped in a non-fatal try/catch so that if it throws, `_activeStep = 3` still fires.

```csharp
private async Task HandleDiscoveryCompleted()
{
    // Re-fetch session so answers are included before rendering the Review step
    if (_submissionId.HasValue)
    {
        try
        {
            _discoverySession = await DiscoveryService.GetSessionAsync(_submissionId.Value);
        }
        catch
        {
            // Non-fatal — proceed with stale session; answers may show [Not answered]
        }
    }
    _activeStep = 3;
}
```

## CC invocation
`cat /tmp/tony-1884-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

## Self-review checklist
- [x] HandleDiscoveryCompleted is now async Task (not Task)
- [x] GetSessionAsync called before _activeStep = 3
- [x] try/catch wraps GetSessionAsync (non-fatal)
- [x] _activeStep = 3 fires regardless of fetch success/failure
- [x] No other methods touched
- [x] dotnet build 0 errors
