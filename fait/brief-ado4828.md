# CC Task: ADO#4828 — Warning Banner Clickable Modal With Debug Info

## Working directory
`/home/fredw/projects/fip/fait/`

## Files to modify
- `src/FortressAI.Web/Components/Chat/ChatView.razor` only

---

## Context

The warning banner is in ChatView.razor around lines 110-117:
```razor
@if (_agentReady && _hasFailedTasks && !_failedTaskBannerDismissed)
{
    <MudAlert Severity="Severity.Warning" Dense="true"
              ShowCloseIcon="true" CloseIconClicked="@(() => _failedTaskBannerDismissed = true)"
              Class="mx-2 mt-2">
        One or more scheduled tasks have failed.
        <MudLink Href="/tasks">View tasks →</MudLink>
    </MudAlert>
}
```

Available state for dialog:
- `_currentHarnessSessionId` (string?) — active harness session ID
- `ConversationId` (Guid?) — current conversation ID  
- `_assistantConfig?.AssistantName` (string?) — assistant name
- Assembly informational version — can be retrieved via `System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion`

---

## Changes Required

### 1. Add dialog state variable

In the `@code` section, near `_hasFailedTasks` and `_failedTaskBannerDismissed`, add:
```csharp
private bool _debugInfoDialogOpen = false;
```

### 2. Make the banner clickable

Replace the MudAlert block with one that includes a clickable "Debug info" link/button:

```razor
@if (_agentReady && _hasFailedTasks && !_failedTaskBannerDismissed)
{
    <MudAlert Severity="Severity.Warning" Dense="true"
              ShowCloseIcon="true" CloseIconClicked="@(() => _failedTaskBannerDismissed = true)"
              Class="mx-2 mt-2">
        One or more scheduled tasks have failed.
        <MudLink Href="/tasks">View tasks →</MudLink>
        <MudLink Class="ml-2" Style="cursor:pointer;" @onclick="@(() => _debugInfoDialogOpen = true)" @onclick:preventDefault>ⓘ Debug info</MudLink>
    </MudAlert>
}
```

### 3. Add MudDialog for debug info

Place this dialog BELOW the banner block (still inside the main layout, before `<div class="chat-messages-wrapper">`):

```razor
<MudDialog @bind-IsVisible="_debugInfoDialogOpen" Options="@(new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true, CloseOnEscapeKey = true })">
    <TitleContent>
        <MudText Typo="Typo.h6">Session Debug Info</MudText>
    </TitleContent>
    <DialogContent>
        <MudStack Spacing="2">
            <div>
                <MudText Typo="Typo.caption" Color="Color.Secondary">Build</MudText>
                <MudText Typo="Typo.body2" Style="font-family: monospace; word-break: break-all;">
                    @(System.Reflection.Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()
                        ?.InformationalVersion ?? "unknown")
                </MudText>
            </div>
            <div>
                <MudText Typo="Typo.caption" Color="Color.Secondary">Conversation ID</MudText>
                <MudText Typo="Typo.body2" Style="font-family: monospace;">
                    @(ConversationId?.ToString() ?? "none")
                </MudText>
            </div>
            <div>
                <MudText Typo="Typo.caption" Color="Color.Secondary">Harness Session ID</MudText>
                <MudText Typo="Typo.body2" Style="font-family: monospace;">
                    @(_currentHarnessSessionId ?? "none")
                </MudText>
            </div>
            <div>
                <MudText Typo="Typo.caption" Color="Color.Secondary">Assistant</MudText>
                <MudText Typo="Typo.body2">
                    @(_assistantConfig?.AssistantName ?? "default")
                </MudText>
            </div>
        </MudStack>
    </DialogContent>
    <DialogActions>
        <MudButton OnClick="@(() => _debugInfoDialogOpen = false)" Color="Color.Primary">Close</MudButton>
    </DialogActions>
</MudDialog>
```

---

## Acceptance Criteria
- AC1: Clicking "ⓘ Debug info" in the warning banner opens a MudDialog
- AC2: Dialog shows the assembly informational version (build tag / git commit hash)
- AC3: Dialog shows harness session ID (or "none" if no active session)
- AC4: Dialog shows current conversation ID
- AC5: Dialog shows assistant name

---

## Final steps
1. Verify the build compiles: `cd /home/fredw/projects/fip/fait/src/FortressAI.Web && dotnet build --no-restore 2>&1 | tail -10`
2. Fix any compilation errors
3. Commit: `cd /home/fredw/projects/fip && git add -A && git commit -m "ADO#4828: warning banner debug info dialog — harness session ID, conv ID, build tag, assistant name"`
