# Build Report — ADO#3144

## What was built

Wired `ChatView.HandleSend` to route all inference through `IUserAgentRuntime.SendTurnAsync` instead of directly calling Bedrock. Both the tool-enabled agentic loop (`BedrockSvc.StreamChatWithToolsAsync`) and the no-tools path (`BedrockSvc.StreamChatAsync`) were replaced by a single `SendTurnAsync` call.

## Files changed

- `src/FortressAI.Web/Components/Chat/ChatView.razor` — Replaced `if (availableTools.Any()) { ... } else { ... }` block (old lines 759–1013, 255 lines) with 32-line `SendTurnAsync` streaming block. Net change: **−244 lines, +21 lines**.

## Lines replaced

- **Old lines 759–1013** (255 lines): entire `if (availableTools.Any())` ... `else` ... `BedrockSvc.StreamChatAsync` block
- **New lines 759–790** (32 lines): `TurnRequest` construction + `await foreach` over `AgentRuntime.SendTurnAsync`

## Parallelization used

No — single-file change, sequential CC session.

## CC sessions run

1 CC Sonnet session. No notable decisions; brief was fully specified.

## Acceptance criteria verification

- [x] **Both Bedrock paths removed from HandleSend** — `StreamChatWithToolsAsync` and `StreamChatAsync` are no longer called in `HandleSend`
- [x] **Single `SendTurnAsync` call replaces both** — `AgentRuntime.SendTurnAsync` is the sole inference path
- [x] **`HarnessEvent.Type` values used correctly** — `"text"` | `"done"` | `"error"` | `"log"` (swallowed silently)
- [x] **`fullResponse` StringBuilder still populated** — `fullResponse.Append(evt.Content ?? "")` on every `"text"` event; `fullResponse.ToString()` passed to `ChatSvc.AddMessageAsync` downstream (line 792)
- [x] **`inputTokens`/`outputTokens` captured** — read from `evt.InputTokens ?? 0` / `evt.OutputTokens ?? 0` on `"done"` event
- [x] **Build: 0 errors** — `dotnet build` passed, 32 pre-existing warnings only
- [x] **Commit message correct** — `feat(fait#3144): route ChatView.HandleSend through IUserAgentRuntime.SendTurnAsync — harness SSE streaming`

## What was NOT touched (verified)

- `BedrockSvc` injection — still registered in DI, still injected in ChatView; just no longer called in `HandleSend`
- MCP tool injection + `availableTools` loading block — still present (tools are loaded but not used in this path; harness handles tool execution)
- `streamingMessage` initialization
- Message persistence after streaming (DB save via `ChatSvc.AddMessageAsync`)
- `isStreaming` flag management
- Attachment handling
- `chatHistory` building via `PrepareMessagesWithSlidingWindowAsync`
- `_toolCallInProgress`, `_currentToolName`, `_activeToolCalls` field declarations (used elsewhere in the component)

## Known edge cases / things Clint should scrutinize

1. **Tool execution now in harness, not ChatView** — The old agentic loop in ChatView executed tool calls directly via `McpToolSvc.ExecuteToolAsync`. With `SendTurnAsync`, tool execution happens inside the Fargate harness. The ChatView no longer participates in tool call orchestration. This is intentional per the spec.

2. **`availableTools` still loaded but unused in HandleSend** — The MCP tool loading block still runs on every `HandleSend` call (wasted work). Left intact per spec ("don't touch MCP service injections"). Future cleanup opportunity.

3. **No `SessionId` passed in `TurnRequest`** — `TurnRequest.SessionId` is omitted (null). The harness will use/create its own session. Per spec this is acceptable ("the harness has its own system prompt from S3").

4. **`_toolCallInProgress` UI state no longer updated** — The old streaming loop set `_toolCallInProgress = true` when tool calls started. The new path never sets it. The tool indicator UI will not fire. This is expected for this WI; harness-side tool UX is out of scope.

## Commit

`b98f10a9` — `feat(fait#3144): route ChatView.HandleSend through IUserAgentRuntime.SendTurnAsync — harness SSE streaming`

## How to test locally

```bash
cd /home/fredw/projects/fip/fait
dotnet run --project src/FortressAI.Web/FortressAI.Web.csproj
# Navigate to chat, send a message — verify streaming response appears
# Check logs for harness SSE events
```
