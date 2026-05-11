# Review Report: ADO#3244 — Task Progress Timeline
**Review cycle**: 1 of 2
**Reviewer**: Clint (pipeline-review agent)
**Date**: 2026-05-11
**Commit reviewed**: `eac6da83`
**Verdict**: ⚠️ NEEDS-CHANGES

## CC Invocation
```
cat /tmp/review-brief-3244.md | claude --model sonnet --print --dangerously-skip-permissions
```
Files read directly during this review:
- `/home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js` (lines 1610–1810, mode_switch/NDJSON section)
- `/home/fredw/projects/fip/fait/src/FortressAI.Web/Hubs/CCProgressHub.cs`
- `/home/fredw/projects/fip/fait/src/FortressAI.Web/Services/IUserAgentRuntime.cs`
- `/home/fredw/projects/fip/fait/src/FortressAI.Web/Components/Chat/ChatView.razor` (full)
- `/home/fredw/projects/fip/fait/src/FortressAI.Web/Program.cs`

---

## Summary of Findings

| Severity | Count | Items |
|----------|-------|-------|
| Critical | 0 | — |
| Important | 2 | tool_result event type mismatch, CCProgressHub auth gap |
| Nitpick | 3 | dead `_taskCancelled` field, CSS color fallbacks, wrong path in build report |

---

## Issues

### [Important] #1 — `tool_result` event type will never match in CC stream-json output

**File**: `harness-server.js:1760`

```js
} else if (evtType === 'tool_result') {
    const toolName = parsed.tool_name || parsed.name || 'tool';
    sendEvent({ type: 'task_progress', payload: JSON.stringify({ step: 'tool_result', toolName, status: 'done', message: `${toolName} completed` }) });
}
```

**Problem**: Claude Code's `--output-format stream-json` does NOT emit a top-level `{"type":"tool_result"}` event. Tool results are nested inside a `{"type":"user"}` event as content blocks of type `tool_result`. The actual stream-json event sequence is:

```json
{"type":"assistant","message":{"role":"assistant","content":[{"type":"tool_use","id":"...","name":"Bash","input":{...}}]}}
{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"...","content":"..."}]}}
```

The check `evtType === 'tool_result'` will **never** match because `evtType` is `parsed.type` at the top level (which is `"user"` for tool results). This means:
- No `task_progress` events with `step: 'tool_result'` will ever fire.
- Tools will show as "calling" in the timeline but will never flip to "done".
- The acceptance criterion "task_progress SSE events emitted for each CC tool call" is only half-satisfied — tool start events work, completion events do not.

**Fix**: Check for `evtType === 'user'` and iterate `parsed.message?.content` for blocks with `type === 'tool_result'`:
```js
} else if (evtType === 'user' && Array.isArray(parsed.message?.content)) {
    for (const block of parsed.message.content) {
        if (block.type === 'tool_result') {
            const toolName = block.tool_use_id || 'tool';
            sendEvent({ type: 'task_progress', payload: JSON.stringify({
                step: 'tool_result', toolName, status: 'done', message: `Tool completed`
            }) });
        }
    }
}
```

Note: `tool_use_id` (the ID, not name) is what's available in the `tool_result` block; the tool name itself is only in the preceding `tool_use` block. Consider tracking `tool_use_id → name` in a Map from the `assistant` event if the tool name is needed in the completion step.

---

### [Important] #2 — CCProgressHub: no caller authorization on `JoinUserGroup`

**File**: `CCProgressHub.cs:12`

```csharp
public async Task JoinUserGroup(string userId)
{
    await Groups.AddToGroupAsync(Context.ConnectionId, $"cc-user-{userId}");
}
```

**Problem**: The `userId` parameter comes directly from the client. Any authenticated user can call `JoinUserGroup("other-user-guid")` and receive another user's task progress events. There is no check that the caller's identity matches the requested group.

This hub is currently unused (no harness code pushes to it, no Blazor client subscribes), but it will be wired up in future cycles. Registering it insecure now establishes a vulnerable pattern.

**Fix**: Validate `userId` against the caller's authenticated identity before adding to the group:
```csharp
public async Task JoinUserGroup(string userId)
{
    var callerId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (callerId != userId)
        throw new HubException("Cannot join another user's group.");
    await Groups.AddToGroupAsync(Context.ConnectionId, $"cc-user-{userId}");
}
```

Same fix for `LeaveUserGroup`.

---

### [Nitpick] #3 — `_taskCancelled` field is set but never read

**File**: `ChatView.razor:447,452,737,1029`

```csharp
private bool _taskCancelled = false;
```

The field is set in `CancelTask()`, `HandleSend()`, and the `start` step handler, but it is never read by any rendering logic or conditional. The cancel UX is driven entirely by `_taskModeActive = false` and `_taskProgressSteps.Clear()`. `_taskCancelled` is dead state and should be removed.

---

### [Nitpick] #4 — CSS: hardcoded color fallbacks in new task-mode styles

**File**: `ChatView.razor` — `<style>` block, lines ~1663 and ~1678

```css
.btn-task-mode--active {
    color: var(--color-text-on-accent, #fff);  /* #fff is a hardcoded color fallback */
}
.chat-task-indicator {
    background: var(--color-accent-light, rgba(212, 175, 55, 0.1));  /* rgba() is hardcoded */
}
```

The standing CSS-variable rule requires zero hardcoded colors. These fallbacks should be either:
- A second-level CSS variable (e.g., `var(--color-text-on-accent, var(--color-text-primary))`), or
- Defined as variables in fortress.css and removed from the fallback position.

The `task-progress-timeline` classes themselves are clean (all fallbacks are numeric sizes or other variables). Only `.btn-task-mode--active` and `.chat-task-indicator` are affected.

---

### [Nitpick] #5 — Build report states wrong file path for `IUserAgentRuntime.cs`

**Build report claims**: `src/FortressAI.Web/Interfaces/IUserAgentRuntime.cs`
**Actual path**: `src/FortressAI.Web/Services/IUserAgentRuntime.cs`

Minor documentation error only — no code impact.

---

## Consistency Audit

| Check | Result |
|-------|--------|
| `mode_switch` emitted before CC spawn | ✓ Correct — line 1614, inside `if (taskMode)` block |
| `task_progress start` emitted before CC spawn | ✓ Correct — line 1615 |
| NDJSON line buffering (partial chunk handling) | ✓ Correct — `lines.pop()` pattern |
| `ccTextEmitted` dedup guard | ✓ Correct — prevents double-emit on `result` event |
| `--print` + `--output-format stream-json` co-existence | ✓ OK — `--print` is required for non-interactive mode; both flags are valid together |
| Blazor `@foreach` closure capture | ✓ `var stepLocal = step` correctly applied at line 203 |
| Timer lifecycle: created, disposed in all paths | ✓ HandleSend reset, finally block, CancelTask, DisposeAsync all dispose `_elapsedTimer` |
| `streamingCts` cleanup | ✓ DisposeAsync cancels and disposes; CancelTask cancels (no double-dispose risk) |
| No IHttpContextAccessor in new Blazor circuit code | ✓ Not present |
| No MudDialog inside @if | ✓ Not applicable to this WI |
| Bedrock path (taskMode=false) unchanged | ✓ Verified — line 1810 onward, entirely separate branch |
| Program.cs hub registration | ✓ Line 775: `app.MapHub<CCProgressHub>("/hubs/cc-progress")` |
| `HarnessEvent.Type` comment updated | ✓ `"task_progress"` added to comment at line 73 |
| `TaskProgressPayload` record fields match harness JSON | ✓ Step/ToolName/Status/Message match `step`/`toolName`/`status`/`message` in harness payload |

---

## What Works Well

- **NDJSON buffering** is correctly implemented — the `lines.pop()` pattern handles partial chunk boundaries cleanly.
- **Timer disposal** is thorough — covered in 4 separate code paths (reset, finally, cancel, dispose).
- **`task_progress start` handler** correctly disposes any prior timer before creating a new one (handles rapid re-submits).
- **Bedrock path isolation** is clean — no changes touch the `else` branch.
- **`TaskProgressPayload`** record design is clean and matches the JSON wire format.
- **`mode_switch` handler** correctly uses the event to set `_taskModeActive` rather than relying on the client's `_taskMode` toggle.
- **TakeLast(8)** capping on timeline render is a good UX choice.

---

## Required Changes Before Cycle 2 Approval

1. **Fix `tool_result` event handling** in harness NDJSON parser — check `evtType === 'user'` and iterate `message.content` for `tool_result` blocks.
2. **Add userId authorization** in `CCProgressHub.JoinUserGroup` / `LeaveUserGroup` — validate against `Context.User` claims.
3. (Optional but recommended) Remove `_taskCancelled` dead field.
4. (Optional) Replace hardcoded color fallbacks `#fff` and `rgba(212, 175, 55, 0.1)` with variable fallbacks.

Issues #1 and #2 are blocking. Issues #3 and #4 are recommended cleanups.
