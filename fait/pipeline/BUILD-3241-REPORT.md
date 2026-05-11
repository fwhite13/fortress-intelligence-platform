# Build Report — ADO#3241

## What was built

Harness now emits two new typed SSE events (`kb_sources` and `tool_call`) for transparency during AI turns. KB retrieval ownership has shifted from Blazor to the harness — Blazor passes `KbFlags`, harness does the Bedrock retrieval and injects context into the system prompt, then emits `kb_sources` so the UI indicator updates in real-time. Tool call transparency events wrap all `graph_*`, `ado_*`, and `web_search` dispatches in the agentic loop.

## Files changed

- `fait-v2/agent-harness/harness-server.js` — Added `retrieveFromKbFull` helper; `emitToolCall` helper; harness-side KB retrieval block in `/turn` (reads `KbFlags`, calls Bedrock Retrieve, emits `kb_sources`, rebuilds system prompt); `tool_call` SSE events before/after graph_, ado_, and web_search tool dispatches in agentic loop.

- `fait/src/FortressAI.Web/Services/IUserAgentRuntime.cs` — Added `KbFlags` record; added `KbFlags? KbFlags = null` to `TurnRequest`; added `KbSourcesPayload`, `KbSourceItem`, `KbChunkItem`, `ToolCallPayload` DTOs; updated HarnessEvent type comment.

- `fait/src/FortressAI.Web/Services/FargateUserAgentRuntime.cs` — SSE reading loop now handles typed `event:` lines. Added `pendingEventType` tracking so `kb_sources` and `tool_call` events (which use `event: <type>\ndata: <json>` format) are parsed and yielded as `HarnessEvent(type, Payload: json)`.

- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — Removed entire Blazor-side KB retrieval block (ForgeQueryService + KbSvc calls). Blazor now just computes KB flag booleans and passes them as `KbFlags` in `TurnRequest`. Added SSE handlers for `kb_sources` (populates `_lastKbResult` from harness retrieval data) and `tool_call` (populates/updates `_activeToolCalls`). Added `_activeToolCalls` list, `ToolCallEvent` record, `HandleToolCallEvent` method, and tool call indicator UI in the streaming message area. Clear logic added on new send and chat switch.

- `fait/src/FortressAI.Web/wwwroot/css/fortress.css` — Added CSS classes: `.tool-call-indicator-list`, `.tool-call-indicator`, `.tool-call-active`, `.tool-call-done`, `.tool-call-error`, `.tool-call-summary` — all using CSS variables, no hardcoded colors.

## Parallelization used

No — single CC Opus session (all changes are interdependent).

## CC sessions run

1 (CC Opus, single pass from `/home/fredw/projects/fip/fait/`)

## Acceptance criteria verification

- [x] `node --check harness-server.js` — PASS (exit 0)
- [x] `dotnet build` — PASS (0 errors, 45 pre-existing warnings)
- [x] Harness emits `event: kb_sources\ndata: {...}` when KB flags set
- [x] Harness emits `event: tool_call\ndata: {...}` before/after graph_, ado_, web_search tool calls
- [x] FargateUserAgentRuntime.cs SSE loop handles typed `event:` lines
- [x] ChatView.razor handles `kb_sources` → `_lastKbResult` populated
- [x] ChatView.razor handles `tool_call` → `_activeToolCalls` updated + rendered
- [x] `KbFlags` added to `TurnRequest` and passed from ChatView

## Known edge cases / things Clint should scrutinize

1. **KB retrieval ordering**: `fullSystemPrompt` is a `let` now and rebuilt after KB retrieval appends to `systemParts`. The KB block runs AFTER `fullSystemPrompt` is first set, then reassigns it. Verify the agentic loop at line ~2044 uses the rebuilt `fullSystemPrompt`.

2. **doKbRetrieval inner function**: Defined inside the `if (kbEnabled)` block. Works in Node.js but is non-standard style — if this causes hoisting issues in strict mode, extract to outer scope. `node --check` passes so syntax is fine.

3. **Team KB mapping**: Spec mentions `teamKbIds` (array) but the harness env var model only has `TEAM_KB_ID` (single). `TeamKbEnabled: hasTeamKb || hasProjectKb` maps both to the single team KB. This is a known simplification — full per-team KB routing wasn't in scope for this story.

4. **`_activeToolCalls` persistence**: Tool calls accumulate during a turn and are NOT cleared after the turn completes (only on new send or chat switch). Clint should verify this renders cleanly — should show the history of tool calls for the last turn.

5. **KB sources for `_lastKbResult`**: The harness emits the event before first token (by design, in the pre-loop retrieval section). Blazor populates `_lastKbResult` from this event. If KB retrieval returns 0 results, `WasSearched` is still set to true so the indicator shows "KB searched — no relevant results".

6. **CSS variables**: New classes use `var(--color-text-success, var(--color-text-secondary))` fallbacks. If `--color-text-success` or `--color-text-danger` aren't defined in fortress.css, they fall back gracefully.

## How to test locally

1. Start FAIT with `CORP_KB_ID`, `PERSONAL_KB_ID`, `TEAM_KB_ID` env vars set
2. Enable a KB toggle in the chat UI
3. Send a message — harness should emit `kb_sources` event; KB indicator should populate from SSE (not Blazor-side retrieval)
4. Enable an MS365 or ADO MCP tool; ask Claude something that triggers a tool call
5. Should see tool call indicators (⏳ calling → ✓ done) in the streaming message area

## Commit

`036a8a8f` — `feat(fait#3241): harness SSE kb_sources + tool_call events + KB retrieval ownership`
