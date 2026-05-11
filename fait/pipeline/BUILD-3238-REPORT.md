# Build Report — ADO#3238

## What was built

Three resumption brief bugs fixed across Blazor (`ChatView.razor`) and the agent harness (`harness-server.js`).

---

## Files changed

- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — Fix 1 (per-conversation storage key) + Fix 2 (brief state reset on chat switch)
- `fait-v2/agent-harness/harness-server.js` — Fix 3 (contextual Bedrock summary instead of raw quote)

---

## Changes detail

### Fix 1 — Per-conversation storage key (`ChatView.razor`)

**Root cause:** `ProtectedSessionStorage` used a single key `"resumption_brief_session"` for all conversations. Once any conversation fired the brief in a Fargate session, all others in that session were suppressed.

**Fix:** Storage key now includes both session ID and ConversationId:
```csharp
var briefStorageKey = $"resumption_brief_{currentSessionId}_{ConversationId}";
```
Applied in both `HandleAgentReady()` (read) and `SendResumptionBrief()` (write).

### Fix 2 — Reset brief state on conversation switch (`ChatView.razor`)

**Root cause:** `_briefContent` and `_resumptionBriefSent` were not cleared when `ConversationId` changed, causing stale brief display or suppressed brief for next conversation.

**Fix:** Added two reset lines inside the existing `ConversationId != _lastConversationId` block in `OnParametersSetAsync`:
```csharp
_briefContent = new System.Text.StringBuilder();
_resumptionBriefSent = false;
```

### Fix 3 — Contextual Bedrock summary (`harness-server.js`)

**Root cause:** Handler echoed the last user message verbatim (truncated at 80 chars) — not contextual.

**Fix:** When history is available, the handler now:
1. Takes last 6 messages, formats as `Speaker: content` transcript
2. Calls Bedrock `ConverseStreamCommand` with a mini-prompt requesting one "Last time..." sentence
3. Streams the AI-generated summary back via SSE
4. Falls back to truncated last-user-message if Bedrock call fails
5. Still appends `Memory synced: <date>` line unchanged

---

## Parallelization used

No — single CC session, two files.

## CC sessions run

1 (CC Sonnet). Prompt delivered via `/tmp/brief-3238.md` pipe. Both build checks embedded in brief.

---

## Acceptance criteria verification

- [x] **Fix 1** — Storage key is `resumption_brief_{sessionId}_{conversationId}` — unique per conversation
- [x] **Fix 2** — `_briefContent` and `_resumptionBriefSent` reset when ConversationId changes
- [x] **Fix 3** — Harness calls Bedrock for contextual summary; graceful fallback on failure
- [x] `dotnet build` → 0 errors (46 pre-existing warnings)
- [x] `node --check harness-server.js` → no syntax errors

---

## Known edge cases / things Clint should scrutinize

1. **Fix 1:** `_showBrief` field was referenced in the original brief spec but doesn't exist in ChatView — CC correctly omitted it. The visible brief state is driven by `_briefContent.Length > 0 || _isBriefStreaming` in the template.

2. **Fix 3:** The Bedrock summary call uses `MODEL_ID` (same model as main turns). If the model is unavailable, the fallback echoes the last user message — same behavior as before. No new failure mode introduced.

3. **Fix 3:** `inferenceConfig: { maxTokens: 80 }` is tight. If Claude's one-sentence response exceeds 80 tokens (unlikely but possible for long specifics), it will truncate mid-sentence. Could increase to 120 if needed post-QA.

---

## How to test locally

1. Start harness: `node harness-server.js`
2. Open two different conversations in FAIT
3. Switch between them — confirm brief fires independently for each (not suppressed after first)
4. Confirm brief content reads like "Last time we were..." not a raw user message
5. Navigate away and back — confirm brief fires again (new page load = new component = new `_resumptionBriefSent = false`)

---

## Build Report — ADO#3238 Cycle 2

### What was built
Two surgical fixes in `ChatView.razor` to address brief-streaming state leak on conversation switch and a ConversationId capture-before-await race condition.

### Files changed
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — Two targeted edits:
  1. Added `_isBriefStreaming = false;` to the `ConversationId != _lastConversationId` reset block in `OnParametersSetAsync` — prevents input controls from staying frozen when user switches conversations mid-stream.
  2. Added `var capturedConversationId = ConversationId;` before the `try` in `SendResumptionBrief()` and replaced `ConversationId` in the `finally` storage key with `capturedConversationId` — prevents using a stale/changed `ConversationId` across async continuations.

### Parallelization used
No — single file, two edits in one CC pass.

### CC sessions run
1 CC Sonnet session.

### Acceptance criteria verification
- [x] `_isBriefStreaming = false` present in `ConversationId != _lastConversationId` block (line 533) — verified via grep
- [x] `capturedConversationId` declared before `try` in `SendResumptionBrief()` (line 1425) — verified via sed
- [x] `finally` storage key uses `capturedConversationId` (line 1469) — verified via grep
- [x] `dotnet build` — 0 errors, 46 pre-existing warnings

### Known edge cases / things Clint should scrutinize
- `HandleAgentReady` (the duplicate-guard path) still reads `ConversationId` directly — that path has its own `currentSessionId` capture and runs entirely on the first render so timing is less critical, but worth a glance.
- The `_isBriefStreaming = false` reset is unconditional on switch; if a brief happens to complete successfully just as the switch fires, the state would have already been false anyway — no double-clear risk.

### How to test locally
1. Start a resumption brief streaming on conversation A
2. While streaming, switch to conversation B
3. Input controls on conversation B should be enabled immediately (not frozen by stale `_isBriefStreaming = true`)

### Commit
`1f161cc7` — fix(fait#3238): add _isBriefStreaming reset on chat switch + capture ConversationId before await in SendResumptionBrief (cycle 2)
