# CC Task: ADO#4827 — Resumption Brief Fires on New Conversation

## Working directory
`/home/fredw/projects/fip/fait/`

## Files to modify
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — primary fix
- `agent-harness/harness-server.js` — guard enhancement

---

## Root Cause

In `HandleAgentReady()`, `_wasColdStart` is set to `true` unconditionally:
```csharp
if (!string.IsNullOrEmpty(currentSessionId))
{
    ...
    _currentHarnessSessionId = currentSessionId;
    _wasColdStart = true;  // BUG: set even for brand new empty conversation
}
else
{
    _wasColdStart = true;  // BUG: ditto
}
```

For a brand-new conversation with no message history, the brief should not fire.

The harness guards with `!hasHistory && !memoryTimestamp` — but if the user has a MEMORY.md (which most users do), that guard passes and the brief fires even with empty history, generating a nonsensical "resumption" message.

---

## Fix 1: ChatView.razor — HandleAgentReady

In `HandleAgentReady()`, find both places where `_wasColdStart = true` is set (there are 3-4 occurrences inside the method, including in the catch block). 

**Add the guard:** Before setting `_wasColdStart = true`, check `messages.Any(m => !m.IsResumptionBrief)`. If there are no prior real messages, this is a brand-new conversation — do NOT set `_wasColdStart`.

Replace the block (approximately lines 2074-2085 of current file):
```csharp
                _currentHarnessSessionId = currentSessionId;
                _wasColdStart = true;
            }
            else
            {
                _wasColdStart = true;
            }
```

With:
```csharp
                _currentHarnessSessionId = currentSessionId;
                // ADO#4827 — only set cold-start if there is prior conversation history to resume
                _wasColdStart = messages.Any(m => !m.IsResumptionBrief);
                Logger.LogInformation("[ChatView] HandleAgentReady: setting _wasColdStart={WasColdStart} (messageCount={Count})", _wasColdStart, messages.Count(m => !m.IsResumptionBrief));
            }
            else
            {
                // ADO#4827 — no session ID yet, but still only cold-start if there's history
                _wasColdStart = messages.Any(m => !m.IsResumptionBrief);
                Logger.LogInformation("[ChatView] HandleAgentReady: no sessionId, _wasColdStart={WasColdStart} (messageCount={Count})", _wasColdStart, messages.Count(m => !m.IsResumptionBrief));
            }
```

Also fix the `catch` block fallback at the bottom of `HandleAgentReady`:
```csharp
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[ChatView] Resumption brief session guard failed — proceeding with cold start");
            _wasColdStart = true;  // <-- this line
        }
```

Change to:
```csharp
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[ChatView] Resumption brief session guard failed — proceeding with cold start");
            // ADO#4827 — even on error, don't set cold start for empty conversations
            _wasColdStart = messages.Any(m => !m.IsResumptionBrief);
        }
```

## Fix 2: ChatView.razor — Cold-start trigger guard

In `OnAfterRenderAsync`, find the cold-start trigger block (around line 783):
```csharp
if (_wasColdStart && _agentReady && !_resumptionBriefSent && ConversationId.HasValue && ConversationId.Value != Guid.Empty)
```

Add a messages check as an extra guard:
```csharp
if (_wasColdStart && _agentReady && !_resumptionBriefSent && ConversationId.HasValue && ConversationId.Value != Guid.Empty
    && messages.Any(m => !m.IsResumptionBrief))
```

This is a belt-and-suspenders guard — the primary fix is in HandleAgentReady, but this prevents any edge case.

## Fix 3: harness-server.js — Strengthen guard

In harness-server.js at the resumption brief block (around line 3043):
```javascript
if (!hasHistory && !memoryTimestamp) {
    console.log(`[harness] resumption brief: no history and no MEMORY.md for userId=${userId} — skipping brief`);
```

Change the guard to ALSO skip if there's no history even when MEMORY.md exists:
```javascript
if (!hasHistory) {
    console.log(`[harness] resumption brief: no history for userId=${userId} — skipping brief (memoryTimestamp=${memoryTimestamp})`);
```

This ensures the harness never sends a resumption brief for a new conversation, regardless of whether MEMORY.md exists. The memory timestamp is still logged for observability but is no longer a condition for sending the brief.

---

## Final steps
1. Verify the build compiles: `cd /home/fredw/projects/fip/fait/src/FortressAI.Web && dotnet build --no-restore 2>&1 | tail -10`
2. Fix any compilation errors
3. Commit: `cd /home/fredw/projects/fip && git add -A && git commit -m "ADO#4827: resumption brief suppressed for new conversations — guard on messages.Any() in HandleAgentReady and OnAfterRenderAsync; harness guard requires history regardless of MEMORY.md"`
