# Build Report — WI #1656: _hasChanges Change Detection

## CC Invocation
cat /tmp/1656-brief.md | claude --model sonnet --print --dangerously-skip-permissions

## "New Files Added" Detection
Used `_pendingFiles.Count > 0`.

In resume mode, `_uploadedFiles` is populated at wizard init from existing submission files
(OnInitializedAsync). Newly selected files are collected in `_pendingFiles` (List<IBrowserFile>)
by the HandleFilesSelected callback and held there until GoToStep2Discovery uploads them.
Therefore, `_pendingFiles.Count > 0` is the complete and correct signal for "files added this session."
No overlap with `_originalFileIds` is possible because newly added files haven't been uploaded yet.

## _hasChanges Property
```csharp
private bool _hasChanges => _isResume && (
    _narrativeText.Trim() != _originalNarrative.Trim() ||
    _filesToDelete.Count > 0 ||
    _pendingFiles.Count > 0
);
```
Computed property (not a field) — always current, no stale-value risk.
Short-circuits on `_isResume == false` so new-submission path always returns false.

## Files Modified
- `src/FortressNexus.Web/Components/Pages/NewSpecWizard.razor`

## Build Result
0 errors, 0 warnings

## Commit Hash
fda199c
