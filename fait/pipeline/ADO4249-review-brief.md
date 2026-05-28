# CC Review Brief: ADO#4249 — Ephemeral Chips: Contextual Detail

## Your Role
You are an adversarial code reviewer. Read the specified files and verify the implementation against the acceptance criteria and specific concerns below. Report findings with severity (Critical/Important/Nitpick), file, line, and evidence.

## Files to Read (in full or targeted sections)
1. `/home/fredw/projects/fip/fait/agent-harness/harness-server.js` — lines 263–382 (chipTrunc, resolveProgressLabel, getBuiltinSummary, extractFilename)
2. `/home/fredw/projects/fip/fait/agent-harness/harness-server.js` — lines 3070–3095 (folder context chip)
3. `/home/fredw/projects/fip/fait/agent-harness/harness-server.js` — lines 4375–4440 (ADO summaries, web_search chip)
4. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — lines 1510–1555 (TruncChip, GetToolLabel)
5. `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — lines 186–195 (chip render)

## Acceptance Criteria (from WI)
1. All first-class tool chips include context string
2. Task start chip includes working folder name
3. CC sub-tool chips include brief description
4. Human-readable (no raw JSON or underscores)
5. Truncation at 60 chars with ellipsis

---

## Specific Checks to Perform

### CHECK 1: chipTrunc correctness (harness-server.js ~line 263)

Read the `chipTrunc(str, max=57)` function.

Questions to verify:
a. **Output length guarantee**: When `str.length > max` (57), it returns `str.substring(0, 57) + '...'` which is 60 chars. ✓ or ✗? Prove it.
b. **Null/undefined input**: What does `if (!str) return '';` handle? Does `!str` cover: `null`, `undefined`, `''`, `0`, `false`? Note: passing `0` would falsely return empty string — is that a risk in practice given call sites?
c. **Non-string input**: The function calls `String(str).trim()` — does this handle numbers, objects? What if `toolInput.title` is `undefined` — does `chipTrunc(undefined)` return `''` or crash?
d. **The `?? ''` pattern at call sites**: At line 4384, `chipTrunc(toolInput.title ?? '')` — does this double-guard correctly, or is `chipTrunc` receiving `''` and returning `''` (leading to "Filing WI: " with no context)?

### CHECK 2: TruncChip in ChatView.razor (~line 1513)

Read the `TruncChip(string? s, int max = 60)` static helper.

Questions to verify:
a. **Null input**: What does `TruncChip(null)` return?
b. **Output length when truncating**: `s[..57] + "..."` = 60 chars max. Is this correct? (57 + 3 = 60 ✓)
c. **Edge case — exactly 60 chars**: If string is 60 chars, does it get truncated or pass through? Verify: `s.Length > max` means 60 chars is NOT truncated (correct?).
d. **Harness vs Blazor truncation consistency**: harness chipTrunc uses max=57 (57+3=60), Blazor TruncChip uses max=60 but truncates to [..57]+3. Are these producing the same result? A 58-char string: harness → 57+3=60 chars; Blazor → 58 chars (not truncated, since 58 <= 60). This means Blazor allows 58–60 char labels through without truncation while harness would truncate at 58. Is this a meaningful discrepancy or acceptable?

### CHECK 3: resolveProgressLabel regression check (harness-server.js ~line 270)

Read the entire `resolveProgressLabel` function.

Questions to verify:
a. **All tool names still handled**: Verify these tool names are handled: `bash`, `computer`, `str_replace_based_edit_tool`, `str_replace_editor`, `write_file`, `read_file`, `list_files`. Any others that were in a previous version that are now missing?
b. **Bash cmd null guard**: `const cmd = input.command || input.cmd || '';` — if both are undefined/null/missing, `cmd = ''`. The `if (cmd)` check then skips the structured path and falls through to rawStr-based matching. Is the rawStr fallback still meaningful? What does `JSON.stringify({}).toLowerCase()` produce for an empty input? Does it produce false positives?
c. **Windows-style paths**: `fp.split('/').pop()` — if `fp = 'C:\\Users\\foo\\bar.js'`, this returns `'C:\\Users\\foo\\bar.js'` (the whole string). Is that a real risk in this harness? (It runs in Linux containers — probably not, but confirm.)
d. **No-slash paths**: If `fp = 'myfile.txt'` (no `/`), `.split('/').pop()` returns `'myfile.txt'` ✓. Correct behavior.
e. **Empty path string**: If `fp = ''`, `.split('/').pop()` returns `''`. Then `fname = ''`, so `fname ?` is falsy → falls through to fallback string. ✓ Correct.

### CHECK 4: getBuiltinSummary exhaustiveness (harness-server.js ~line 328)

Read the entire `getBuiltinSummary` function.

Questions to verify:
a. **Default case**: The `switch` has `default: return \`${toolName}...\``. This returns the raw tool name with `...` appended. Example: `getBuiltinSummary('some_unknown_tool', {})` returns `'some_unknown_tool...'`. Is this human-readable? Does it violate AC4 (no underscores in display)?
b. **Missing cases**: Compare against the tools that appear in the `GetToolLabel` switch in ChatView.razor. Are there tools in ChatView that map to labels but are NOT in `getBuiltinSummary`? Specifically: `search_knowledge_base`, `search_memory`, `read_memory`, `write_memory`, `list_workspace_files`, `create_document`, `read_file`, `write_file`, `list_files`, `read_workspace_file`. List any gaps.
c. **read_memory null guard**: `const slug = toolInput.slug || toolInput.key || toolInput.id || '';` — what if `toolInput` itself is null/undefined (not just missing properties)? The function is called as `getBuiltinSummary(name, toolInput)` where `toolInput` may be an empty object `{}`. Check: can `toolInput` be null?
d. **Fallback for empty docName**: `case 'create_document': const docName = toolInput.filename || toolInput.title || 'document';` — has a hardcoded fallback of `'document'`. This is fine and intentional.

### CHECK 5: GetToolLabel simplification (ChatView.razor ~line 1517)

Read the `GetToolLabel` method.

Questions to verify:
a. **Old underscore-filter behavior**: The old filter blocked summaries containing underscores. The new code uses `if (!string.IsNullOrWhiteSpace(summary)) return summary`. What if the harness sends a raw tool name as the summary (e.g., `summary = "read_memory"`)? Would this now display `read_memory` as the chip label? Trace: in `getBuiltinSummary`, `read_memory` returns `"Reading memory..."` (no underscores). In `resolveProgressLabel`, what is returned for tool names from the CC path? What does CC set as `summary`? Check line ~1591 in ChatView.razor to see how summary is set from `payload.Summary`.
b. **Empty string chip**: `summary = payload.Summary ?? ""`. If harness sends `summary: ""` (empty string), `string.IsNullOrWhiteSpace("")` is true → falls through to switch. ✓ Correct.
c. **Whitespace-only summary**: If `summary = "   "`, `IsNullOrWhiteSpace` catches it → falls through. ✓ Correct.
d. **Chip render — null label**: At line 191, `@TruncChip(GetToolLabel(...))`. `GetToolLabel` always returns a non-null, non-empty string (default case returns `"Working..."`). So `TruncChip` always receives a non-null non-empty string here. ✓ No empty chip risk.

### CHECK 6: Folder context chip (harness-server.js ~line 3082)

Read lines 3070–3095.

Questions to verify:
a. **Null guard on folder.name**: The code has `if (folder && folder.name)` — this guards against null folder and null/empty name. ✓ But what if `folder.name` is `undefined` vs `null`? JS: `if (folder && folder.name)` treats both as falsy. ✓ Safe.
b. **"Working in: undefined" scenario**: If `folder.name` is explicitly `undefined`, it's filtered by the guard above. ✓ Won't emit.
c. **Race condition**: The folder context chip fires synchronously after `await resolveTaskFolder(...)` completes within the same try block. It fires before the conversation loop starts. Is there a race condition? The folder resolution and chip emission happen sequentially in the same async flow — no race.
d. **Folder resolution failure**: If `resolveTaskFolder` throws, execution jumps to the `catch` block at ~line 3088: `console.warn(...)` and continues. The chip emission code is inside the `try` block BEFORE `mkdirSync` — so if resolveTaskFolder succeeds but mkdirSync fails, does the chip still fire? Read carefully: chip emission is at ~line 3082 which is AFTER `mkdirSync(folderLocalDir, {recursive: true})`. Check exact ordering: resolveTaskFolder → folderLocalDir assignment → mkdirSync → chip emission? Or different order?
e. **Step type**: Chip is emitted as `step: 'tool_use'` — this means ChatView treats it as a tool call chip (added to `_activeToolCalls`), not a start/done event. It will participate in chip fade logic. Is this correct, or should it be `step: 'start'`?

### CHECK 7: ADO label for undefined title (harness-server.js ~line 4384)

Read the ADO summaries object.

Questions to verify:
a. **ado_create_work_item with undefined title**: `chipTrunc(toolInput.title ?? '')` — if `title` is undefined, `?? ''` produces empty string. `chipTrunc('')` returns `''` (since `!str` is true for `''`). Result: `"Filing WI: "` (with trailing space, no title). Is this acceptable or should there be a fallback like `"Filing WI..."` when title is empty?

### CHECK 8: AC compliance check

For each acceptance criterion, state whether it is met:
1. All first-class tool chips include context string — verify by tracing what each tool returns
2. Task start chip includes working folder name — verify folder chip code
3. CC sub-tool chips include brief description — verify resolveProgressLabel covers bash, file ops
4. Human-readable (no raw JSON or underscores) — check default case of getBuiltinSummary (returns `toolName...` with potential underscores!)
5. Truncation at 60 chars — verify chipTrunc + TruncChip

---

## Verdict Criteria
- **PASS**: All ACs met, no Critical issues, Important issues are minor
- **NEEDS-CHANGES**: Important issues found (AC violation, edge case that produces bad UX, underscore leak in default case)
- **FAIL**: Critical issues (crash path, null dereference in hot path, fundamental AC failure)

Focus on real issues, not hypotheticals. This is a UI chip feature — the blast radius of edge cases is limited to cosmetic chip label display. Prioritize accordingly.
