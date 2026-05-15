# ADO3351 Cycle 2 Fixes — harness-server.js

## Context

File: `/home/fredw/projects/fip/fait-v2/agent-harness/harness-server.js`

There are two fixes needed. No other changes.

---

## Fix 1: Double assistant message after reauth_required (lines ~2502-2560)

### Problem
In the agentic loop (around lines 2492-2559), when a `graph_*` or `ado_*` tool is called but the token is missing, the code:
1. Sets `toolResultText = statusMsg` and `isError = true`
2. Emits `sendEvent({ type: 'reauth_required', ... })`
3. Falls through to push a `pendingToolResults` entry
4. That entry gets fed back to Bedrock (around line 2657+), causing Bedrock to generate a second prose response

### Fix
After `sendEvent({ type: 'reauth_required', ... })` in BOTH the `graph_*` block (around line 2509) and the `ado_*` block (around line 2545), immediately:
1. Emit a `done` event: `sendEvent({ type: 'done' });`
2. End the response: `res.end();`
3. Return from the route handler: `return;`

This terminates the response immediately after showing the re-auth card, preventing Bedrock from ever seeing the tool_result.

The pattern to apply in both places:
```javascript
sendEvent({ type: 'reauth_required', provider: 'ms365', message: statusMsg });
sendEvent({ type: 'done' });
res.end();
return;
```

And for ADO:
```javascript
sendEvent({ type: 'reauth_required', provider: 'ado', message: statusMsg });
sendEvent({ type: 'done' });
res.end();
return;
```

**DO NOT** set `toolResultText` or `isError` for these paths — they're unreachable after return.
Actually, keep setting them for safety but add the return after the sendEvent calls.

---

## Fix 2: Indentation in else blocks (lines ~2521 and ~2558)

### Problem
The `} else {` blocks for `graph_*` and `ado_*` token-check branches are not indented properly.

Current (bad indentation):
```javascript
            } else {
            emitToolCall(res, 'graph', ...);
            try {
```

Expected (good indentation — the else block content should be indented one level deeper):
```javascript
            } else {
                emitToolCall(res, 'graph', ...);
                try {
```

### Fix
For the `graph_*` else block (~lines 2521-2535): indent all lines inside the else block by 4 additional spaces (one indent level).
For the `ado_*` else block (~lines 2558-2572): same — indent all lines inside the else block by 4 additional spaces.

The closing `}` of each else block also needs proper indentation alignment.

---

## IMPORTANT
- Only change harness-server.js
- No Blazor changes
- No other changes
- After making changes, verify with: grep -n "reauth_required" harness-server.js
- Then commit with message: "fix: terminate response after reauth_required, fix indentation in graph/ado else blocks (ADO#3351)"
- Push to origin/main
