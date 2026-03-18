# Review Brief: WI835 — FAIT Cowork Sprint 3

You are Hawkeye (Clint Barton), code reviewer. Review commit 546e10a in /home/fredw/projects/fip/cowork.

## Files to Review

New files:
- src/CoworkAgent/src/services/taskQueue.ts
- src/CoworkAgent/src/routes/users.ts
- src/CoworkWeb/Components/Pages/SettingsPage.razor
- src/CoworkWeb/Components/Shared/TaskQueue.razor

Modified files:
- src/CoworkAgent/src/services/forgeClient.ts
- src/CoworkAgent/src/agent/runner.ts
- src/CoworkAgent/src/routes/tasks.ts
- src/CoworkAgent/src/services/taskStore.ts
- src/CoworkWeb/Services/AgentApiClient.cs
- src/CoworkWeb/Components/Layout/MainLayout.razor

## Priority Checks

### HIGH: Lua atomic script in tryStartTask()
In taskQueue.ts — verify:
1. redis.eval(LUA_TRY_START, { keys: [...], arguments: [...] }) is a SINGLE atomic call
2. Lua script checks count AND increments in same body (no separate GET + INCR)
3. Keys use {userId} curly-brace hash tag for cluster slot co-location
4. NOT implemented as separate GET count + INCR (TOCTOU race would exist)

### HIGH: onTaskFinished() floors running_count at 0
1. await redis.decr(countKey) — decrement
2. if (newCount < 0) await redis.set(countKey, '0') — floor guard present
3. No path where decrement goes negative and stays negative

### HIGH: buildSearchForgeTool closure factory
1. buildSearchForgeTool is a factory function (not module-level singleton)
2. userId and userEmail are captured in the closure
3. In runner.ts, called as buildSearchForgeTool(params.userId, params.userEmail) per task

### HIGH: FORGE cache key includes userId
Cache key format must be cowork:forge-cache:${userId}:${hash} — NOT cowork:forge-cache:${hash}
Missing userId = cross-user data leak.

### MEDIUM: Persistent instructions NOT logged
In runner.ts — auditLog call must have data: { length: N } and NO content or text field with instructions

### MEDIUM: Cancellation check AFTER each message (not in hook)
In runner.ts — Redis cancellation check (cowork:cancel:<taskId>) must be in the for await loop body AFTER processing, NOT in preToolCall or postToolCall hooks

### MEDIUM: TaskQueue.razor polls at exactly 10 seconds
Verify TimeSpan.FromSeconds(10) — NOT 2, NOT 5
Also verify TimeSpan.Zero for initial delay

### MEDIUM: TaskQueue.razor IDisposable
1. Component implements IDisposable (@implements IDisposable)
2. public void Dispose() => _timer?.Dispose() present
3. Without this, dangling Timer fires after navigation

### LOW: _connectPromise ??= guard in taskStore.ts
Verify _connectPromise ??= (async () => { ... })() pattern to prevent double-connect

### LOW: No files outside fip/cowork/ modified
Zero changes to fip/fait/, fip/firm/, fip/forms/, fip/shared/

## Output
Provide a detailed code review report answering each check with PASS or FAIL and exact evidence (file, line numbers, code snippets).
