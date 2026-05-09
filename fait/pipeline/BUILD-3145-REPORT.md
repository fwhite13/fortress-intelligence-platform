# Build Report — ADO#3145

## What was built
Task mode toggle button in the chat input area + `mode_switch` SSE event handler that activates a task indicator in the chat header. The `TurnRequest` now passes the user's toggle state instead of hardcoded `false`.

## Commit
`1261e3f7f0a400b3f3915bfa295f72011766de38`
`feat(fait#3145): task mode toggle + mode_switch SSE event`

## Files changed
- `src/FortressAI.Web/Services/IUserAgentRuntime.cs` — Added `Payload` (string?) as final field in `HarnessEvent` record; updated comment to include `mode_switch` in the type list
- `src/FortressAI.Web/Components/Chat/ChatView.razor`:
  - Added `_taskMode` / `_taskModeActive` bool fields (before cold-start guards)
  - Added `chat-task-indicator` div in `chat-header` (conditional on `_taskModeActive`)
  - Added `btn-task-mode` toggle button before Send button in `chat-input-wrapper`
  - Reset `_taskModeActive = false` at top of `HandleSend`
  - Changed `TaskMode: false` → `TaskMode: _taskMode` in `TurnRequest` construction
  - Added `mode_switch` branch in SSE `await foreach` loop (`_taskModeActive = true; StateHasChanged()`)
  - Added `_taskMode = false` reset in `finally` block
  - Added `<style>` block at end of file with CSS vars only for `.btn-task-mode`, `.btn-task-mode--active`, `.btn-task-mode:hover`, `.chat-task-indicator`

## Parallelization used
No — single CC session, all changes in two files with cross-file dependency (HarnessEvent Payload, ChatView consuming it).

## CC sessions run
1 — CC Sonnet, single pass. All 9 changes applied, build passed 0 errors.

## Acceptance criteria verification
- [x] `HarnessEvent.Payload` field added — ✅ IUserAgentRuntime.cs line 68
- [x] `_taskMode` / `_taskModeActive` fields added — ✅ ChatView.razor lines 309–310
- [x] Toggle button before Send button — ✅ ChatView.razor line 234 (inside chat-input-wrapper, before MudIconButton Send)
- [x] `mode_switch` SSE handler — ✅ ChatView.razor line 807
- [x] `chat-task-indicator` in chat-header — ✅ ChatView.razor line 50–54
- [x] `TaskMode: _taskMode` in TurnRequest — ✅ ChatView.razor line 781
- [x] `_taskModeActive = false` reset at start of HandleSend — ✅ ChatView.razor line 480
- [x] `_taskMode = false` reset in finally — ✅ ChatView.razor line 841
- [x] All CSS via CSS vars only — ✅ No hardcoded values, all `var(--...)`

## Known edge cases / things Clint should scrutinize
- The `btn-task-mode` button is an HTML `<button>` (not MudIconButton) — consistent with the KB toggle buttons pattern already in the file. The `disabled` binding uses `@isStreaming` (Blazor bool attribute binding).
- `_taskModeActive` resets to `false` at the START of each `HandleSend` call — meaning the indicator only lights up if the current turn's harness fires a `mode_switch` event. If no `mode_switch` arrives, the indicator stays off even if user had toggled task mode. This is the intended spec behavior.
- `_taskMode` resets to `false` in `finally` after every send — user must re-toggle for each message. Spec says reset after send; if persistence per-conversation is wanted, that's a future WI.
- `ForceTaskMode` field left untouched per spec.
- No changes to `ChatInput.razor` or `FargateUserAgentRuntime.cs` per spec.

## How to test locally
1. `cd ~/projects/fip/fait && dotnet run --project src/FortressAI.Web`
2. Open chat, verify task mode toggle button (⊕ tasks icon) appears before the send button
3. Click toggle — button should show active state (accent color)
4. Send a message with toggle active — `TurnRequest.TaskMode` will be `true` in the request
5. Simulate `mode_switch` SSE event from harness — chat-header should show "Task Mode" indicator
6. After message completes, toggle resets to off (indicator clears on next send)
