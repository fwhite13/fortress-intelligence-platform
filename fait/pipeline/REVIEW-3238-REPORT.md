# Review Report — ADO#3238

### Verdict: NEEDS-CHANGES

---

### CC Review Summary

CC (Sonnet, targeted 5-question brief) read both files and answered all questions. One confirmed important bug found in Fix 2. Fix 1 and Fix 3 are clean. One nitpick on harness missing `system` prompt (intentional and acceptable).

---

### Spec Compliance Check

**§2 Files changed:**
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ modified
- `fait-v2/agent-harness/harness-server.js` — ✅ modified

**§6 Out of Scope:**
- ✅ No out-of-scope files touched

**§7 Acceptance Criteria:**
- [x] Fix 1 — Storage key is `resumption_brief_{sessionId}_{conversationId}` ✅ Verified
- [x] Fix 3 — Bedrock contextual summary with fallback ✅ Verified
- [ ] Fix 2 — State reset on chat switch: ❌ Incomplete — see Critical #1 below

**Spec compliance verdict:** ❌ NON-COMPLIANT on Fix 2 (blocks PASS)

---

### Consistency Audit

**Files cross-referenced:**
- `ChatView.razor` storage key in `SendResumptionBrief()` ↔ `HandleAgentReady()` — ✅ Both use `$"resumption_brief_{sessionId}_{ConversationId}"`, consistent
- `_currentHarnessSessionId` assignment (set in `HandleAgentReady`) ↔ used in `SendResumptionBrief` `finally` — ✅ `_currentHarnessSessionId` is always set before `SendResumptionBrief` is called; the null-session path sets `_wasColdStart = true` but NOT `_currentHarnessSessionId`, and the `finally` guard `if (!string.IsNullOrEmpty(_currentHarnessSessionId))` handles that correctly
- `harness-server.js` `ConverseStreamCommand` stream iteration pattern ↔ main agentic loop — ✅ Both use `for await (const chunk of resp.stream)` and `chunk.contentBlockDelta?.delta?.text`

---

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Important | `ChatView.razor` | 531 | `_isBriefStreaming` not reset on conversation switch — see C1 | Add `_isBriefStreaming = false;` to the reset block |
| Nitpick | `harness-server.js` | 1527 | Summary `ConverseStreamCommand` has no `system` prompt — minor inconsistency vs main loop | Intentional and acceptable; no action needed |

---

### Critical Issues — 0

None.

---

### Important Issues — 1

#### I1: `_isBriefStreaming` Not Reset on Conversation Switch

**File:** `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` (lines 530–532)

**Category:** Correctness / UX

**Issue:** When the user switches conversations while a resumption brief is mid-stream, `_isBriefStreaming` is NOT reset in the `ConversationId != _lastConversationId` block. The new conversation's input (send button, textarea, attachments, dictation, task mode) remains disabled until the old stream's `finally` block fires.

Additionally — more subtle: the old streaming task holds a reference to `this._briefContent` (the field). Fix 2 reassigns `_briefContent = new System.Text.StringBuilder()`. After the reassignment, the old streaming task continues appending to the **new** StringBuilder (since C# `Append` works on the field reference captured at the time of each `.Append` call — and `_briefContent` now points to the new instance). This means residual text from the old conversation's brief can bleed into the new conversation's brief area.

Furthermore: when the old stream's `finally` fires, it writes `ProtectedSessionStorage` using `this.ConversationId` at time of execution — which is now the **new** conversation's ID. This marks the new conversation as already briefed before it has received a brief.

**Evidence:**
```csharp
// Reset block (lines 530-532) — _isBriefStreaming is NOT reset
_briefContent = new System.Text.StringBuilder();
_resumptionBriefSent = false;
// _isBriefStreaming = false; ← MISSING
```

The old streaming task's finally:
```csharp
// ~line 1467 — ConversationId is this.ConversationId at execution time
var briefStorageKey = $"resumption_brief_{_currentHarnessSessionId}_{ConversationId}";
await SessionStorage.SetAsync(briefStorageKey, _currentHarnessSessionId); // ← writes NEW convo's key!
```

**Impact:** 
1. Input freeze on new conversation for ~1-3 seconds (transient, low severity on its own)
2. New conversation may be marked as already briefed (brief never fires even after fresh page load)
3. Old brief content may flash briefly in new conversation view

**Fix:**
```diff
 if (ConversationId != _lastConversationId)
 {
     Logger.LogInformation("[ChatView] ConversationId changed...");
     _lastConversationId = ConversationId;
     _needsScroll = true;
     // Reset resumption brief state for new conversation
     _briefContent = new System.Text.StringBuilder();
     _resumptionBriefSent = false;
+    _isBriefStreaming = false;
 }
```

For the storage key race condition, the cleanest fix is to capture the conversation ID in the brief send:
```diff
 // In SendResumptionBrief, capture at start of method:
+var capturedConversationId = ConversationId;
 // ...
 // In finally:
-var briefStorageKey = $"resumption_brief_{_currentHarnessSessionId}_{ConversationId}";
+var briefStorageKey = $"resumption_brief_{_currentHarnessSessionId}_{capturedConversationId}";
```

---

### Fix 1 — Storage Key Verification ✅

Key is built as `$"resumption_brief_{sessionId}_{ConversationId}"` — used consistently in both `SendResumptionBrief` (`_currentHarnessSessionId`) and `HandleAgentReady` (`currentSessionId`), and `_currentHarnessSessionId = currentSessionId` is set before `SendResumptionBrief` fires. If `currentSessionId` is null, `_currentHarnessSessionId` is never set and the `finally` storage guard (`if (!string.IsNullOrEmpty(_currentHarnessSessionId))`) correctly skips the write. No key collision risk — ConversationId is a Guid.

---

### Fix 3 — Harness Bedrock Summary ✅

- Stream iteration: `for await (const chunk of summaryResp.stream)` ↔ matches main agentic loop pattern (`response.stream`) ✅
- SSE `done` event: always fires — normal path calls it explicitly; exception path emits `error` event; `res.end()` is always called ✅
- Fallback: catch block emits `Last time: {truncated}` via `sendEvent` — `sendEvent` is in scope (outer `/turn` handler closure) ✅
- `maxTokens: 80`: One English sentence = 15-30 tokens output. 80 is fine. Even with verbose model preamble, 80 token output won't truncate a one-sentence response. Build report notes it could be bumped to 120 post-QA if needed — fair assessment, not a block.
- No hardcoded colors in `resumption-brief-*` CSS — all CSS variables with fallbacks ✅

---

### What to Fix

**Tony** — one change needed:

**1. In `ChatView.razor`, `OnParametersSetAsync` conversation-switch block (~line 531):**

Add `_isBriefStreaming = false;` to the reset block:

```csharp
// Reset resumption brief state for new conversation
_briefContent = new System.Text.StringBuilder();
_resumptionBriefSent = false;
_isBriefStreaming = false;  // ← ADD THIS
```

**2. In `SendResumptionBrief`, capture `ConversationId` at method entry:**

```csharp
private async Task SendResumptionBrief()
{
    var capturedConversationId = ConversationId;  // ← ADD THIS
    try
    {
        // ... existing code ...
    }
    finally
    {
        if (!string.IsNullOrEmpty(_currentHarnessSessionId))
        {
            try
            {
                var briefStorageKey = $"resumption_brief_{_currentHarnessSessionId}_{capturedConversationId}";  // ← use captured
```

These two changes together prevent input lockout and the false "already briefed" storage write on conversation switch.

---

_Clint Barton — code review complete_

---

## Review Report — ADO#3238 — Cycle 2 Sign-off

### Verdict: PASS

---

### CC Review Summary

CC (Sonnet, targeted cycle 2 brief) read `ChatView.razor` and verified both fixes from cycle 1. Both are correctly implemented. No regressions introduced.

---

### Fix Verification

**Fix 1 — `_isBriefStreaming = false` in reset block**

✅ **CORRECT**

```csharp
if (ConversationId != _lastConversationId)
{
    // ...
    _briefContent = new System.Text.StringBuilder();
    _resumptionBriefSent = false;   // line 532
    _isBriefStreaming = false;      // line 533 ← present, same block
}
```

Both `_resumptionBriefSent` and `_isBriefStreaming` reset together in the `ConversationId != _lastConversationId` block. Fix is in the right place.

---

**Fix 2 — `capturedConversationId` in `SendResumptionBrief()`**

✅ **CORRECT**

```csharp
var capturedConversationId = ConversationId; // first statement, before any await
// ...
// first await on line 1445 (AgentRuntime.SendTurnAsync)
// ...
finally
{
    var briefStorageKey = $"resumption_brief_{_currentHarnessSessionId}_{capturedConversationId}"; // ← captured value used
}
```

Capture is the first statement in the method body, before any `await`. The `finally` block uses `capturedConversationId` exclusively. No remaining bare `ConversationId` references exist in `SendResumptionBrief` after the capture point.

---

### Advisory (pre-existing, not a cycle 2 block)

`HandleAgentReady` (~line 1494) has the same race pattern that Fix 2 addressed — `ConversationId` is used uncaptured after an `await GetSessionAsync(...)` call when building a storage key. This was present before this commit, not introduced by cycle 2. Recommended follow-up: capture `ConversationId` before the `GetSessionAsync` await in `HandleAgentReady` to close that same window.

**This does not block PASS for cycle 2.**

---

### Quick Scan

No new issues introduced by commit `1f161cc7`. Change is surgical and correct.

---

_Clint Barton — cycle 2 sign-off complete_

---

## Review Report — ADO#3238 — Round 2 (Commit `9320e00a`)

**Date:** 2026-05-11
**Reviewer:** Hawkeye (Clint Barton)

### Verdict: ✅ PASS

---

### CC Review Summary

CC (Sonnet, targeted 3-question adversarial brief) read `ChatView.razor` and traced the fire-and-forget refactor in detail. All three questions resolved clean or low-risk. No regressions from the move of state-init to the caller.

---

### Spec Compliance Check

**What changed:**
- `_isBriefStreaming = true` + `_briefContent = new StringBuilder()` + `StateHasChanged()` moved from `SendResumptionBrief()` try block → `OnAfterRenderAsync` cold-start trigger block
- `await SendResumptionBrief()` → `_ = SendResumptionBrief()` (fire-and-forget)
- 3 now-redundant init lines removed from `SendResumptionBrief()`

**Files changed:**
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — ✅ confirmed as the only changed file

**Acceptance criteria:**
- [x] `_isBriefStreaming = true` set before fire-and-forget fires — ✅ line 633
- [x] `StateHasChanged()` called before `_ = SendResumptionBrief()` so UI renders brief card immediately — ✅ line 635
- [x] 3 state-init lines removed from `SendResumptionBrief()` try block — ✅ CC confirmed absent
- [x] `dotnet build` — 0 errors per build report

**Spec compliance verdict:** ✅ COMPLIANT

---

### Q1: Fire-and-forget exception handling

`capturedConversationId = ConversationId` is the only statement before `try` in `SendResumptionBrief()`. It's a simple component parameter read — cannot throw. The `catch (Exception ex)` covers the entire try body including `SendTurnAsync` and the `await foreach`. No code path exists that can escape the catch under normal conditions.

**Verdict: ✅ Clean.** No harmful propagation path from fire-and-forget.

---

### Q2: Duplicate state initialization

CC confirmed: after the refactor, `SendResumptionBrief()` try block begins directly with building `recentHistory` and `briefRequest`. Zero remaining calls to `_isBriefStreaming = true`, `_briefContent = new StringBuilder()`, or `StateHasChanged()` inside the try. The 3 lines were cleanly moved and removed.

**Verdict: ✅ Clean.** No duplicate initialization.

---

### Q3: StateHasChanged() in fire-and-forget — Advisory

`StateHasChanged()` is called **bare** (not wrapped in `InvokeAsync`) at two locations:
- Inside `await foreach` loop body (line ~1414) — called on each SSE text event
- In `finally` block (line ~1441)

The fire-and-forget task is dispatched from `OnAfterRenderAsync`, which captures Blazor's circuit `SynchronizationContext`. All `await` continuations should resume on that context — including continuations from `await foreach` over `AgentRuntime.SendTurnAsync`.

**Risk:** If `SendTurnAsync` internally switches context via `ConfigureAwait(false)` or `Task.Run`, bare `StateHasChanged()` calls would execute off the circuit sync context, racing with other lifecycle methods. Additionally, `_briefContent.Append()` inside the loop would also be off-thread — `StringBuilder` is not thread-safe.

**Probability in practice:** Low. Blazor's sync context is typically preserved through await chains, and the SSE stream delivery should stay on the circuit thread.

**This does NOT block PASS.** It's a latent threading risk that exists in similar patterns throughout the codebase. Recommended future hardening: wrap `_briefContent.Append(...)` + `StateHasChanged()` in `await InvokeAsync(() => { _briefContent.Append(content); StateHasChanged(); })`.

---

### Findings Summary

| Severity | Issue | Status |
|----------|-------|--------|
| Advisory | Bare `StateHasChanged()` in fire-and-forget — latent threading risk if sync context switches | Non-blocking; future hardening |
| — | Q1: exception handling | ✅ Clean |
| — | Q2: no duplicate state init | ✅ Clean |

No blocking issues. No regressions.

---

_Hawkeye (Clint Barton) — Round 2 sign-off. Ready to ship._
