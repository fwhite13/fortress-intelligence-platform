# CC Task: ADO#4831 — Working Folder Modal Stays Open During Task Execution

## Context

In `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/FolderPicker.razor`, the FolderPicker MudDialog is opened by ChatView.razor when a `folder_required` SSE event fires. The dialog flow is:

1. `folder_required` SSE event arrives → ChatView.razor opens `FolderPicker` via `DialogService.ShowAsync<FolderPicker>`
2. User clicks "Start task" in FolderPicker → `OnStartTask()` runs → POSTs to `/turn/folder-confirm` → calls `MudDialog.Close(DialogResult.Ok(confirmedArgs))`
3. ChatView.razor gets the result back from `dialog.Result` and calls `HandleFolderConfirmed(confirmedArgs)`

## Problem

The modal is NOT correctly closing. Look at the `FolderPicker.razor` and `ChatView.razor` code carefully.

The issue is in `ChatView.razor`. After `folder_required` fires and the dialog opens, look at where `HandleFolderConfirmed` is called from the SSE event handler loop in `HandleSend`. The `folder_required` event handler calls `DialogService.ShowAsync` and `await dialog.Result`. BUT after the modal closes, `HandleFolderConfirmed` is called and `StateHasChanged()` is called there. However, there is also a potential issue where the dialog state is not cleared before the async CC spawn proceeds.

Specific investigation needed:
1. In ChatView.razor, look at the `folder_required` event handler in the SSE streaming loop (~line 1170-1220 area). It shows: `var dialog = await DialogService.ShowAsync<FolderPicker>(...)` and then `var result = await dialog.Result`. This is correct — it awaits the dialog result before calling `HandleFolderConfirmed`.

2. The REAL bug: The `FolderPicker.razor` `OnStartTask()` method sets `_submitting = true` and calls `StateHasChanged()` which keeps the dialog open and showing a spinner while it POSTs to harness. The POST to `/turn/folder-confirm` completes, then `MudDialog.Close(DialogResult.Ok(...))` is called. But there can be a race where the MudBlazor dialog doesn't close visually before the parent `ChatView` continues processing SSE events and updates state, causing the dialog to flash back open or appear to stay open.

## Fix Required

In `FolderPicker.razor`, the `OnStartTask()` method should:

1. After `MudDialog.Close(...)` is called successfully (after the folder confirm POST succeeds), ensure the dialog close is immediate. There's nothing to change here — MudDialog.Close is synchronous.

2. The REAL fix is in `ChatView.razor`. The `folder_required` handler should explicitly call `StateHasChanged()` BEFORE awaiting the dialog result, to force Blazor to update the UI and clear any lingering state. Also ensure there is NO path where the `folder_required` handler re-fires during the same SSE stream.

**Specific fix:**
- In `ChatView.razor`, in the `folder_required` SSE handler (inside the `await foreach` loop in `HandleSend`), add `await InvokeAsync(StateHasChanged)` AFTER the dialog is opened but BEFORE `await dialog.Result`. This ensures UI is updated to show the dialog cleanly.
- Also: after `HandleFolderConfirmed(confirmedArgs)` is called, call `await InvokeAsync(StateHasChanged)` to explicitly re-render with closed modal state.
- Check: is there any code path after `HandleFolderConfirmed` that might call `StateHasChanged` in a way that could re-show the folder picker? Look for any code in the `folder_required` SSE handler path that could cause a re-render issue.

**Also fix in FolderPicker.razor:**
- Add a comment explaining the `_confirming` guard prevents double-fire (it's already there, but verify it's working)
- After `MudDialog.Close(...)` is called, explicitly reset `_submitting = false` — although MudDialog.Close should dismiss the component entirely, this is a safety measure in case Blazor re-renders the component before fully disposing it.

## Files to modify

1. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor`
   - In the `folder_required` SSE handler (search for `evt.Type == "folder_required"`)
   - Add `await InvokeAsync(StateHasChanged)` AFTER `var dialog = await DialogService.ShowAsync<FolderPicker>(...)` and BEFORE `var result = await dialog.Result`
   - After `HandleFolderConfirmed(confirmedArgs)`, call `await InvokeAsync(StateHasChanged)`
   - After the `else` branch (dialog cancelled), the existing `await InvokeAsync(StateHasChanged)` should already be there — if not, add it

2. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/FolderPicker.razor`  
   - In `OnStartTask()`: after calling `MudDialog.Close(...)`, add `_submitting = false; _confirming = false;` as a safety reset before Close in case Close doesn't fully dispose

## Acceptance Criteria Verification

- AC1: Clicking Confirm in the folder picker dismisses the modal immediately ✓ (StateHasChanged after dialog close forces re-render)
- AC2: Modal does not reappear while CC task is executing ✓ (no re-entry to folder_required handler during same turn)  
- AC3: Folder picker state is reset correctly ✓ (existing conversation working folder persisted)

## IMPORTANT: Do not change any other functionality
- Do not modify the actual folder selection logic
- Do not modify the harness POST to `/turn/folder-confirm`
- Do not modify `HandleFolderConfirmed` logic
- Only fix the modal dismissal timing / state update issue
