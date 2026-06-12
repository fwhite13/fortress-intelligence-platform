ADO#5134 hot fix review. Read these files then answer each question YES/NO + one-line evidence:

Files: fait/src/FortressAI.Web/Components/Chat/ChatView.razor, fait/agent-harness/harness-server.js, fait/src/FortressAI.Web/Components/Chat/FolderPicker.razor, fait/src/FortressAI.Web/Services/IUserAgentRuntime.cs, fait/src/FortressAI.Web/Services/FargateUserAgentRuntime.cs

Q1a: Is `_awaitingFolderConfirm = true` set before `break` in folder_required handler?
Q1b: Does `finally` block skip full teardown when `_awaitingFolderConfirm` is true?
Q1c: Is `isStreaming = false` set in ONLY two places: ReadPostConfirmEventsAsync finally + cancelled ContinueWith branch?
Q1d: Is `_awaitingFolderConfirm = false` cleared in BOTH confirmed AND cancelled ContinueWith branches?
Q1e: CRITICAL — cancelled branch no longer sets `_folderPickerCancelled = true` and no longer calls `streamingCts?.Cancel()`. The outer catch is `catch (OperationCanceledException) when (_folderPickerCancelled)`. Since `break` exits the loop cleanly (no OCE), is this catch now dead code? Any risk of unhandled OperationCanceledException leaking?

Q2a: Does `GET /turn/events/:userId` have auth middleware? What protects it?
Q2b: Does `userId` come from req.params (URL param) not from a verified session?
Q2c: Is `pendingTurnEventsMap.set(userId, ...)` called immediately after `sendEvent({type:'folder_required'})`, ensuring no race window for missed events?

Q3: Is `await Task.Delay(1)` before `StateHasChanged()` in FolderPicker firstRender only?

Q4: Do `IUserAgentRuntime.ReadTurnEventsAsync` and `FargateUserAgentRuntime.ReadTurnEventsAsync` signatures match exactly?

Flag any Critical/Important issues with file+line+fix. Verdict: PASS / NEEDS-CHANGES / FAIL.
