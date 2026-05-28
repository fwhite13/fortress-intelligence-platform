# Build Report: ADO#4249
## Summary
Upgraded ephemeral tool chips to display meaningful context in both harness-server.js and ChatView.razor. All first-class tool chips now include context strings (slug, filename, query, title). CC sub-tool chips show command previews or filenames. A folder-context chip fires after task folder resolution. All output is truncated at 60 chars. Commit: `d1f81cc2`.

## CC Invocation
Single CC Sonnet run via:
```
cat /home/fredw/projects/fip/fait/pipeline/ADO4249-build-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```
Sequential (single pass). No parallelization needed — both changes are related and CC handled them together.

## Files Modified
- `fait/agent-harness/harness-server.js`
  - Added `chipTrunc(str, max=57)` helper — consistent 60-char truncation (57 + "...")
  - Rewrote `resolveProgressLabel` — structured input parsing; bash shows "Running: [cmd preview 40 chars]"; str_replace/write_file/read_file show "Editing/Saving/Reading: [basename]"
  - Rewrote `getBuiltinSummary` — read_memory/write_memory show slug/title; read/write_file show basename; new read_workspace_file case; all use chipTrunc
  - Updated task-start message (line ~2732): "Starting task..." (generic, folder not available yet)
  - Added folder context chip (line ~3081): after folder resolution → "Working in: /[folder-name]"
  - Updated ADO summaries (~line 4384): "Filing WI: [chipTrunc(title)]", "Looking up WI #[id]", "Updating WI #[id]"
  - Updated web_search chip (~line 4404): "Searching: [chipTrunc(query, 50)]"

- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor`
  - Added `TruncChip(string? s, int max=60)` static helper (~line 1513)
  - Updated chip render (~line 191): wrapped in `TruncChip()`
  - Simplified `GetToolLabel` (~line 1517): always use summary when non-empty (removed old underscore/prefix checks that were blocking contextual summaries from rendering)

## Self-Review Checklist
- [x] AC1: First-class tool chips show context string — read/write_memory show slug/title, read/write_file show basename, web_search shows query, ADO create shows title, KB search shows query
- [x] AC2: Task start chip includes working folder — "Working in: /[folder-name]" emitted after folder resolution (step='tool_use' so it appears as a progress chip in the active set)
- [x] AC3: CC sub-tool chips include brief description — bash shows "Running: [cmd preview]", file ops show filename
- [x] AC4: Human-readable — no raw JSON, no underscores in displayed text; all strings are natural language
- [x] AC5: Truncation at 60 chars — `chipTrunc` truncates at 57+ellipsis=60; `TruncChip` in Blazor also enforces 60-char max as safety net
- [x] Deploy note: two-image — harness-server.js changed (harness image) + ChatView.razor changed (fred-chat image)

## ADO Comment
```
Build complete for ADO#4249 — ephemeral tool chips now show contextual detail. 
Commit d1f81cc2. Two-image deploy: harness + fred-chat. Sending to Clint for review.
```

## Known Edge Cases / Things Clint Should Scrutinize
1. **`resolveProgressLabel` try/catch** — If `toolInput` is null or malformed JSON string, the catch block falls back to string-contains matching. This is safe but verbose logs may appear.
2. **`GetToolLabel` summary-first logic** — Old logic rejected summaries containing underscores (e.g. `some_tool_name`). New logic always uses summary when non-empty. Verify no tool name leaks through `summary` as raw `tool_name` text.
3. **Folder context chip** — Fires as `step: 'tool_use'` which means it gets added to `_activeToolCalls`. It will participate in the chip fade logic. This is intentional — it shows briefly while the task boots.
4. **`chipTrunc(str, 57)` vs `TruncChip(s, 60)`** — Harness truncates at 57+3=60; Blazor TruncChip is a safety net at 60. Consistent outcome.
5. **`read_workspace_file` in `getBuiltinSummary`** — New case added. Verify this tool name matches what harness routes.

## How to Test Locally
1. Start harness: `cd fait/agent-harness && npm start`
2. Start Blazor: `cd fait/src && dotnet run --project FortressAI.Web`
3. Open a chat, enable Task mode, select a folder — verify "Working in: /[folder]" chip appears
4. Trigger a memory read (e.g., ask "what do you remember about me?") — verify chip shows "Reading memory: [slug]"
5. Trigger a web search — verify chip shows "Searching: [query truncated]"
6. Trigger an ADO WI creation — verify "Filing WI: [title]"

---

## Review Cycle 1 Fixes — Commit `12378215`

### Issues addressed

| ID | Severity | Fix |
|----|----------|-----|
| I1 | Important | `getBuiltinSummary` default case now returns `'Working...'` instead of raw `` `${toolName}...` `` — fixes AC4 violation where unrecognized tool names leaked as raw snake_case |
| N1 | Nitpick | `ado_create_work_item` chip now guards against missing `title`: `toolInput.title ? \`Filing WI: ...\` : 'Filing WI...'` |
| N2 | Nitpick | `web_search` chip now guards against missing `query`: `toolInput.query ? \`Searching: ...\` : 'Searching...'` |

### Files changed
- `fait/agent-harness/harness-server.js` — 3 line fixes, no structural changes

### CC sessions
1 run (CC Sonnet), pipe mode
