# CC Brief: Sprint 3 R1 Fixes

You are fixing 5 issues (2 important, 3 nitpicks) identified in review cycle 1 of a sprint.

---

## I1 — ADO#4810: Legacy "chip" SSE event path broken

**File:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor`

Find the `else if (evt.Type == "chip")` handler (around L1158). Currently it creates a `ToolCallEvent` with `Id = default` (Guid.Empty) and no auto-dismiss timer — chips created this way stick forever and can break bulk-remove.

**Current code:**
```csharp
else if (evt.Type == "chip")
{
    // ADO#4717 — ephemeral chip from Bedrock tool calls
    var chipText = evt.Content ?? "Working...";
    _activeToolCalls.Add(new ToolCallEvent("bedrock", "chip", "calling", chipText, DateTime.UtcNow));
    await InvokeAsync(StateHasChanged);
}
```

**Replace with** (assign a new Guid, use `GetChipIconKeyFromToolName("chip")` for the icon, add auto-dismiss timer exactly matching the `task_progress` pattern):
```csharp
else if (evt.Type == "chip")
{
    // ADO#4717 — ephemeral chip from Bedrock tool calls
    // ADO#4810 — assign unique Id so auto-dismiss only removes this chip
    var chipText = evt.Content ?? "Working...";
    var chipIcon = GetChipIconKeyFromToolName("chip");
    var legacyChip = new ToolCallEvent("bedrock", "chip", "calling", chipText, DateTime.UtcNow, chipIcon, Guid.NewGuid());
    _activeToolCalls.Add(legacyChip);
    await InvokeAsync(StateHasChanged);

    // ADO#4810 — auto-dismiss legacy chip after 2s (fade then remove), same pattern as task_progress
    var chipId = legacyChip.Id;
    _ = Task.Delay(2000).ContinueWith(async t =>
    {
        if (t.IsFaulted || t.IsCanceled) return;
        try
        {
            await InvokeAsync(() =>
            {
                var idx = _activeToolCalls.FindIndex(c => c.Id == chipId);
                if (idx >= 0)
                {
                    _activeToolCalls[idx] = _activeToolCalls[idx] with { Status = "done" };
                    StateHasChanged();
                }
            });
            await Task.Delay(300);
            await InvokeAsync(() =>
            {
                _activeToolCalls.RemoveAll(c => c.Id == chipId);
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException) { }
        catch (TaskCanceledException) { }
    }, TaskScheduler.Default);
}
```

---

## I2 — ADO#4834: No file count limit in dirty-file upload loop

**File:** `fait/agent-harness/harness-server.js`

Find the dirty-file sync section (around L3963–3970), which currently starts like:
```javascript
const dirtyFiles = findDirtyFiles(preSyncSnapshot, postSyncSnapshot);
const uploadedFiles = [];
for (const relPath of dirtyFiles) {
```

**Fix:** Change `const dirtyFiles` to `let dirtyFiles` and add a count cap after that line:

```javascript
let dirtyFiles = findDirtyFiles(preSyncSnapshot, postSyncSnapshot);
const MAX_DIRTY_FILES = 50;
if (dirtyFiles.length > MAX_DIRTY_FILES) {
    console.warn(`[harness] dirty-file sync: ${dirtyFiles.length} files detected, capping at ${MAX_DIRTY_FILES}`);
    dirtyFiles = dirtyFiles.slice(0, MAX_DIRTY_FILES);
}
const uploadedFiles = [];
for (const relPath of dirtyFiles) {
```

---

## N1 — brief-ado*.md files not gitignored

**File:** `fait/.gitignore` — create this file (it does not currently exist).

Create `fait/.gitignore` with the following content:
```
# CC brief files — never commit temp pipeline briefs
brief-*.md
brief-ado*.md
/tmp/cc-brief*.md
```

---

## N2 — CONVERTER_BASE_URL localhost fallback should log warning

**File:** `fait/src/FortressAI.Web/Services/ArtifactPreviewService.cs` (around L96) and `fait/src/FortressAI.Web/Controllers/ArtifactPreviewController.cs` (around L140)

In both files, find the line:
```csharp
var converterBase = _config["CONVERTER_BASE_URL"] ?? "http://localhost:3001";
```

Replace with a pattern that logs a warning when falling back. In ArtifactPreviewService.cs, use the existing `_logger` field (or whatever the logger field is named in that class). In ArtifactPreviewController.cs, use its logger.

In ArtifactPreviewService.cs:
```csharp
var converterBaseRaw = _config["CONVERTER_BASE_URL"];
if (string.IsNullOrEmpty(converterBaseRaw))
    _logger.LogWarning("[ArtifactPreview] CONVERTER_BASE_URL not set — falling back to localhost. PPTX conversion may fail in production.");
var converterBase = converterBaseRaw ?? "http://localhost:3001";
```

In ArtifactPreviewController.cs:
```csharp
var converterBaseRaw = _config["CONVERTER_BASE_URL"];
if (string.IsNullOrEmpty(converterBaseRaw))
    _logger.LogWarning("[ArtifactPreview] CONVERTER_BASE_URL not set — falling back to localhost. PPTX conversion may fail in production.");
var converterBase = converterBaseRaw ?? "http://localhost:3001";
```

**Important:** Check the actual logger field names used in those files before making the substitution — use whatever `ILogger` field is already declared. Do NOT add new logger fields; use what exists.

---

## N4 — web_fetch domain extraction returns filename for non-URL inputs

**File:** `fait/agent-harness/harness-server.js` — `resolveProgressLabel()` function (around L270)

Find the current `web_fetch` branch inside `resolveProgressLabel`:
```javascript
if (toolName === 'web_fetch') {
    const url = input.url || input.uri || '';
    let domain = '';
    if (url) {
        try { domain = new URL(url).hostname; } catch { domain = url.split('/')[0]; }
    }
    return domain ? `Fetching: ${chipTrunc(domain, 40)}` : 'Fetching web content...';
}
```

**Replace with** an `extractDomain` helper and updated branch:
```javascript
if (toolName === 'web_fetch') {
    const url = input.url || input.uri || '';
    const domain = extractDomain(url);
    return domain ? `Fetching: ${chipTrunc(domain, 40)}` : 'Fetching web content...';
}
```

And add the `extractDomain` helper function **just before** `resolveProgressLabel`:
```javascript
function extractDomain(url) {
    if (!url) return '';
    try {
        return new URL(url).hostname || url;
    } catch {
        return url.length > 30 ? url.slice(0, 30) + '\u2026' : url;
    }
}
```

---

## After all changes

After making all changes, run:
```bash
cd /home/fredw/projects/fip && git add -A && git diff --cached --stat
```

Show the diff summary. Then commit:
```bash
cd /home/fredw/projects/fip && git commit -m "Fix Sprint 3 R1: legacy chip Guid, dirty-file cap, gitignore, converter warning, extractDomain

ADO#4810: legacy chip SSE path now assigns Guid.NewGuid() + auto-dismiss timer
ADO#4834: dirty-file sync capped at MAX_DIRTY_FILES=50 with warn log
N1: fait/.gitignore added for brief-ado*.md files
N2: CONVERTER_BASE_URL fallback now logs LogWarning in service + controller
N4: extractDomain() helper added, web_fetch chip label handles invalid URLs"
```

Output the commit hash after committing.
