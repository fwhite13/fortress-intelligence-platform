# Build Brief: ADO#4249 — Ephemeral Tool Chips: Show Contextual Detail

## Overview

Upgrade ephemeral tool chips to display meaningful context alongside the chip label — the slug, filename, query, title, or folder name — rather than just a generic tool name.

**Two files to change:**
1. `fait/agent-harness/harness-server.js` — update `getBuiltinSummary`, `resolveProgressLabel`, ADO/graph/web_search chip summaries, and task-start chip message
2. `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — update `GetToolLabel` to incorporate a `Context` field; also update `ToolCallPayload` record in `fait/src/FortressAI.Web/Services/IUserAgentRuntime.cs`

---

## Current State

### harness-server.js

**`getBuiltinSummary` (line ~297)** — already emits minimal context for some tools:
```js
case 'search_knowledge_base': return `Searching knowledge base: "${(toolInput.query||'').substring(0,50)}"`;
case 'search_memory': return 'Searching memory...';
case 'read_memory': return 'Reading memory...';
case 'write_memory': return 'Saving to memory...';
case 'create_document': return `Creating document: "${toolInput.filename||toolInput.title||'document'}"`;
case 'read_file': return `Reading file: ${toolInput.path||''}`;
case 'write_file': return `Saving file: "${toolInput.path || 'file'}"`;
```

**`resolveProgressLabel` (line ~262)** — used for CC sub-tool chips. Gives coarse labels like "Reading files...", "Running command..." but no specific file/command details.

**Task start chip (line ~2689):**
```js
sendEvent({ type: 'task_progress', payload: JSON.stringify({ step: 'start', status: 'starting', message: 'Starting Claude Code task...' }) });
```
At this point `folder` object is available (folder.name). The chip should include the folder name.

**ADO tools (line ~4278):**
```js
ado_create_work_item: `Creating work item: ${toolInput.title ?? ''}...`
```
Already has a title — just ensure it's truncated.

**web_search (line ~4302):**
```js
emitToolCall(res, 'brave', 'web_search', 'calling', `Searching the web for: ${toolInput.query ?? ''}`);
```
Already includes query — just needs truncation to ~60 chars.

### Blazor: GetToolLabel (ChatView.razor line ~1497)

Currently checks: if summary is non-empty and doesn't start with "Calling " and has no underscores → use summary. Otherwise falls back to a switch on toolName.

The rendering at line ~175:
```razor
<span class="tool-call-summary">@GetToolLabel(tc.ToolName, tc.Server, tc.Summary)</span>
```
Uses only `Summary`. There is no `Context` field.

### ToolCallPayload (IUserAgentRuntime.cs line ~130)

```csharp
public record ToolCallPayload(
    [property: JsonPropertyName("server")] string? Server,
    [property: JsonPropertyName("toolName")] string? ToolName,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("summary")] string? Summary
);
```

---

## What to Change

### Strategy

Rather than adding a separate `context` field to the payload, **encode the full human-readable chip text directly in the `summary` field** (it's already the free-text description field). The harness already passes summary to `emitToolCall`, and Blazor already renders it. This means:
- No payload schema changes needed (simpler)
- Just improve the content of what goes into `summary` in the harness
- Improve `GetToolLabel` in Blazor to use `summary` more aggressively

### 1. `harness-server.js` — Truncation helper

Add a truncation helper near the top of utility functions (after line ~290):

```js
// ADO#4249 — Truncate context strings for chip display
function chipTrunc(str, max = 57) {
    if (!str) return '';
    str = String(str).trim();
    return str.length > max ? str.substring(0, max) + '...' : str;
}
```

### 2. `harness-server.js` — Update `getBuiltinSummary`

Replace the existing function (line ~297–310) with enriched, truncated context:

```js
function getBuiltinSummary(toolName, toolInput) {
    switch(toolName) {
        case 'search_knowledge_base': return `Searching KB: "${chipTrunc(toolInput.query||'', 50)}"`;
        case 'search_memory': return `Searching memory: "${chipTrunc(toolInput.query||'', 50)}"`;
        case 'read_memory': {
            const slug = toolInput.slug || toolInput.key || toolInput.id || '';
            return slug ? `Reading memory: ${chipTrunc(slug)}` : 'Reading memory...';
        }
        case 'write_memory': {
            const title = toolInput.title || toolInput.slug || toolInput.key || '';
            return title ? `Saving memory: ${chipTrunc(title)}` : 'Saving to memory...';
        }
        case 'list_workspace_files': return 'Listing workspace files...';
        case 'create_document': {
            const docName = toolInput.filename || toolInput.title || 'document';
            return `Creating: ${chipTrunc(docName)}`;
        }
        case 'read_file': {
            const fp = toolInput.path || toolInput.filename || '';
            const fname = fp.split('/').pop();
            return fname ? `Reading: ${chipTrunc(fname)}` : 'Reading file...';
        }
        case 'write_file': {
            const fp = toolInput.path || toolInput.filename || '';
            const fname = fp.split('/').pop();
            return fname ? `Saving: ${chipTrunc(fname)}` : 'Saving file...';
        }
        case 'list_files': return 'Listing files...';
        case 'read_workspace_file': {
            const fp = toolInput.path || toolInput.filename || '';
            const fname = fp.split('/').pop();
            return fname ? `Reading: ${chipTrunc(fname)}` : 'Reading workspace file...';
        }
        default: return `${toolName}...`;
    }
}
```

### 3. `harness-server.js` — Update `resolveProgressLabel` (CC sub-tool chips)

This function is used for CC sub-tool tool_use chips. Update it to extract specific file/command context:

Replace the existing `resolveProgressLabel` function (lines ~262–290) with:

```js
function resolveProgressLabel(toolName, toolInput) {
    try {
        // toolInput may be a string (JSON) or object — normalize
        const input = typeof toolInput === 'string' ? JSON.parse(toolInput || '{}') : (toolInput || {});
        const rawStr = JSON.stringify(input).toLowerCase();

        if (toolName === 'bash' || toolName === 'computer') {
            const cmd = input.command || input.cmd || '';
            if (cmd) {
                // Detect meaningful command types and show a brief preview
                if (/pip\s*install|pip3\s*install/.test(cmd)) return 'Installing dependencies...';
                if (/openpyxl|\.xlsx|xlrd|xlwt/.test(cmd)) return 'Building spreadsheet...';
                if (/pptx|python-pptx/.test(cmd)) return 'Building presentation...';
                if (/docx|python-docx/.test(cmd)) return 'Building document...';
                if (/python3?\s+\S+\.py/.test(cmd)) return 'Running Python script...';
                // Show first ~40 chars of command
                const preview = chipTrunc(cmd.replace(/\n/g, ' ').trim(), 40);
                return `Running: ${preview}`;
            }
            if (rawStr.includes('pip install') || rawStr.includes('pip3 install')) return 'Installing dependencies...';
            if (rawStr.match(/\.xlsx|openpyxl|xlrd/)) return 'Building spreadsheet...';
            if (rawStr.includes('pptx')) return 'Building presentation...';
            if (rawStr.includes('docx')) return 'Building document...';
            if (rawStr.match(/python3? /) || rawStr.match(/\.py\b/)) return 'Running Python script...';
            if (rawStr.match(/\b(ls|find|cat|head|tail|grep)\b/)) return 'Reading files...';
            if (rawStr.includes('curl ') || rawStr.includes('wget ') || rawStr.includes('requests')) return 'Fetching data...';
            return 'Running command...';
        }
        if (toolName === 'write_file' || toolName === 'str_replace_based_edit_tool' || toolName === 'str_replace_editor') {
            const fp = input.path || input.filename || '';
            const fname = fp ? fp.split('/').pop() : '';
            if (toolName === 'str_replace_based_edit_tool' || toolName === 'str_replace_editor') {
                return fname ? `Editing: ${chipTrunc(fname)}` : 'Editing file...';
            }
            return fname ? `Saving: ${chipTrunc(fname)}` : 'Saving file...';
        }
        if (toolName === 'read_file') {
            const fp = input.path || input.filename || '';
            const fname = fp ? fp.split('/').pop() : '';
            return fname ? `Reading: ${chipTrunc(fname)}` : 'Reading file...';
        }
        if (toolName === 'list_files') return 'Listing files...';
        return 'Working...';
    } catch {
        // Fall back gracefully if toolInput cannot be parsed
        const rawStr = (typeof toolInput === 'string' ? toolInput : JSON.stringify(toolInput || '')).toLowerCase();
        if (rawStr.includes('pip install')) return 'Installing dependencies...';
        return 'Working...';
    }
}
```

### 4. `harness-server.js` — Update task-start chip (line ~2689)

The `folder` variable may not be resolved at this exact line (it's resolved at ~2982). The task-start chip fires BEFORE folder resolution for gate-check purposes. However, there is a second opportunity to emit a contextual start chip AFTER folder is resolved.

Check the code flow: the first `task_progress start` (line 2689) fires before the gate check. After the gate passes and folder is resolved (~2982), we can emit an additional informational chip or update the message.

**Change 1:** Update the initial task-start message to be generic (it stays as-is since folder isn't available yet):
```js
sendEvent({ type: 'task_progress', payload: JSON.stringify({ step: 'start', status: 'starting', message: 'Starting task...' }) });
```

**Change 2:** After `folder` is resolved (after line ~2983), emit a folder context chip:
```js
if (folder && folder.name) {
    sendEvent({ type: 'task_progress', payload: JSON.stringify({ 
        step: 'tool_use', toolName: 'folder', status: 'calling', 
        message: `Working in: /${chipTrunc(folder.name, 40)}`
    }) });
}
```

Actually — re-reading the code, let me check where exactly folder resolution happens relative to the gate check to ensure no regression. Let me check more carefully.

Looking at lines 2689 vs 2982: line 2689 is inside `if (taskMode)` block, before the gate check. Line 2982 is AFTER the gate check passes. So we can:
- Keep line 2689 as-is (generic "Starting task...")  
- Add folder chip AFTER line 2983 (after folder is resolved)

### 5. `harness-server.js` — Update web_search chip (truncate query)

Find this line (~4302):
```js
emitToolCall(res, 'brave', 'web_search', 'calling', `Searching the web for: ${toolInput.query ?? ''}`);
```
Replace with:
```js
emitToolCall(res, 'brave', 'web_search', 'calling', `Searching: ${chipTrunc(toolInput.query ?? '', 50)}`);
```

### 6. `harness-server.js` — Update ADO create_work_item chip (truncate title)

Find this line (~4284):
```js
ado_create_work_item: `Creating work item: ${toolInput.title ?? ''}...`,
```
Replace with:
```js
ado_create_work_item: `Filing WI: ${chipTrunc(toolInput.title ?? '')}`,
```

Also update the other ADO summaries to use `chipTrunc` where they include dynamic data:
```js
ado_get_work_item: `Looking up WI #${toolInput.id ?? ''}`,
ado_update_work_item: `Updating WI #${toolInput.id ?? ''}`,
```

### 7. `ChatView.razor` — Update `GetToolLabel` to use summary more aggressively

The current check at line ~1504:
```csharp
if (!string.IsNullOrWhiteSpace(summary) && !summary.StartsWith("Calling ") && !summary.Contains('_'))
    return summary;
```

With our harness improvements, summary now always contains meaningful text. Simplify to always prefer summary when non-empty:

```csharp
private static string GetToolLabel(string toolName, string server, string? summary = null)
{
    // ADO#4249 — prefer harness summary (now always contextual) when present
    if (!string.IsNullOrWhiteSpace(summary))
        return summary;

    return toolName switch
    {
        // ... keep existing fallbacks unchanged
    };
}
```

Also add a truncation helper in C# for display safety (in case something slips through):
```csharp
private static string TruncChip(string? s, int max = 60) =>
    s == null ? "" : s.Length > max ? s[..57] + "..." : s;
```

And use it in the rendering:
```razor
<span class="tool-call-summary">@TruncChip(GetToolLabel(tc.ToolName, tc.Server, tc.Summary))</span>
```

---

## Files to Modify

1. **`/home/fredw/projects/fip/fait/agent-harness/harness-server.js`**
   - Add `chipTrunc` helper after line ~260 (after `emitToolCall`)
   - Replace `resolveProgressLabel` (lines ~262–290)
   - Replace `getBuiltinSummary` (lines ~297–310)
   - Update task-start chip (line ~2689): after folder resolution at ~2983, add folder context chip
   - Update web_search chip (~4302): add chipTrunc to query
   - Update ADO summaries (~4278): add chipTrunc, improve ado_create_work_item wording

2. **`/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor`**
   - Update `GetToolLabel` (~line 1497): always use summary when non-null/empty
   - Add `TruncChip` static helper
   - Update chip rendering (~line 175): wrap label in `TruncChip()`

**No schema changes needed** — we're encoding context directly in the existing `summary` field.

---

## Constraints

- Do NOT modify any other harness endpoint logic
- Do NOT change the SSE event type names or payload field names (other than enriching summary content)
- Do NOT change chip fade/timing logic
- Do NOT modify ToolCallPayload or TaskProgressPayload record shapes
- Preserve all existing `emitToolCall` call sites — just change the summary string passed to them
- Keep C# code idiomatic — use pattern matching, null-coalescing
- All chip text must be human-readable (no raw JSON, no underscores in tool names)
- Test compilation: run `dotnet build` in `/home/fredw/projects/fip/fait/src/` after Blazor changes

---

## Acceptance Criteria to Verify

After making changes, verify:
1. `getBuiltinSummary('read_memory', { slug: 'preferences' })` → "Reading memory: preferences"
2. `getBuiltinSummary('write_memory', { title: 'User Preferences' })` → "Saving memory: User Preferences"
3. `getBuiltinSummary('write_file', { path: '/workspace/summary.md' })` → "Saving: summary.md"
4. `getBuiltinSummary('read_file', { path: '/workspace/quarterly-report.xlsx' })` → "Reading: quarterly-report.xlsx"
5. `web_search` chip includes truncated query (first 50 chars)
6. `ado_create_work_item` shows "Filing WI: [title]"
7. After folder resolution, a chip shows "Working in: /[folder-name]"
8. CC sub-tool chips: bash shows "Running: [cmd preview]", read_file shows "Reading: [filename]"
9. Long strings are truncated with ellipsis at 57 chars + "..." = 60 total
10. `dotnet build` passes with no errors

---

## Output

After making all changes, output a summary of:
- Every line range modified in each file
- Any edge cases encountered
- Confirmation that `dotnet build` passed

Save this output to `/home/fredw/projects/fip/fait/pipeline/ADO4249-cc-output.txt`
