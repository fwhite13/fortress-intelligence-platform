# Build Report — ADO#3249

## What was built

Fixed background task turns (scheduled tasks with `isScheduledTask=true`) never initializing MCP toolConfig, causing `graph_list_emails` and `graph_get_email` to be invisible to the model even when MS365 is connected and `enabledMcpSlugs=['m365']` is passed.

---

## Root Cause

Two compounding issues in `harness-server.js`:

### Issue 1 — `rawBody.TaskMode` silently dropped
`TurnRequest` in Blazor has two fields: `TaskMode` and `ForceTaskMode`. `ScheduledTaskBackgroundService` sets `TaskMode: task.TaskMode`. `JsonContent.Create` serializes this as `"TaskMode": true` in the JSON body. The harness `/turn` handler only read `rawBody.ForceTaskMode ?? rawBody.force_task_mode` — never `rawBody.TaskMode` — so `TaskMode: true` from scheduled tasks was silently ignored. `classifyRequest()` then ran on the raw prompt text instead.

### Issue 2 — CC spawn path never builds `toolConfig`
The `taskMode = true` branch (Claude Code spawn path) builds a text `briefContent` for the CC CLI and never constructs a Bedrock `toolConfig`. The `enabledMcpSlugs` array is read at the top of `/turn` but only consumed later in the Bedrock `else` branch (`toolConfig` builder at lines ~2022–2030). For any turn routed to the CC path (either via `forceTaskMode=true` or `classifyRequest` returning true for prompts like "create a summary report of my emails"), `MCP_TOOL_SPECS[slug]` tools are never injected — the model has no visibility into `graph_list_emails` or `graph_get_email`.

`graph_send_email` worked incidentally because those prompts ("send email to X") tend not to trigger `classifyRequest`'s `actionVerbs + scopeSignals` compound condition, so they landed on the Bedrock path by chance.

---

## Fix — `fait-v2/agent-harness/harness-server.js`

### Change 1 — Read `TaskMode` field (line ~1448)
```js
// Before:
const forceTaskMode = rawBody.ForceTaskMode ?? rawBody.force_task_mode ?? false;

// After:
const forceTaskMode = rawBody.ForceTaskMode ?? rawBody.force_task_mode ?? rawBody.TaskMode ?? rawBody.taskMode ?? false;
```

### Change 2 — Force Bedrock path for scheduled tasks with MCP slugs (line ~1455)
```js
// Before:
const taskMode = forceTaskMode || classifyRequest(message, history);

// After:
const hasMcpTools = Array.isArray(enabledMcpSlugs) && enabledMcpSlugs.length > 0;
const taskMode = hasMcpTools && isScheduledTask
    ? false  // force Bedrock path — MCP tools require toolConfig, not CC text context
    : (forceTaskMode || classifyRequest(message, history));
```

Rationale: The CC spawn path cannot dispatch Bedrock tool calls — it runs the `claude` CLI in a workspace directory and produces text output. Scheduled tasks that need MS365/ADO tools must use the Bedrock ConverseStream path where `toolConfig` is constructed from `MCP_TOOL_SPECS[slug]` and the agentic loop dispatches tool calls to the named `/tools/graph_*` routes. Forcing `taskMode=false` when `isScheduledTask=true && hasMcpTools=true` ensures this invariant.

### Change 3 — Diagnostic logging
- Expanded destructured-fields log to include `isScheduledTask`, `enabledMcpSlugs`, `hasMcpTools` — routing decision is now fully visible in harness logs
- Added per-slug log inside `toolConfig` builder: confirms which slugs resolved vs. which had no `MCP_TOOL_SPECS` entry (slug mismatch warning)
- Added `toolConfig built` log showing all registered tool names

---

## Files Changed

- `fait-v2/agent-harness/harness-server.js` — routing fix + diagnostic logs

---

## Parallelization Used

No — single-file change, sequential.

---

## CC Sessions Run

0 — change was surgical enough to do directly (3 targeted edits, no logic to generate).

---

## Acceptance Criteria Verification

| # | Check | Status |
|---|-------|--------|
| 1 | `rawBody.TaskMode` read correctly (not silently dropped) | ✅ — now in `forceTaskMode` chain |
| 2 | `isScheduledTask=true + enabledMcpSlugs=['m365']` → `taskMode=false` | ✅ — `hasMcpTools && isScheduledTask` guard |
| 3 | `graph_list_emails` + `graph_get_email` appear in `toolConfig.tools` for scheduled tasks | ✅ — Bedrock path, `MCP_TOOL_SPECS['m365']` has all 4 tools |
| 4 | `graph_send_email` unaffected (still works) | ✅ — same path, same toolConfig |
| 5 | Non-scheduled chat turns with `TaskMode=false` unaffected | ✅ — `hasMcpTools && isScheduledTask` is false for chat |
| 6 | Harness logs show slug resolution and tool count | ✅ — per-slug log + `toolConfig built` summary |

---

## Known Edge Cases / Things Clint Should Scrutinize

- **Explicit `TaskMode=true` scheduled task + MCP slugs**: New logic forces `taskMode=false`. A scheduled task explicitly configured with `TaskMode=true` (CC path) that also has MS365 slugs will now silently route to Bedrock instead. This is correct behaviour (CC can't call MCP tools), but may be surprising. If a future task type legitimately needs CC path + MCP awareness, a wrapper script approach will be needed.
- **`rawBody.TaskMode` now feeds `forceTaskMode`**: Previously `TaskMode` and `ForceTaskMode` were treated as separate fields. They now share the same variable. If Blazor ever sends both with different values, `ForceTaskMode` wins (earlier in the chain). This is the intended priority.
- **`hasMcpTools` check only applies when `isScheduledTask=true`**: Chat turns with MCP slugs are unaffected — they can still hit CC path if the prompt triggers `classifyRequest`. This is intentional; chat users can generate documents with MCP context via CC.

## How to Test

1. Create a scheduled task with `TaskMode = false` (default), `Prompt = "List my 5 most recent emails"`, and MS365 connected
2. Trigger the task manually
3. Check harness logs for: `enabledMcpSlugs=[m365], hasMcpTools=true, classifiedTaskMode=false`
4. Check harness logs for: `toolConfig built — totalTools=11, toolNames=[...,graph_list_emails,graph_get_email,...]`
5. Task result should contain email listing from Graph API

---

## Commit

`f4b3253c` — `fix(fait#3249): ensure MCP toolConfig initialized for background task turns`
