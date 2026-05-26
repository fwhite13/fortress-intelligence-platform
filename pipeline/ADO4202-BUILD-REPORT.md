# Build Report: ADO#4202 — FolderPicker new folder persistence fix

## Commit
`72da916d fix(fait#4202): parse harness folderId for new folder persistence`

## Change
**File:** `fait/src/FortressAI.Web/Components/Chat/FolderPicker.razor`

In `HandleConfirmAsync()` (now `OnStartTask()`), after the successful `/turn/folder-confirm` response, parse the JSON body to extract the `folderId` returned by the harness. Use this as `resolvedFolderId` for new folder creation instead of passing `null`.

## Root Cause
`FolderConfirmedArgs.FolderId` was set to `null` for new folders, causing `ChatView.HandleFolderConfirmed` to skip `PersistWorkingFolderAsync` (guarded by `if (args.FolderId != null)`).

## Layers Fixed
- `FolderPicker.razor` — new folder folderId now parsed from harness response and passed through

## Harness Touched
No

## Build Result
`dotnet build` — 0 errors, 49 warnings (pre-existing)

## Acceptance
- New folder creation: `FolderConfirmedArgs.FolderId` = UUID from harness response
- Existing folder selection: unchanged
- Parse failure: graceful fallback (non-fatal)
