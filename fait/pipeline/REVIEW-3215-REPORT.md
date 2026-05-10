# Review Report — ADO#3215
## KB tool results: agentic loop (toolResult blocks)

### Verdict: NEEDS-CHANGES

---

### CC Review Invocation
```bash
cd /home/fredw/projects/fip/fait-v2/agent-harness
cat /tmp/review-3215.md | claude --model sonnet --print --dangerously-skip-permissions
```
CC Sonnet, single pass, clean exit (code 0).

---

### Spec Compliance Check

**Brief:** Inline in dispatch task (ADO#3215)

**§ Codebase Map:**
- `fait-v2/agent-harness/harness-server.js` (+199/-138) — ✅ modified as specified

**§ Out of Scope:**
- ✅ No out-of-scope changes detected

**§ Acceptance Criteria:**
- [x] `sendEvent({type:'text', content:toolResultText})` GONE — ✅ Verified: grep returns empty
- [x] `while (continueLoop && toolIterations < MAX_TOOL_ITERATIONS)` at L1777 — ✅ Confirmed
- [x] Token counting uses `+=` at lines 1939-1940 — ✅ Confirmed
- [x] `node --check` passes — ✅ Confirmed (run during review)
- [x] All 7 tool dispatch cases present — ✅ Confirmed
- [x] `create_document` artifact SSE emission unchanged — ✅ Confirmed (L1870 sendEvent preserved)
- [x] `write_memory` confirmation behavior unchanged — ✅ Confirmed (same pattern as pre-ADO#3215; no prior sendEvent existed; CC false positive dismissed)
- [x] `messageStopSeen` / metadata-after-messageStop pattern preserved — ✅ Confirmed (L1942-1947)

**Spec compliance verdict:** ✅ COMPLIANT — all ACs met. Verdict driven by correctness issues below.

---

### Consistency Audit

**Files Cross-Referenced:**
- `harness-server.js` toolConfig ↔ harness-server.js tool dispatch switch — ✅ 7 tools declared, 7 handlers present
- `toolUseId` field name in `contentBlockStart` ↔ `toolResult` block — ✅ consistent (`toolUseId` camelCase throughout)
- `pendingToolResult.toolUseId` ↔ `assistantContent` toolUse block `toolUseId` — ✅ same variable threaded through

**Undocumented Dependencies Found:**
- `executeKbSearch` called from agentic loop without try/catch — see Critical C2

---

### Critical Issues [2]

#### C1: Multi-Tool-Per-Turn — `pendingToolResult` Scalar Drops All But Last Tool Result

- **File:** `fait-v2/agent-harness/harness-server.js` (lines 1784, 1928-1931, 1960-1971)
- **Category:** correctness / protocol violation
- **Issue:** `pendingToolResult` is declared as a scalar (`null`) at the top of each loop iteration. Inside `contentBlockStop`, it is **assigned** (not pushed to an array). With 7 tools declared and `toolChoice: auto`, Bedrock CAN emit multiple `toolUse` blocks in a single assistant turn. When it does:
  - Each `contentBlockStop` overwrites `pendingToolResult` — only the last tool survives
  - `assistantContent` correctly accumulates ALL toolUse blocks (the assistant message is correct)
  - The `user` message contains only ONE `toolResult` block
  - Bedrock receives a protocol violation: assistant content claims it called tool A and tool B; user only provides a result for tool B
  - Result: Bedrock returns an error, request fails
  
  The developer self-reported this risk. The code does NOT handle it.

- **Evidence:**
  ```js
  // L1784 — scalar, not array
  let pendingToolResult = null;

  // L1928 — inside contentBlockStop, overwrites on every tool call
  pendingToolResult = {
      toolUseId: toolUseAccumulator.toolUseId,
      toolResultText
  };

  // L1960 — only ONE toolResult fed back
  content: [{ toolResult: { toolUseId: pendingToolResult.toolUseId, ... } }]
  ```

- **Impact:** Silent data loss on any turn where Bedrock calls ≥2 tools. Protocol violation causes Bedrock error response. High probability — 7 tools declared, auto tool choice.

- **Fix:**
  ```diff
  - let pendingToolResult = null;
  + const pendingToolResults = [];

  // in contentBlockStop, replace assignment with push:
  - pendingToolResult = { toolUseId: toolUseAccumulator.toolUseId, toolResultText };
  + pendingToolResults.push({ toolUseId: toolUseAccumulator.toolUseId, toolResultText });

  // when building user message, map all results:
  - if (pendingToolResult) {
  + if (pendingToolResults.length > 0) {
      messages.push({ role: 'assistant', content: assistantContent });
      messages.push({
          role: 'user',
  -       content: [{
  -           toolResult: {
  -               toolUseId: pendingToolResult.toolUseId,
  -               content: [{ text: pendingToolResult.toolResultText }],
  -               status: 'success'
  -           }
  -       }]
  +       content: pendingToolResults.map(r => ({
  +           toolResult: {
  +               toolUseId: r.toolUseId,
  +               content: [{ text: r.toolResultText }],
  +               status: 'success'
  +           }
  +       }))
      });
  -   pendingToolResult = null;
  +   pendingToolResults.length = 0;
  ```

---

#### C2: `search_knowledge_base` Unguarded — Throws Escape the Loop

- **File:** `fait-v2/agent-harness/harness-server.js` (lines 1908-1912)
- **Category:** correctness / reliability
- **Issue:** Every other tool handler (6/7) wraps its execution in `try/catch` and stores an error string in `toolResultText`, allowing the agentic loop to continue gracefully. `search_knowledge_base` (the default case) has no protection:
  ```js
  } else {
      // default: search_knowledge_base
      const kbResult = await executeKbSearch(toolInput.query, toolInput.kb_type || 'personal');
      toolResultText = `\n\n[KB Search Results]\n${kbResult}\n\n`;
  }
  ```
  If `executeKbSearch` throws (KB unavailable, Bedrock timeout, empty query, etc.), the exception propagates to the outer catch at ~L1991, which emits an `error` SSE event and calls `res.end()`. The entire request terminates instead of returning a graceful error to the model. Since KB search is the primary use case for this harness, this is a high-probability production failure path.

- **Impact:** Any KB error causes the full request to fail with an `error` SSE event instead of letting Bedrock respond gracefully ("I couldn't search the KB, but here's what I can tell you...").

- **Fix:**
  ```diff
  } else {
  -   // default: search_knowledge_base
  -   const kbResult = await executeKbSearch(toolInput.query, toolInput.kb_type || 'personal');
  -   toolResultText = `\n\n[KB Search Results]\n${kbResult}\n\n`;
  +   // default: search_knowledge_base
  +   try {
  +       const kbResult = await executeKbSearch(toolInput.query, toolInput.kb_type || 'personal');
  +       toolResultText = `\n\n[KB Search Results]\n${kbResult}\n\n`;
  +   } catch (kbErr) {
  +       toolResultText = `\n\n[KB Search Error]\n${kbErr.message}\n\n`;
  +   }
  }
  ```

---

### Important Issues [2]

#### I1: `status: 'success'` Hardcoded for Tool Error Paths

- **File:** `harness-server.js` (line 1968)
- **Issue:** When a tool catches an error and stores a `[Tool Error] message` in `toolResultText`, the toolResult block is still sent with `status: 'success'`. Bedrock supports `status: 'error'` which signals the model to handle the failure differently. Sending `success` with error text may confuse the model into treating errors as valid output.
- **Fix:** Thread an `isError` boolean through `pendingToolResult` (after C1 fix: through the results array). Set `status: r.isError ? 'error' : 'success'` in the final message construction.

#### I2: MAX_TOOL_ITERATIONS Cap Is Silent — No User Notification

- **File:** `harness-server.js` (loop at L1777)
- **Issue:** When `toolIterations` reaches 10, the loop exits silently. The model's last response (which may have expected another loop) could be mid-thought. No SSE event signals this condition. Add a log and optionally a text event:
  ```js
  if (toolIterations >= MAX_TOOL_ITERATIONS && continueLoop) {
      console.warn(`[harness] /turn: MAX_TOOL_ITERATIONS (${MAX_TOOL_ITERATIONS}) reached — loop capped`);
      // Optional: sendEvent({ type: 'text', content: '\n\n[Note: reached tool call limit]\n\n' });
  }
  ```

---

### Dismissed CC Findings (False Positives)

| CC Finding | Disposition |
|-----------|-------------|
| F3: write_memory confirmation regression | **FALSE POSITIVE** — Pre-ADO#3215 code also used `toolResultText` only; no `sendEvent` confirmation existed in HEAD~1. AC#6 "unchanged" is satisfied. |
| F6: MODEL_ID hardcoded | **CONTEXT-DEPENDENT** — Could not verify whether a `security-rules.md` prohibition exists in this repo. Not blocking. If policy exists, file as separate WI. |
| F7: `continueLoop = true` redundant | **NITPICK** — Not blocking, cosmetic. |
| F8: `tokenCount` log drift | **NITPICK** — Not blocking, debugging aid only. |

---

### Issues Found (Summary)

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Critical | harness-server.js | 1784, 1928, 1960 | `pendingToolResult` scalar — multi-tool drops all but last | Replace with array, map all results |
| Critical | harness-server.js | 1908-1912 | `search_knowledge_base` no try/catch — throws escape loop | Wrap in try/catch matching other handlers |
| Important | harness-server.js | 1968 | `status:'success'` hardcoded even for error paths | Thread `isError` flag, conditionally set `'error'` |
| Important | harness-server.js | 1777 loop | MAX_TOOL_ITERATIONS silent exit | Log + optional SSE notification |

---

### What to Fix (Tony)

**Fix 1 — Critical — Multi-tool array (blocking):**
Replace `let pendingToolResult = null` with `const pendingToolResults = []`. In `contentBlockStop`, push to the array instead of assigning. When building the user message, map all entries to `toolResult` blocks. See C1 fix diff above.

**Fix 2 — Critical — KB search try/catch (blocking):**
Wrap lines 1910-1911 in try/catch. Same pattern as every other tool handler. See C2 fix above.

**Fix 3 — Important — error status:**
After the C1 fix, add `isError: false` to the push default and `isError: true` in catch blocks. Use conditionally in the user message builder.

**Fix 4 — Important — iteration cap notification:**
Add a post-loop check after the while statement. Log + optional SSE.

---

### Spec Fidelity

The core agentic loop structure is correct — message accumulation, toolResult format, loop exit on `end_turn`, token accumulation, SSE preservation. The fix correctly eliminates raw `sendEvent` for tool results. The two critical issues are both in correctness/reliability, not in spec compliance. Once fixed, this ships.

---

_Review cycle 1 of 2. NEEDS-CHANGES — fix C1 and C2 before cycle 2._
