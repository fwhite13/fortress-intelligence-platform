# Build Report: WI835 — FAIT Cowork Sprint 3

**Agent:** Tony Stark (software-engineer)
**Date:** 2026-03-17
**Model:** CC Sonnet (`claude --model sonnet -p --dangerously-skip-permissions`)
**CC Brief:** `~/projects/fait-for-excel/cc-brief-wi835.md`
**Commit:** `546e10a` — WI835: FAIT Cowork Sprint 3 — FORGE injection, persistent instructions, task queue
**Repo:** `/home/fredw/projects/fip/` — changes all within `cowork/`

---

## Summary

Cowork Sprint 3 implemented across 11 tasks: FORGE knowledge base injection as an agent tool, persistent standing instructions per user, Redis-backed task queue with atomic Lua concurrency control, cancellation support, and new Blazor UI components for settings and queue visibility.

---

## Task Completion

| # | File | Type | Status |
|---|------|------|--------|
| 1 | `src/CoworkAgent/src/services/forgeClient.ts` | Modified | ✅ Complete |
| 2 | `src/CoworkAgent/src/routes/users.ts` | New | ✅ Complete |
| 3 | `src/CoworkAgent/src/services/taskQueue.ts` | New | ✅ Complete |
| 4 | `src/CoworkAgent/src/agent/runner.ts` | Modified | ✅ Complete |
| 5 | `src/CoworkAgent/src/routes/tasks.ts` | Modified | ✅ Complete |
| 6 | `src/CoworkAgent/src/services/taskStore.ts` | Modified | ✅ Complete |
| 7 | `src/CoworkAgent/src/server.ts` | Modified | ✅ Complete |
| 8 | `src/CoworkWeb/Components/Pages/SettingsPage.razor` | New | ✅ Complete |
| 9 | `src/CoworkWeb/Components/Shared/TaskQueue.razor` | New | ✅ Complete |
| 10 | `src/CoworkWeb/Services/AgentApiClient.cs` | Modified | ✅ Complete |
| 11 | `src/CoworkWeb/Components/Layout/MainLayout.razor` | Modified | ✅ Complete |

---

## Critical Constraint Verification

### ✅ Lua atomic script in `tryStartTask()`
```
=== Lua atomic script in tryStartTask ===
6:const LUA_TRY_START = `
7:  local countKey = KEYS[1]
23: * Uses Lua eval to atomically check count + increment (no TOCTOU race).
29:  const result = await redis.eval(LUA_TRY_START, {
```
Verified: `redis.eval(LUA_TRY_START, { keys: [...], arguments: [...] })` — atomic, no separate GET+INCR.

### ✅ Hash tag `{userId}` in queue keys
```
31:      `cowork:user:{${userId}}:running_count`,
32:      `cowork:user:{${userId}}:queue`,
```
Verified: curly-brace hash tag present — required for Redis cluster slot co-location.

### ✅ `onTaskFinished()` floor at 0
```
53:  const newCount = await redis.decr(countKey);
54:  if (newCount < 0) await redis.set(countKey, '0');
```
Verified: decrements then floors at 0.

### ✅ `buildSearchForgeTool` closure factory
```
104:export function buildSearchForgeTool(userId: string, userEmail: string) {
```
Verified: factory function; userId/userEmail captured in closure, NOT module-level.

### ✅ FORGE cache key includes userId
```
90:  const cacheKey = `cowork:forge-cache:${userId}:${hash}`;
```
Verified: `cowork:forge-cache:${userId}:${hash}` — user-isolated, NOT global hash.

### ✅ Persistent instructions NOT in CloudWatch
```
event: 'instructions_loaded',
taskId: params.taskId,
userId: params.userId,
data: { length: persistentInstructions.length }, // NO content field — must not log instruction text
```
Verified: only `{ length }` logged, no `content` or `text` field.

### ✅ Cancellation check AFTER message (not in hook)
```
228:      // Check cancellation AFTER processing each message (not inside hooks)
230:      const cancelled = await redis.get(`cowork:cancel:${params.taskId}`);
231:      if (cancelled) {
```
Verified: check is inside `for await (const message ...)` loop, after message processing, OUTSIDE hooks.

### ✅ TaskQueue polls at 10s (NOT 2s)
```
36:        }), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
```
Verified: `TimeSpan.FromSeconds(10)`.

### ✅ TaskQueue `Dispose()` cancels Timer
```
51:    public void Dispose() => _timer?.Dispose();
```
Verified.

### ✅ `ensureConnected()` promise-cache guard
```
17:let _connectPromise: Promise<void> | null = null;
20:  _connectPromise ??= (async () => {
```
Verified: `??=` nullish assignment — prevents duplicate connect calls.

### ✅ No files outside `cowork/`
```
=== No files outside cowork/ ===
(no output — CLEAN)
```
Verified: all changes within `cowork/` boundary.

---

## What Was Built

### Task 1: `forgeClient.ts`
- Added `ForgeResult` interface (`content`, `source`, `score`)
- Added `searchForge()` — HTTP call to FORGE `/api/haven/kb-search`
- Added `formatForgeContextBlock()` — for system prompt upfront injection
- Added `formatForgeToolResult()` — for SearchForge tool results
- Added `queryForgeContextCached()` — Redis-cached with `cowork:forge-cache:${userId}:${hash}` key, 10m TTL
- Added `buildSearchForgeTool(userId, userEmail)` — closure factory for per-task agent tool

### Task 2: `routes/users.ts` (NEW)
- `GET /users/me/instructions` — fetch user's persistent instructions from Redis hash
- `PUT /users/me/instructions` — save/clear user's persistent instructions (max 2000 chars)
- Uses `getRedis()` from taskStore; clears key if text is empty

### Task 3: `services/taskQueue.ts` (NEW)
- `tryStartTask(taskId, userId)` — atomic Lua eval: increments count or enqueues (returns `'started'` | `'queued'`)
- `onTaskFinished(userId)` — decr with floor-at-0, promotes next task from queue
- `cancelTask(taskId, userId)` — removes from queue if queued, sets cancel key if running
- `getQueuePosition(taskId, userId)` — 1-based position in user's queue

### Task 4: `agent/runner.ts`
- Loads persistent instructions from Redis at task start (non-fatal if Redis error)
- Audit logs `{ length }` only — no instruction text in CloudWatch
- Updated system prompt assembly: `SYSTEM_PROMPT + Standing Instructions + FORGE context`
- Replaced `queryForgeContext()` with `queryForgeContextCached()`
- Added `buildSearchForgeTool(params.userId, params.userEmail)` per-task
- Injected `SearchForge` tool into `query()` options
- Added cancellation check after each message in `for await` loop

### Task 5: `routes/tasks.ts`
- Queue integration: `tryStartTask()` before starting async work
- Returns `{ taskId, status: 'started' | 'queued' }` from POST
- Extended `SseChunk` type with `'queued'` type and `position` field
- `finally` block in `startTaskWithRedis()` calls `onTaskFinished()` and promotes next task
- Added `DELETE /tasks/:id` for cancellation

### Task 6: `services/taskStore.ts`
- Replaced `_connected` boolean with `_connectPromise ??=` pattern
- Exported `getRedis()` for use by other services
- Extended `TaskMeta.status` with `'queued' | 'cancelled'`

### Task 7: `server.ts`
- Added `usersRouter` import and `app.use('/users', usersRouter)`

### Task 8: `SettingsPage.razor` (NEW)
- Route: `/settings`
- Text area for standing instructions (2000 char max with counter)
- Save/Clear buttons with debounced save confirmation
- Calls `AgentApi.GetInstructionsAsync()` / `SaveInstructionsAsync()`

### Task 9: `TaskQueue.razor` (NEW)
- Shows running/queued task counts in header bar
- 10s poll timer with `IDisposable` cleanup
- Only renders when there are active tasks (running > 0 or queued > 0)
- Link to task history

### Task 10: `AgentApiClient.cs`
- Added `GetInstructionsAsync()` — GET `/users/me/instructions`
- Added `SaveInstructionsAsync(text)` — PUT `/users/me/instructions`
- Updated `CancelTaskAsync()` to use `DeleteAsync` instead of POST
- Added `InstructionsResponse` private record

### Task 11: `MainLayout.razor`
- Added `<TaskQueue />` component between FipNavBar and nav links
- Added Settings nav link: `<a href="/settings">Settings</a>`

---

## Self-Review Checklist

- [x] All 11 tasks implemented and verified
- [x] CC Sonnet used for all code changes (`--dangerously-skip-permissions`)
- [x] Lua atomic script verified — no TOCTOU race
- [x] Hash tag `{userId}` in all Redis cluster-sensitive keys
- [x] Floor-at-0 guard in `onTaskFinished()`
- [x] `buildSearchForgeTool` is a closure factory, not module-level
- [x] FORGE cache key isolated per user
- [x] Instructions NOT logged (only length)
- [x] Cancellation check is in `for await` loop, after message, outside hooks
- [x] TaskQueue polls at 10s with proper Dispose()
- [x] `ensureConnected()` uses promise-cache guard
- [x] No files touched outside `cowork/` boundary
- [x] Clean git commit: `546e10a`
- [x] Committed to `cowork/` subdirectory only

---

## Ready for Review

Passing to Clint (code-reviewer) for REVIEW stage.
