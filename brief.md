# ADO#4911 + ADO#4912 — Ephemeral Chips + Task Mode Fixes

## Context
Working on: `/home/fredw/projects/fip/fait/`

Files to modify:
- `agent-harness/harness-server.js`
- `src/FortressAI.Web/Components/Chat/ChatView.razor`

---

## ADO#4911 — Ephemeral Chips: Mid-Task Gap + Generic Descriptions + Assistant Chips Cut Off

### Problem 1: Mid-Task Chip Gap (harness-server.js)

**Root cause:** The `consecutiveLabelCount <= 3` deduplication at line ~3926 silences chips when the same label fires more than 3 consecutive times. When CC calls `bash` repeatedly to build a multi-slide presentation or multi-row spreadsheet, all chip emissions after the 3rd are suppressed, creating a long silence.

**Fix:** Remove the `consecutiveLabelCount` / `lastEmittedLabel` dedup entirely. Every `tool_use` block should emit a chip. The existing auto-dismiss timer (2s) in Blazor already handles UI clutter. We do NOT want to suppress chips.

Remove these variables and the dedup check:
```js
// REMOVE all of this:
let lastEmittedLabel = '';
let consecutiveLabelCount = 0;
// ... and the if/else block checking consecutiveLabelCount
if (label === lastEmittedLabel) {
    consecutiveLabelCount++;
} else {
    lastEmittedLabel = label;
    consecutiveLabelCount = 1;
}
if (consecutiveLabelCount <= 3) {
    sendEvent(...)
}
```
Replace with: always emit the chip unconditionally.

### Problem 2: Generic Descriptions (harness-server.js)

`resolveProgressLabel` (around line 279) falls through to `'Working...'` for many CC tools. The CC model (Claude Code) uses these tool names:
- `Read` → reading a file (input has `file_path` or `path`)
- `Write` → writing a file (input has `file_path` or `path`, `content`)
- `Glob` → finding files by pattern (input has `pattern`)
- `LS` → listing directory (input has `path`)
- `Grep` → searching code (input has `pattern`, `path`)
- `Task` → subagent spawn (input has `description` or `prompt`)
- `TodoWrite` → writing to-do list
- `TodoRead` → reading to-do list
- `NotebookRead` → reading notebook
- `NotebookEdit` → editing notebook cell

Add handling for these CC-native tool names in `resolveProgressLabel`:

```js
// CC-native tool names (from claude --output-format stream-json)
if (toolName === 'Read') {
    const fp = input.file_path || input.path || '';
    const fname = fp ? fp.split('/').pop() : '';
    return fname ? `Reading: ${chipTrunc(fname, 40)}` : 'Reading file...';
}
if (toolName === 'Write') {
    const fp = input.file_path || input.path || '';
    const fname = fp ? fp.split('/').pop() : '';
    return fname ? `Writing: ${chipTrunc(fname, 40)}` : 'Writing file...';
}
if (toolName === 'Glob') {
    const pat = input.pattern || '';
    return pat ? `Finding: ${chipTrunc(pat, 40)}` : 'Finding files...';
}
if (toolName === 'LS') {
    const dir = input.path || '';
    const dirname = dir ? dir.split('/').pop() || dir : '';
    return dirname ? `Listing: ${chipTrunc(dirname, 40)}` : 'Listing files...';
}
if (toolName === 'Grep') {
    const pat = input.pattern || '';
    return pat ? `Searching: "${chipTrunc(pat, 35)}"` : 'Searching files...';
}
if (toolName === 'Task') {
    const desc = input.description || input.prompt || '';
    return desc ? `Delegating: ${chipTrunc(desc, 40)}` : 'Delegating to agent...';
}
if (toolName === 'TodoWrite' || toolName === 'TodoRead') {
    return toolName === 'TodoWrite' ? 'Updating task list' : 'Checking task list';
}
if (toolName === 'NotebookRead') {
    const fp = input.notebook_path || '';
    const fname = fp ? fp.split('/').pop() : '';
    return fname ? `Reading notebook: ${fname}` : 'Reading notebook...';
}
if (toolName === 'NotebookEdit') {
    const fp = input.notebook_path || '';
    const fname = fp ? fp.split('/').pop() : '';
    return fname ? `Editing notebook: ${fname}` : 'Editing notebook...';
}
```

Also add icon resolution for these in `resolveProgressIcon`:
- `Read` → `'file'`
- `Write` → `'document'`
- `Glob` → `'file'`
- `LS` → `'file'`
- `Grep` → `'search'`
- `Task` → `'agent'`
- `TodoWrite`, `TodoRead` → `'task'`

Add at the top of the `resolveProgressIcon` switch/if-chain (before the `return 'tool'` fallback):
```js
// CC-native PascalCase tool names
if (t === 'read') return 'file';
if (t === 'write') return 'document';
if (t === 'glob' || t === 'ls' || t === 'grep') return 'file';
if (t === 'task') return 'agent';
if (t === 'todowrite' || t === 'todoread') return 'task';
if (t === 'notebookread' || t === 'noteditbootkedit') return 'document';
```

### Problem 3: Assistant Chips Cut Off / Positioned Low (ChatView.razor)

**Root cause:** The `tool-call-indicator-list` div renders AFTER the `streamingMessage` bubble. During CC task execution, `isStreaming = true` (so the streaming bubble renders), but CC suppresses all text content (no tokens emitted). The streaming bubble is empty — the chip list appears beneath an invisible/empty bubble, and can be pushed off-screen or appear cut off.

**Fix:** Move the `tool-call-indicator-list` block to appear BEFORE the streaming message bubble when `_ccTaskActive` is true, so chips appear in the natural message flow position rather than below an empty streaming bubble.

In ChatView.razor around line 213–232, restructure the render order:

Current order (in the `@if (isStreaming)` / chip list section):
1. `@if (isStreaming)` → streaming bubble (empty during CC)
2. `@if (_activeToolCalls.Any() || _toolCallsFading)` → chips (below empty bubble)

Change to:
```razor
@if (_activeToolCalls.Any() || _toolCallsFading)
{
    <div class="tool-call-indicator-list @(_toolCallsFading ? "tool-call-list-fading" : "")">
        @foreach (var tc in _activeToolCalls)
        {
            <div class="tool-call-indicator @(tc.Status == "calling" ? "tool-call-active" : tc.Status == "error" ? "tool-call-error" : "tool-call-done")">
                <MudIcon Icon="@ChipIconToMudIcon(tc.ChipIcon ?? GetChipIconKeyFromToolName(tc.ToolName))"
                         Class="@("tool-call-mud-icon" + (tc.Status == "calling" ? " tool-call-mud-icon-pulse" : ""))"
                         Style="width: 16px; height: 16px; flex-shrink: 0;" />
                <span class="tool-call-summary">@TruncChip(GetToolLabel(tc.ToolName, tc.Server, tc.Summary))</span>
            </div>
        }
    </div>
}
@if (isStreaming)
{
    <MessageBubble Message="@streamingMessage" IsStreaming="true" AssistantConfig="@_assistantConfig" AvatarPreviewUrl="@_avatarPreviewUrl" UserInitial="@UserInitial" />
}
```

This puts chips above the streaming bubble. The streaming bubble follows the chips when text eventually arrives.

---

## ADO#4912 — Task Mode: Pill Not Activating + False Positive Workspace Check

### Problem 1: Task Selector Pill Not Activating (ChatView.razor)

**Root cause:** `btn-task-mode--active` CSS class is gated on `_taskMode` (line 365):
```razor
<button class="btn-task-mode @(_taskMode ? "btn-task-mode--active" : "")">
```

But when the harness auto-classifies a request as task mode and emits `mode_switch`, the code sets `_taskModeActive = true` but does NOT set `_taskMode = true`. So the header indicator lights up but the pill stays inactive.

**Fix:** On `mode_switch` event, also set `_taskMode = true`:
```csharp
else if (evt.Type == "mode_switch")
{
    _taskModeActive = true;
    _taskMode = true;  // ADO#4912 — sync pill with task mode indicator
    await InvokeAsync(StateHasChanged);
}
```

Also update the button binding to use `_taskModeActive` as the active state (which is the canonical "task is running" flag), OR use `_taskMode || _taskModeActive`:
```razor
<button class="btn-task-mode @((_taskMode || _taskModeActive) ? "btn-task-mode--active" : "")">
```

Use BOTH approaches: set `_taskMode = true` on `mode_switch` AND use `(_taskMode || _taskModeActive)` for the CSS class. This ensures the pill stays active whenever either flag is true.

### Problem 2: False Positive Workspace Check (harness-server.js)

**Root cause:** The CC brief context injects `Recent Workspace Artifacts` (prior task files from DB) into the CC prompt. When CC calls `list_workspace_files` or simply reads the brief, it sees prior files and may conclude the user's requested task already completed. The `CLAUDE.md` workspace rules also say "At the start of each task, you will receive a list of files currently in the working folder. Use this to understand what already exists before writing anything." This causes CC to check existing files and misinterpret them as current task output.

**Root cause detail:** The `Recent Workspace Artifacts` section is pulled from `user_workspace_uploads WHERE source IN ('assistant','cc')` — it returns ALL prior task files, not scoped to the current conversation or folder. When the new task is similar to a prior task (e.g., another spreadsheet), CC sees the old spreadsheet, reads its context, and responds "It looks like that already completed."

**Fix in harness-server.js (CC context path, around line 3529):**

Change the `Recent Workspace Artifacts` section to:
1. Scope to the CURRENT folder only (filter by `folder_id = taskFolderIdResolved`)
2. Label it explicitly as "EXISTING files in this folder (from prior sessions)" NOT as completion evidence

Replace:
```js
contextParts.push(`## Recent Workspace Artifacts\nRecent assistant-created files: ${fileList}`);
```
With:
```js
contextParts.push(`## Existing Files in This Folder\nThese files already exist in the working folder from prior sessions. They are reference material — NOT evidence that the current task completed. Execute the user's request fresh:\n${fileList}`);
```

AND filter the workspace files query to only include the current folder:
```js
// Only show files in the current task folder (not all prior artifacts)
const briefResp = await fetch(`${FAIT_BASE_URL}/api/workspace/files?type=generated&limit=10&userId=${encodeURIComponent(userId)}&folderId=${encodeURIComponent(taskFolderIdResolved || '')}`, { headers });
```

Also add to the CC brief's `EXECUTE_DIRECTIVE` an explicit line:
```
DO NOT assume a prior artifact means this task is already done. Prior files in the folder are from PREVIOUS sessions. Execute the user's current request now.
```

Add this line to the existing `EXECUTE_DIRECTIVE` const (around line 3833).

---

## Files to Modify

1. `agent-harness/harness-server.js`:
   - Remove `lastEmittedLabel` / `consecutiveLabelCount` dedup (lines ~3880-3927)
   - Add CC-native tool names to `resolveProgressLabel` (after line ~345 before the final `return 'Working...'`)
   - Add CC-native tool names to `resolveProgressIcon` (after line ~366 before the final `return 'tool'`)
   - On `mode_switch` (but this is harness; mode_switch is handled in Blazor)
   - Fix workspace brief label + folder scoping (line ~3533-3540)
   - Add stale-artifact warning to `EXECUTE_DIRECTIVE` (line ~3833)

2. `src/FortressAI.Web/Components/Chat/ChatView.razor`:
   - Move chip list before streaming bubble (~line 213-232)
   - Set `_taskMode = true` on `mode_switch` event (~line 1102-1106)
   - Update pill active CSS class to use `(_taskMode || _taskModeActive)` (~line 365)

---

## Constraints

- Do NOT add new SSE event types
- Do NOT change DB schema
- Do NOT change the `list_workspace_files` API handler
- Do NOT touch auth, routing, or MCP dispatch
- Keep all existing ADO comment references intact
- `dotnet build` must have 0 errors
- JS must not introduce syntax errors (no undefined variables)

## Output

When done, run:
```bash
cd /home/fredw/projects/fip/fait && dotnet build src/FortressAI.Web/FortressAI.Web.csproj --no-restore 2>&1 | tail -5
```
And print the result.
