# Review Report: WI835
## Verdict: PASS
## Review Cycle: 1 of 2

---

## CC Invocation

```bash
cd /home/fredw/projects/fip/cowork && cat ~/projects/fait-for-excel/review-brief-wi835.md | claude --model sonnet -p
```

First 20 lines of output:
```
# Code Review Report — WI835 FAIT Cowork Sprint 3
**Reviewer:** Hawkeye (Clint Barton)
**Commit:** `546e10a`
**Date:** 2026-03-17

---

## HIGH Priority Checks

### ✅ PASS — Lua atomic script in `tryStartTask()`

**1. Single atomic `redis.eval()` call**

`taskQueue.ts:29–35` — one call, no split:
const result = await redis.eval(LUA_TRY_START, {
  keys: [
    `cowork:user:{${userId}}:running_count`,
    `cowork:user:{${userId}}:queue`,
  ],
  arguments: [taskId, String(MAX_CONCURRENT)],
```

---

## Priority Checks

| Check | Result | Evidence |
|-------|--------|----------|
| Lua eval — single atomic call | ✅ | `taskQueue.ts:29–35` — single `redis.eval(LUA_TRY_START, { keys: [...], arguments: [...] })` |
| {userId} hash tag in Lua keys | ✅ | `taskQueue.ts:30–31` — `` `cowork:user:{${userId}}:running_count` `` and `` `cowork:user:{${userId}}:queue` `` |
| NOT separate GET + INCR | ✅ | No JS-level GET precedes `eval()`. Lua body: `GET → compare → INCR/RPUSH`, all atomic |
| onTaskFinished floor at 0 | ✅ | `taskQueue.ts:53–54` — `redis.decr(countKey)` + `if (newCount < 0) await redis.set(countKey, '0')` |
| buildSearchForgeTool is closure factory | ✅ | `forgeClient.ts:104` — `export function buildSearchForgeTool(userId: string, userEmail: string)` — factory, not singleton |
| runner.ts calls buildSearchForgeTool per task | ✅ | `runner.ts:126` — `const forgeTool = buildSearchForgeTool(params.userId, params.userEmail)` inside `runTask()` |
| FORGE cache key includes userId | ✅ | `forgeClient.ts:90` — `` const cacheKey = `cowork:forge-cache:${userId}:${hash}` `` |
| Instructions: length only in audit log (no content) | ✅ | `runner.ts:100–105` — `data: { length: persistentInstructions.length }` — no `content`/`text` field present |
| Cancellation after message (not in hook) | ✅ | `runner.ts:228–235` — check is in `for await` loop body after message processing; `PreToolUse` hook contains no cancellation check |
| TaskQueue 10s interval (not 2s or 5s) | ✅ | `TaskQueue.razor:36` — `TimeSpan.FromSeconds(10)` confirmed |
| TaskQueue IDisposable + Dispose() | ✅ | `TaskQueue.razor:3` — `@implements IDisposable`; `TaskQueue.razor:51` — `public void Dispose() => _timer?.Dispose()` |
| ensureConnected _connectPromise ??= guard | ✅ | `taskStore.ts:20` — `_connectPromise ??= (async () => { ... })()` — concurrent callers share the same promise |
| No files outside fip/cowork/ | ✅ | `git diff --name-only 546e10a~1 546e10a` — zero files outside `cowork/` prefix |

---

## Issues Found

### Non-Blocking Observation: Double `onTaskFinished()` on user cancellation

**Severity:** Important (not critical — floor guard prevents persistent negative counter)

**Location:** `taskQueue.ts:cancelTask()` + `routes/tasks.ts:startTaskWithRedis()` finally block

**Description:** When a user cancels a running task, two calls to `onTaskFinished()` fire in sequence:
1. `cancelTask()` (`taskQueue.ts:85`) calls `onTaskFinished()` → decrements count, pops and promotes one queued task
2. The runner detects the cancel signal at `runner.ts:233`, yields `error`, breaking the `for await` loop
3. `startTaskWithRedis()` `finally` block (`tasks.ts:120`) calls `onTaskFinished()` again → second decrement (floor guard fires), second `lPop` → second queued task promoted into one vacated slot

**Net effect:** For a user-cancelled task, two queued tasks may be promoted when only one slot was freed. The counter doesn't go negative (floor guard), but the concurrency limit can be violated by 1 for that user.

**Recommendation:** Add a cancellation guard in the `startTaskWithRedis` finally block to check whether the task was cancelled (vs. completed/failed) before calling `onTaskFinished()`, since `cancelTask()` already called it. Alternatively, consolidate to a single `onTaskFinished()` call via Lua for decrement+pop.

**Track as:** Follow-up work item — does not block this sprint.

---

## Verdict

**PASS** — All 13 priority checklist items verified. Implementation is correct and clean.

The one observation (double `onTaskFinished()` on cancel) is a latent overrun bug — the floor guard prevents a permanently negative counter, but the concurrency limit can transiently over-promote during a cancel event. Non-critical for sprint 3 delivery. Recommend tracking as a follow-up.

**Ready to advance to SECURITY stage.**

---
## Post-deploy CI fix diff review (546e10a → c4083da)

### CC verdict
CC reviewed the CI fix diff (buildSearchForgeTool → createSdkMcpServer + mcpServers). All four primary criteria pass. CC flagged `inputSchema` as potentially missing a `z.object({...})` wrapper, but inspection of the SDK type definitions confirms that `SdkMcpToolDefinition.inputSchema` is typed as `Schema extends AnyZodRawShape` (a plain object of Zod fields — not wrapped), so the current code is **correct per the SDK contract**. CC also noted `topK` has no lower-bound guard (no `Math.max(1, ...)`), which is a pre-existing minor gap, not a regression introduced in this commit.

### Checks
| Item | Result |
|------|--------|
| buildSearchForgeMcpServer closure captures userId/userEmail per-task | ✅ |
| mcp__forge__SearchForge in allowedTools | ✅ |
| mcpServers wired per task invocation | ✅ |
| FORGE cache key still includes userId | ✅ |
| No security regressions | ✅ |

### Notes
- `inputSchema` uses raw `ZodRawShape` (plain object of Zod fields) — matches `SdkMcpToolDefinition<Schema extends AnyZodRawShape>` SDK type. No `z.object()` wrapper needed.
- `topK` missing `Math.max(1, ...)` lower-bound guard is pre-existing (not introduced by this commit). Low severity follow-up.
- `mcp__forge__SearchForge` naming convention (`mcp__{serverName}__{toolName}`) aligns with `mcpServers: { forge: forgeMcpServer }` key.

### Verdict: CLEAR
