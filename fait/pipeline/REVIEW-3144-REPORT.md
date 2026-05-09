# Review Report — ADO#3144

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

**Spec reference:** `memory/projects/fait-v1-evolution-spec.md` — Feature 1.5, Story 1.5-A

**§ Story 1.5-A Intent:**
> Replace Bedrock send paths in ChatView with `IUserAgentRuntime.SendTurnAsync()`. No change to UI appearance. User should not notice anything different.

**Codebase Map:**
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ modified as specified

**Out of Scope:**
- ✅ No out-of-scope changes detected — single file, exactly the right scope

**Acceptance Criteria:**
- [x] Both Bedrock paths removed from HandleSend: ✅ Verified
- [x] Single `SendTurnAsync` call replaces both: ✅ Verified
- [x] `HarnessEvent.Type` values used correctly: ✅ Verified — `"text"` / `"done"` / `"error"` / `"log"` (swallowed)
- [x] `fullResponse` StringBuilder still populated: ✅ Verified
- [x] `inputTokens`/`outputTokens` captured: ✅ Verified
- [x] Build: 0 errors: ✅ Verified — 32 warnings, 0 errors

**Spec compliance verdict:** ⚠️ PARTIALLY COMPLIANT — Story 1.5-A says "user should not notice anything different." Dropping `effectiveSystemPrompt` violates this (see Critical #1).

---

### Consistency Audit

**Files Cross-Referenced:**
- `ChatView.razor` ↔ `IUserAgentRuntime.cs` (`TurnRequest`) — ✅ Call signature correct; `TurnRequest.SystemPrompt` parameter exists but ❌ not passed (see Critical #1)
- `ChatView.razor` ↔ `FargateUserAgentRuntime.cs` — ✅ POST body serialized and sent correctly
- `FargateUserAgentRuntime.cs` ↔ `harness-server.js` — ✅ Harness reads `rawBody.SystemPrompt ?? rawBody.systemPrompt` at line 1038 and injects it into both CC brief context (task mode, line 1128) and Bedrock system prompt (chat mode, line 1260)

**Undocumented dependencies found:**
- `harness-server.js` line 1038–1264: harness accepts and uses `SystemPrompt` from the request body. The field is wired end-to-end. Tony knew this existed (`TurnRequest.SystemPrompt` is in the interface) but didn't pass it.

---

### Critical Issues [1]

#### C1: `effectiveSystemPrompt` not passed to `TurnRequest` — KB context, project prompts, and attachments silently dropped

- **File:** `src/FortressAI.Web/Components/Chat/ChatView.razor` (lines 759–765)
- **Category:** correctness / spec fidelity
- **Issue:** `TurnRequest` is constructed without `SystemPrompt: effectiveSystemPrompt`. The harness accepts and uses `SystemPrompt` from the POST body (harness-server.js line 1038, injected at lines 1128 and 1260). By omitting it, all per-turn context assembled in `effectiveSystemPrompt` is silently dropped:
  - Organization KB context (Bedrock KB retrieval results)
  - Personal/team/project KB context (FORGE multi-query results)
  - Project system prompt (`BuildSystemPromptFromProject`)
  - Personality/assistant config prefix (`ConfigSvc.GetPersonalitySystemPrompt`)
  - Artifact prompt (`GetArtifactSystemPrompt`)
  - Attachment file content
  
  The harness still has SOUL.md/USER.md/MEMORY.md from S3, but the per-turn dynamic context that makes responses contextually aware is gone. This directly violates Story 1.5-A: "user should not notice anything different."

- **Evidence:**
  ```csharp
  // ChatView.razor line 759–765 — SystemPrompt MISSING
  var turnRequest = new TurnRequest(
      UserId: Session.UserId.ToString(),
      Message: text.Trim(),
      History: chatHistory.Select(m => new ChatHistoryEntry(m.Role, m.Content)).ToList(),
      TaskMode: false
      // SystemPrompt: effectiveSystemPrompt  <-- NOT HERE
  );
  ```
  
  ```js
  // harness-server.js line 1038 — harness DOES read it
  const systemPrompt = rawBody.SystemPrompt ?? rawBody.systemPrompt;
  // ...
  if (systemPrompt) contextParts.push(systemPrompt);  // line 1128 (task mode)
  if (systemPrompt) systemParts.push(systemPrompt);   // line 1260 (chat mode)
  ```

- **Impact:** All KB context dropped on every turn. Project instructions dropped. Attachment content dropped. Users with KB enabled will get responses with no KB grounding. Users with attachments will get responses that ignore the attachment content.

- **Fix:**
  ```diff
  var turnRequest = new TurnRequest(
      UserId: Session.UserId.ToString(),
      Message: text.Trim(),
      History: chatHistory.Select(m => new ChatHistoryEntry(m.Role, m.Content)).ToList(),
  -   TaskMode: false
  +   TaskMode: false,
  +   SystemPrompt: string.IsNullOrEmpty(effectiveSystemPrompt) ? null : effectiveSystemPrompt
  );
  ```

---

### Important Issues [0]

None.

---

### Nitpicks [2]

#### N1: New `CS0649` warning — `_currentToolName` never assigned
- **File:** `ChatView.razor` line 286
- **Issue:** Pre-commit the field was assigned inside the streaming loop. That loop is now gone. Build went from 31 → 32 warnings. The field is still read in the UI template (`@(_currentToolName ?? "Calling tool")`). Field is now dead/orphaned — it will always be null.
- **Note:** This is expected for this WI per the build report. The tool call UI indicator (`_toolCallInProgress`) is declared out of scope. But it's worth flagging: the UI renders a "🔍 Calling tool..." badge that can never fire. Not a blocker — clean it up when `_toolCallInProgress` gets wired to harness events.

#### N2: `availableTools` loading block still runs (wasted work)
- **File:** `ChatView.razor` lines 741–753
- **Issue:** `McpToolSvc.GetConversationToolsAsync` is called on every `HandleSend` but the result is never used. Per build report this was intentionally left in ("don't touch MCP service injections"). Not a bug. Future cleanup.

---

### Checklist Results

| # | Item | Result |
|---|------|--------|
| 1 | `SendTurnAsync` call signature | ✅ `(Session.UserId.ToString(), turnRequest, streamingCts!.Token)` |
| 2 | `TurnRequest` construction — UserId, Message, History, TaskMode:false | ✅ All present |
| 3 | `"text"` → appends `evt.Content ?? ""` to fullResponse | ✅ |
| 4 | `"done"` → captures InputTokens/OutputTokens | ✅ |
| 5 | `"error"` → appends to fullResponse + StateHasChanged | ✅ |
| 6 | `"log"` → swallowed silently | ✅ (comment confirms intent) |
| 7 | `fullResponse.ToString()` passed to DB persistence downstream | ✅ Line 792 `ChatSvc.AddMessageAsync` |
| 8 | `isStreaming = false` in finally | ✅ |
| 9 | `streamingMessage` reset in finally | ✅ (reset to new empty ChatMessage, not null — equivalent) |
| 10 | `StateHasChanged()` after streaming | ✅ In finally block |
| 11 | User message creation (`ChatSvc.AddMessageAsync`) untouched | ✅ Line ~487 |
| 12 | Conversation lazy creation block untouched | ✅ Lines ~473–481 |
| 13 | Message DB persistence after streaming untouched | ✅ Line 792 |
| 14 | `BedrockSvc` still injected | ✅ `@inject BedrockService BedrockSvc` at line 7 |
| 15 | `streamingCts!.Token` passed correctly | ✅ |
| 16 | `_toolCallInProgress`/`_currentToolName` NOT set in new block | ✅ Expected |
| 17 | No new @inject or field declarations added | ✅ |
| 18 | `availableTools` loading block still present | ✅ (wasted work, but per spec) |
| 19 | `StreamChatWithToolsAsync` NOT called in HandleSend | ✅ Removed |
| 20 | `StreamChatAsync` NOT called in HandleSend | ✅ Removed |
| 21 | `AgentRuntime.SendTurnAsync` IS called | ✅ |
| 22 | TurnRequest field types correct vs. record definition | ✅ All fields match |
| 23 | `HarnessEvent` type/fields correct | ✅ Type, Content, ErrorMessage, InputTokens, OutputTokens all valid |
| **C1** | **`effectiveSystemPrompt` passed as `SystemPrompt`** | **❌ MISSING** |

---

### What to Fix

**One change required. Tony should be able to do this in 2 minutes.**

In `src/FortressAI.Web/Components/Chat/ChatView.razor`, at the `TurnRequest` construction (line ~759):

```diff
 var turnRequest = new TurnRequest(
     UserId: Session.UserId.ToString(),
     Message: text.Trim(),
     History: chatHistory.Select(m => new ChatHistoryEntry(m.Role, m.Content)).ToList(),
-    TaskMode: false
+    TaskMode: false,
+    SystemPrompt: string.IsNullOrEmpty(effectiveSystemPrompt) ? null : effectiveSystemPrompt
 );
```

`effectiveSystemPrompt` is already in scope at this point (assembled earlier in `HandleSend`). The harness already handles it correctly on the other end. This is a one-liner.

After the fix: rebuild to confirm 0 errors, then re-submit for review.

---

_Reviewed by Hawkeye (Clint Barton) — 2026-05-09_
