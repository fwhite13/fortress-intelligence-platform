# R2 Review Brief — ADO4620

You are performing an adversarial code review. Verify that 3 specific critical fixes from R1 are properly implemented in R2 commit e4025bd8.

## Files to read
1. `fait/pptx-converter/server.js` — full file
2. `fait/src/FortressAI.Web/Services/ArtifactPreviewService.cs` — full file
3. `fait/src/FortressAI.Web/Components/Chat/PptxPreviewPanel.razor` — full file

## What to verify — 3 specific fixes only

### FIX 1 (C1): LibreOffice user profile isolation — `fait/pptx-converter/server.js`

Check:
1. Does the `spawn('libreoffice', [...])` call include `--env:UserInstallation=file:///tmp/lo-profile-${artifactId}` in the args array?
2. In the `finally` block: is `fs.rmSync(`/tmp/lo-profile-${artifactId}`, { recursive: true, force: true })` or equivalent called to clean up the profile dir?
3. Does the cleanup NOT throw if the directory doesn't exist? (`force: true` option handles this, or equivalent try/catch)

Report EXACTLY what you see for each check — quote the actual code lines.

### FIX 2 (C2): CancellationTokenSource + DisposeAsync — `fait/src/FortressAI.Web/Components/Chat/PptxPreviewPanel.razor`

Check:
1. Is `private CancellationTokenSource? _pollCts` declared as a field?
2. In the 202 poll branch: is `_pollCts = new CancellationTokenSource()` called before the while loop, and is the token passed as `ct` variable?
3. Does `Task.Delay(2000, ct)` pass the cancellation token?
4. Is `OperationCanceledException` (or `TaskCanceledException`) caught to exit the loop cleanly?
5. Does `DisposeAsync()` call `_pollCts?.Cancel()` AND `_pollCts?.Dispose()`? Does it return anything other than `ValueTask.CompletedTask` directly (i.e., is it a real async method or does it at least do the cancel/dispose)?

Report EXACTLY what you see for each check — quote the actual code lines.

### FIX 3 (C3): Direct service poll, no HTTP self-call — `ArtifactPreviewService.cs` + `PptxPreviewPanel.razor`

Check in `ArtifactPreviewService.cs`:
1. Is `GetPreviewStatusAsync(Guid artifactId, Guid userId)` (or similar name) present?
2. Does it query the database directly (look for `DbContext`, `DbContextFactory`, or `_dbFactory` usage)?
3. Does it return `(bool, string?)` tuple or a similar structure with ready-flag + URL?
4. Does it NOT use `HttpClient` internally?

Check in `PptxPreviewPanel.razor` (202 poll branch):
1. Does the poll loop call `PreviewSvc.GetPreviewStatusAsync()` (or whatever the method is named)?
2. Is `HttpClientFactory.CreateClient()` absent from the 202 poll branch?
3. Does the component still have `IHttpClientFactory` injected (it should — used for convert POST and PDF fetch)?

Report EXACTLY what you see for each check — quote the actual code lines.

## Look for regressions
While verifying the fixes, also check:
- Did any fix introduce new null reference risks?
- Did the service method signature match what the panel is calling?
- Are there any obvious logic errors introduced in these specific changes?
- Is `DisposeAsync` properly declared (async/non-async) for ValueTask return?

## Output format
For each fix (C1, C2, C3):
- State VERIFIED or ISSUE
- Quote the relevant code lines
- If ISSUE: describe what's wrong specifically

At the end:
- Overall: PASS or NEEDS-CHANGES
- If NEEDS-CHANGES: list specific issues
