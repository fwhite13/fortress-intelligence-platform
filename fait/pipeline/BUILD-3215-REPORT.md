# Build Report — ADO#3215

## What was built
Replaced the single-pass `ConverseStream` call in `harness-server.js` with an agentic loop that feeds tool results back to Bedrock as proper `toolResult` blocks, enabling the model to incorporate tool output into its response rather than blindly streaming it into the chat bubble.

## Files changed
- `fait-v2/agent-harness/harness-server.js` — Replaced single `for await` stream loop with `while (continueLoop && toolIterations < MAX_TOOL_ITERATIONS)` agentic loop. All tool dispatch logic preserved. Token counting changed from `=` to `+=` to accumulate across iterations.

## Commit
`89e4557e` — ADO#3215 — KB tool results: agentic loop (toolResult blocks)

## Parallelization used
No — single CC Opus task, sequential.

## CC sessions run
1 — CC Opus (`--model opus`), single pass. Output clean on first run.

## Acceptance criteria verification
- [x] `sendEvent({ type: 'text', content: toolResultText })` GONE — `grep` confirms no matches
- [x] `while (continueLoop && toolIterations < MAX_TOOL_ITERATIONS)` present at line 1777
- [x] Token counting uses `+=` at line 1939
- [x] `node --check` syntax validation passes clean
- [x] All tool dispatch cases preserved (list_workspace_files, read_memory, write_memory, create_document, list_files, read_file, search_knowledge_base)
- [x] `create_document` artifact SSE emission unchanged
- [x] `write_memory` confirmation behavior unchanged
- [x] `messageStopSeen` / metadata-after-messageStop pattern preserved

## What changed (structural)

**Before:**
1. Single `ConverseStreamCommand` call
2. Stream events: text → `sendEvent({type:'text'})`; toolUse → execute tool → `sendEvent({type:'text', content:toolResult})` ← **WRONG**
3. Done

**After:**
1. `while (continueLoop && toolIterations < 10)` loop
2. Each iteration: call `ConverseStreamCommand`, stream events
3. Text deltas → `sendEvent({type:'text'})` (unchanged)
4. toolUse complete → execute tool, accumulate `assistantContent`, store `pendingToolResult` (no sendEvent for tool result)
5. After stream ends: if `pendingToolResult` → append `assistant` message (assistantContent) + `user` message (toolResult block) → loop
6. If no pending tool → `continueLoop = false`, exit
7. Model's follow-up response after tool result is streamed as normal text

## Known edge cases / things Clint should scrutinize
1. **Multi-tool turns** — The current implementation handles one tool call per iteration. If Bedrock emits multiple toolUse blocks in a single turn (contentBlockStop fires twice), only the last `pendingToolResult` would be kept. This is unlikely with current tool configs but worth noting. The proper fix would be an array of pendingToolResults — but Bedrock typically serializes tool calls.
2. **assistantContent text accumulation** — Text is accumulated into `assistantTextAccumulator` and pushed to `assistantContent` when a toolUse block arrives (or at stream end). If no tool is called, `assistantContent` ends up with just a text block — this is valid but it's extra data not used for anything (since we only push to `messages` when a tool result needs to loop back).
3. **create_document / write_memory** — These tools still loop back for a follow-up model response. The model's follow-up is typically "I've created the document" — brief and acceptable. No behavior change from the spec intent.
4. **Max iterations guard** — 10 iterations hard cap. If the model chains 10+ tool calls it will silently exit. Log line is emitted at each iteration so this is visible in CloudWatch.

## How to test locally
1. Start harness: `cd fait-v2/agent-harness && node harness-server.js`
2. Send a turn that triggers `search_knowledge_base` (ask a question about something in the KB)
3. Expected: model response incorporates KB results as context (not raw `[KB Search Results]` text dumped into chat)
4. Check harness logs for: `tool result fed back to Bedrock, looping` → `end_turn with no tool call, exiting agentic loop`
5. Also test `read_memory`, `list_files`, `read_file`, `list_workspace_files` for same behavior
6. Test `create_document` to confirm artifact SSE still emits correctly

---

## Build Report — ADO#3215 (Review Cycle 2)

### Fixes Applied
All 4 issues flagged by Clint resolved:

1. **Fix 1 (CRITICAL)** — `pendingToolResult` scalar replaced with `pendingToolResults` array (`const pendingToolResults = []`). Handles multiple toolUse blocks in a single Bedrock turn.
2. **Fix 2 (CRITICAL)** — `search_knowledge_base` default handler wrapped in try/catch. `isError = true` set on catch; error surfaced as `[KB Search Error]` text.
3. **Fix 3 (IMPORTANT)** — `isError` threaded through `pendingToolResults.push({ toolUseId, toolResultText, isError })` and mapped to `status: r.isError ? 'error' : 'success'` in the user message build.
4. **Fix 4 (IMPORTANT)** — `MAX_TOOL_ITERATIONS` cap warning added after while loop: `console.warn('[harness] /turn: MAX_TOOL_ITERATIONS (N) reached — agentic loop capped')`.

### CC Invocation
```
cat /tmp/brief-3215-c2.md | claude --model sonnet --print --dangerously-skip-permissions
```
Run from: `/home/fredw/projects/fip/fait-v2/agent-harness/`

### node --check Result
`PASS` — no syntax errors

### Grep Confirmations
- `pendingToolResult` (scalar): **GONE** — grep returns clean
- `pendingToolResults` (array): **PRESENT** — lines 1785, 1935, 1968, 1972, 1980
- `pendingToolResults.push(`: line 1935
- `pendingToolResults.map(`: line 1972
- `search_knowledge_base` try/catch: lines 1912–1918
- `isError` threaded: lines 1816, 1917, 1938, 1976
- MAX_TOOL_ITERATIONS log: line 1990

### Commit
`f312ed45` — `fix(fait#3215): multi-tool array + KB try/catch (review cycle 2)`

### Files Changed
- `fait-v2/agent-harness/harness-server.js` — 25 insertions, 14 deletions

### Regressions Check
- `create_document` SSE emission: untouched
- `write_memory` confirmation pattern: untouched
- `messageStopSeen` / metadata handling: untouched
- Token `+=` accumulation: untouched
- All 6 named tool dispatch cases: untouched (only `search_knowledge_base` default had try/catch added)
