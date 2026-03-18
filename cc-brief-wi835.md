# CC Brief: WI835 — FAIT Cowork Sprint 3

## Working Directory
`/home/fredw/projects/fip/cowork/`

**CRITICAL: Do NOT touch any files outside `/home/fredw/projects/fip/cowork/`. All changes are Cowork-only.**

## Context
This is Sprint 3 of the FAIT Cowork application. Sprint 1 + Sprint 2 are already implemented and deployed.

### Current file state (already implemented in Sprint 2):
- `src/CoworkAgent/src/services/taskStore.ts` — Redis task store with `ensureConnected()`, `createTaskMeta()`, `publishChunk()`, `subscribeToTask()`, etc.
- `src/CoworkAgent/src/agent/runner.ts` — Agent runner with FORGE upfront injection, approval gate, tool hooks
- `src/CoworkAgent/src/services/forgeClient.ts` — `queryForgeContext()` function
- `src/CoworkAgent/src/routes/tasks.ts` — POST/GET tasks, SSE stream, approve/reject
- `src/CoworkAgent/src/server.ts` — Express server with auth + tasks router
- `src/CoworkWeb/Services/AgentApiClient.cs` — Blazor HTTP client proxy to CoworkAgent
- `src/CoworkWeb/Components/Layout/MainLayout.razor` — Top nav with FipNavBar + nav links

## Task List (11 tasks — ALL must be completed)

### Task 1: `src/CoworkAgent/src/services/forgeClient.ts` — MODIFY

Replace the existing file with an expanded version that:

1. Adds `ForgeResult` interface: `{ content: string; source: string; score: number }`
2. Adds `searchForge(query, userId, userEmail, options)` function that returns `ForgeResult[]`
3. Adds `formatForgeContextBlock(results: ForgeResult[]): string` (for system prompt upfront injection)
4. Adds `formatForgeToolResult(results: ForgeResult[]): string` (for SearchForge tool result)
5. Keeps `queryForgeContext()` as a convenience wrapper (backward compat)
6. Adds **Redis-cached** wrapper `queryForgeContextCached(prompt, userId, userEmail): Promise<string>`:
   - Cache key: `cowork:forge-cache:${userId}:${queryHash}` where queryHash = sha256(prompt.slice(0,200)).hex().slice(0,16)
   - **CRITICAL: Cache key MUST include userId — NOT just queryHash** (user isolation)
   - TTL: 600 seconds (10 minutes)
   - Uses `getRedis()` imported from `../services/taskStore.js`
7. Adds `buildSearchForgeTool(userId: string, userEmail: string)` factory function:
   - Returns a tool object with name `'SearchForge'`, description, input_schema, and execute()
   - **CRITICAL: Must use closure pattern** — userId and userEmail captured in closure, NOT module-level
   - execute(input) calls `searchForge()` then `formatForgeToolResult()`
   - input_schema: `{ type: 'object', properties: { query: { type: 'string', description: '...' }, topK: { type: 'number', description: '...' } }, required: ['query'] }`

Add `import crypto from 'crypto';` at top. Import `getRedis` from taskStore.

**EXACT implementation for `buildSearchForgeTool`:**
```typescript
export function buildSearchForgeTool(userId: string, userEmail: string) {
  return {
    name: 'SearchForge',
    description: `Search the FORGE knowledge base for relevant documents, notes, and context.
Use this when you need information about Fortress AM's funds, strategies, clients, policies, or past work.
The FORGE knowledge base contains internal documents and analysis — prefer it over guessing from general knowledge.
Returns the top matching results with source attribution.`,
    input_schema: {
      type: 'object' as const,
      properties: {
        query: {
          type: 'string',
          description: 'The search query — describe what information you need in natural language',
        },
        topK: {
          type: 'number',
          description: 'Number of results to return (1-8, default 5)',
        },
      },
      required: ['query'],
    },
    async execute(input: { query: string; topK?: number }) {
      const results = await searchForge(input.query, userId, userEmail, {
        topK: Math.min(input.topK ?? 5, 8),
      });
      return formatForgeToolResult(results);
    },
  };
}
```

**EXACT implementation for `queryForgeContextCached`:**
```typescript
export async function queryForgeContextCached(
  prompt: string,
  userId: string,
  userEmail: string
): Promise<string> {
  const redis = await getRedis();
  const hash = crypto.createHash('sha256').update(prompt.slice(0, 200)).digest('hex').slice(0, 16);
  const cacheKey = `cowork:forge-cache:${userId}:${hash}`;
  const cached = await redis.get(cacheKey);
  if (cached) return cached;
  const context = await queryForgeContext(prompt, userId, userEmail);
  if (context) {
    await redis.set(cacheKey, context, { EX: 600 });
  }
  return context;
}
```

---

### Task 2: `src/CoworkAgent/src/routes/users.ts` — NEW FILE

Create this file:

```typescript
import express from 'express';
import type { AuthedRequest } from '../middleware/auth.js';
import { getRedis } from '../services/taskStore.js';

const router = express.Router();

// GET /users/me/instructions
router.get('/me/instructions', async (req, res) => {
  const authed = req as unknown as AuthedRequest;
  const redis = await getRedis();
  const data = await redis.hGetAll(`cowork:user:${authed.userId}:instructions`);
  res.json({ text: data?.text ?? '', updatedAt: data?.updatedAt ?? null });
});

// PUT /users/me/instructions
router.put('/me/instructions', async (req, res) => {
  const authed = req as unknown as AuthedRequest;
  const { text } = req.body as { text: string };

  if (typeof text !== 'string') { res.status(400).json({ error: 'text required' }); return; }
  if (text.length > 2000) { res.status(400).json({ error: 'max 2000 characters' }); return; }

  const redis = await getRedis();
  if (text.trim() === '') {
    await redis.del(`cowork:user:${authed.userId}:instructions`);
  } else {
    await redis.hSet(`cowork:user:${authed.userId}:instructions`, {
      text: text.trim(),
      updatedAt: new Date().toISOString(),
    });
  }
  res.json({ ok: true });
});

export { router as usersRouter };
```

Note: `getRedis()` must be exported from taskStore.ts. See Task 6.

---

### Task 3: `src/CoworkAgent/src/services/taskQueue.ts` — NEW FILE

Create this file with atomic Lua script for tryStartTask and floor-at-0 for onTaskFinished:

```typescript
import { getRedis, getTaskMeta } from './taskStore.js';

const MAX_CONCURRENT = parseInt(process.env.COWORK_MAX_CONCURRENT_TASKS ?? '3', 10);

// CRITICAL: Atomic Lua script — prevents race condition when 2+ tasks start simultaneously
const LUA_TRY_START = `
  local countKey = KEYS[1]
  local queueKey = KEYS[2]
  local taskId   = ARGV[1]
  local maxConcurrent = tonumber(ARGV[2])
  local current = tonumber(redis.call('GET', countKey) or '0')
  if current >= maxConcurrent then
    redis.call('RPUSH', queueKey, taskId)
    return 0
  end
  redis.call('INCR', countKey)
  return 1
`;

/**
 * Attempt to start a task atomically.
 * Returns 'started' if a slot is available, 'queued' if at concurrency limit.
 * Uses Lua eval to atomically check count + increment (no TOCTOU race).
 */
export async function tryStartTask(taskId: string, userId: string): Promise<'started' | 'queued'> {
  const redis = await getRedis();

  // CRITICAL: {userId} curly-brace hash tag required for Redis cluster mode (same slot)
  const result = await redis.eval(LUA_TRY_START, {
    keys: [
      `cowork:user:{${userId}}:running_count`,
      `cowork:user:{${userId}}:queue`,
    ],
    arguments: [taskId, String(MAX_CONCURRENT)],
  });

  const started = result === 1;
  await redis.hSet(`cowork:task:${taskId}`, { status: started ? 'running' : 'queued' });
  return started ? 'started' : 'queued';
}

/**
 * Called when a task finishes (completed, failed, or cancelled).
 * Decrements running count with floor at 0, promotes next queued task.
 * Returns the promoted taskId, or null if queue was empty.
 */
export async function onTaskFinished(userId: string): Promise<string | null> {
  const redis = await getRedis();
  const countKey = `cowork:user:{${userId}}:running_count`;
  const queueKey = `cowork:user:{${userId}}:queue`;

  // CRITICAL: Decrement with floor at 0 — prevents negative count from blocking future tasks
  const newCount = await redis.decr(countKey);
  if (newCount < 0) await redis.set(countKey, '0');

  // Promote next queued task (FIFO)
  const nextTaskId = await redis.lPop(queueKey);
  if (nextTaskId) {
    await redis.hSet(`cowork:task:${nextTaskId}`, { status: 'running' });
    await redis.incr(countKey);
  }
  return nextTaskId ?? null;
}

/**
 * Cancel a task — removes from queue if queued, signals cancellation if running.
 */
export async function cancelTask(taskId: string, userId: string): Promise<void> {
  const redis = await getRedis();
  const meta = await getTaskMeta(taskId);
  if (!meta || meta.userId !== userId) return;

  if (meta.status === 'queued') {
    await redis.lRem(`cowork:user:{${userId}}:queue`, 0, taskId);
    await redis.hSet(`cowork:task:${taskId}`, {
      status: 'cancelled',
      completedAt: new Date().toISOString(),
    });
  } else if (meta.status === 'running') {
    await redis.set(`cowork:cancel:${taskId}`, '1', { EX: 60 });
    await redis.hSet(`cowork:task:${taskId}`, {
      status: 'cancelled',
      completedAt: new Date().toISOString(),
    });
    await onTaskFinished(userId);
  }
}

/**
 * Get the 1-based queue position for a queued task, or null if not queued.
 */
export async function getQueuePosition(taskId: string, userId: string): Promise<number | null> {
  const redis = await getRedis();
  const queue = await redis.lRange(`cowork:user:{${userId}}:queue`, 0, -1);
  const pos = queue.indexOf(taskId);
  return pos === -1 ? null : pos + 1;
}
```

---

### Task 4: `src/CoworkAgent/src/agent/runner.ts` — MODIFY

Make the following changes to the existing file:

1. **Update imports** — add:
   ```typescript
   import { buildSearchForgeTool, queryForgeContextCached } from '../services/forgeClient.js';
   import { getRedis } from '../services/taskStore.js';
   ```
   Change `queryForgeContext` import to `queryForgeContextCached`.

2. **Fetch persistent instructions** — add at the start of `runTask()`, before the FORGE query:
   ```typescript
   let persistentInstructions = '';
   try {
     const redis = await getRedis();
     const instrData = await redis.hGetAll(`cowork:user:${params.userId}:instructions`);
     persistentInstructions = instrData?.text ?? '';
   } catch {
     // Non-fatal — proceed without instructions
   }
   if (persistentInstructions) {
     await auditLog({
       event: 'instructions_loaded',
       taskId: params.taskId,
       userId: params.userId,
       data: { length: persistentInstructions.length }, // NO content field — must not log instruction text
     });
   }
   ```

3. **Replace `queryForgeContext` call** with `queryForgeContextCached`:
   ```typescript
   forgeContext = await queryForgeContextCached(params.prompt, params.userId, params.userEmail);
   ```

4. **Update system prompt assembly** to include persistent instructions:
   ```typescript
   const systemPrompt = [
     SYSTEM_PROMPT,
     persistentInstructions
       ? `## Your Standing Instructions\n${persistentInstructions}`
       : '',
     forgeContext
       ? `## Relevant Knowledge from FORGE\n${forgeContext}`
       : '',
   ].filter(Boolean).join('\n\n');
   ```
   (Remove the existing simple ternary that only added forgeContext)

5. **Add SearchForge custom tool** — create the tool using the closure pattern per task:
   ```typescript
   const forgeTool = buildSearchForgeTool(params.userId, params.userEmail);
   ```
   Add it to the `query()` options. The Agent SDK `query()` options take an `tools` array. Add:
   ```typescript
   tools: [forgeTool],
   ```
   alongside `allowedTools`. If the SDK doesn't support `tools` on `query()` options directly, wrap it as a custom tool following the existing SDK pattern for the pinned version. Check `node_modules/@anthropic-ai/claude-agent-sdk` for the correct option name.

6. **Add cancellation check AFTER each Agent SDK message** (NOT inside hooks):
   In the `for await (const message of query(...))` loop, AFTER processing each message and AFTER emitting pending chunks, add:
   ```typescript
   // Check cancellation AFTER processing each message (not inside hooks)
   const redis = await getRedis();
   const cancelled = await redis.get(`cowork:cancel:${params.taskId}`);
   if (cancelled) {
     await auditLog({ event: 'task_cancelled', taskId: params.taskId, userId: params.userId });
     yield { type: 'error', text: 'Task cancelled' };
     return;
   }
   ```

**CRITICAL rules:**
- Cancellation check is AFTER each message, OUTSIDE hooks
- Persistent instructions MUST NOT include the text in audit log (only length)
- buildSearchForgeTool is called per-task (inside runTask), never at module level

---

### Task 5: `src/CoworkAgent/src/routes/tasks.ts` — MODIFY

Make the following changes:

1. **Add imports** at top:
   ```typescript
   import { tryStartTask, onTaskFinished, cancelTask, getQueuePosition } from '../services/taskQueue.js';
   ```

2. **Update `POST /tasks`** — replace the direct `startTaskWithRedis()` call with queue integration:
   ```typescript
   // After createTaskMeta(), instead of directly calling startTaskWithRedis:
   const decision = await tryStartTask(taskId, authed.userId);

   if (decision === 'started') {
     startTaskWithRedis(taskId, workingDir, authed.userId, authed.userEmail, prompt).catch(console.error);
   } else {
     // Task queued — notify via pub/sub
     await publishChunk(taskId, {
       type: 'step',
       text: 'Task is queued — will start when a slot is available.',
     });
     const position = await getQueuePosition(taskId, authed.userId);
     await publishChunk(taskId, { type: 'queued', position });
   }

   res.json({ taskId, status: decision });
   ```

3. **Update `startTaskWithRedis()`** — add queue drain in the finally block:
   ```typescript
   async function startTaskWithRedis(
     taskId: string,
     workingDir: string,
     userId: string,
     userEmail: string,
     prompt: string
   ): Promise<void> {
     const outputFiles: object[] = [];
     try {
       // ... existing gen/for-await loop (unchanged)
     } catch (e: any) {
       await publishChunk(taskId, { type: 'error', text: e.message });
       await updateTaskFailed(taskId);
     } finally {
       // Always drain queue on finish
       const nextTaskId = await onTaskFinished(userId);
       if (nextTaskId) {
         const nextMeta = await getTaskMeta(nextTaskId);
         if (nextMeta) {
           await publishChunk(nextTaskId, { type: 'step', text: 'Task starting now…' });
           const nextWorkingDir = `/tmp/cowork-${nextTaskId}`;
           await fs.mkdir(nextWorkingDir, { recursive: true }).catch(() => {});
           startTaskWithRedis(nextTaskId, nextWorkingDir, nextMeta.userId, nextMeta.userEmail, nextMeta.prompt).catch(console.error);
         }
       }
     }
   }
   ```

4. **Add `DELETE /tasks/:id`** endpoint for cancellation:
   ```typescript
   router.delete('/:id', async (req, res) => {
     const authed = req as unknown as AuthedRequest;
     const { id } = req.params;
     await cancelTask(id, authed.userId);
     res.json({ ok: true });
   });
   ```

5. **Update `SseChunk` type** — add `queued` type and `position` field:
   ```typescript
   export interface SseChunk {
     type: 'step' | 'tool_call' | 'result' | 'file_output' | 'approval_required' | 'approval_resolved' | 'error' | 'queued';
     // ... existing fields ...
     position?: number; // queue position for 'queued' type
   }
   ```

---

### Task 6: `src/CoworkAgent/src/services/taskStore.ts` — MODIFY

Two changes:

1. **Fix `ensureConnected()` promise-cache guard** — replace the existing `_connected` boolean approach with a promise cache:

   Replace the current `_connected` boolean and `ensureConnected()` function with:
   ```typescript
   private _connectPromise: Promise<void> | null = null; // won't work as module-level — use this pattern instead:
   ```
   
   Since this is a module (not a class), use a module-level variable:
   ```typescript
   let _connectPromise: Promise<void> | null = null;
   
   async function ensureConnected(): Promise<void> {
     _connectPromise ??= (async () => {
       _redis    = createClient({ url: REDIS_URL });
       _redisSub = createClient({ url: REDIS_URL });
       await Promise.all([_redis.connect(), _redisSub.connect()]);
     })();
     return _connectPromise;
   }
   ```
   Remove the old `_connected` boolean entirely.

2. **Export `getRedis()` function** — add this exported function after `ensureConnected()`:
   ```typescript
   export async function getRedis(): Promise<ReturnType<typeof createClient>> {
     await ensureConnected();
     return redis();
   }
   ```
   This allows `forgeClient.ts` and `routes/users.ts` to get a connected Redis client.

3. **Update `TaskMeta` interface** to include new status values:
   ```typescript
   export interface TaskMeta {
     status: 'running' | 'completed' | 'failed' | 'queued' | 'cancelled';
     // ... rest unchanged
   }
   ```

---

### Task 7: `src/CoworkAgent/src/server.ts` — MODIFY

Add the users router registration:

```typescript
import { usersRouter } from './routes/users.js';
// ... existing imports ...

app.use('/tasks', tasksRouter);
app.use('/users', usersRouter);  // ADD THIS LINE
```

---

### Task 8: `src/CoworkWeb/Components/Pages/SettingsPage.razor` — NEW FILE

Create this file:

```razor
@page "/settings"
@inject AgentApiClient AgentApi
@inject CoworkSessionService Session

<PageTitle>Settings — FAIT Cowork</PageTitle>

<div style="max-width: 640px; margin: 0 auto; padding: 40px 16px;">

    <h1 style="font-size: var(--text-2xl); font-weight: var(--font-semibold); color: var(--color-text-primary); margin-bottom: 8px;">
        Settings
    </h1>
    <p style="color: var(--color-text-secondary); margin-bottom: 32px; font-size: var(--text-sm);">
        These settings apply to all your tasks.
    </p>

    <section style="background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: 20px; margin-bottom: 20px;">
        <h2 style="font-size: var(--text-lg); font-weight: var(--font-semibold); color: var(--color-text-primary); margin: 0 0 6px 0;">
            Standing Instructions
        </h2>
        <p style="font-size: var(--text-sm); color: var(--color-text-secondary); margin: 0 0 16px 0;">
            These instructions are prepended to every task. Use them to set your preferred tone, format, or context.
        </p>

        <MudTextField @bind-Value="_instructions"
                      Placeholder="e.g. &quot;Always use Fortress AM formal tone. Include a Key Takeaways section.&quot;"
                      Lines="4"
                      MaxLength="2000"
                      Variant="Variant.Outlined"
                      FullWidth="true"
                      Class="mb-2" />

        <div style="display: flex; justify-content: space-between; align-items: center; margin-top: 8px;">
            <span style="font-size: var(--text-xs); color: var(--color-text-muted);">
                @_instructions.Length / 2000
            </span>
            <div style="display: flex; gap: 8px;">
                @if (_instructions.Length > 0)
                {
                    <MudButton Variant="Variant.Text" Color="Color.Error" Size="Size.Small"
                               Disabled="@_saving"
                               OnClick="ClearInstructions">
                        Clear
                    </MudButton>
                }
                <MudButton Variant="Variant.Filled" Size="Size.Small"
                           Disabled="@(_saving || _instructions == _savedInstructions)"
                           OnClick="SaveInstructions"
                           Style="background: var(--color-btn-gold-bg); color: var(--color-btn-gold-text); font-weight: var(--font-semibold);">
                    @(_saving ? "Saving…" : "Save")
                </MudButton>
            </div>
        </div>

        @if (_saveMessage is not null)
        {
            <div style="margin-top: 8px; font-size: var(--text-xs); color: var(--color-success);">@_saveMessage</div>
        }
    </section>

</div>

@code {
    private string _instructions = string.Empty;
    private string _savedInstructions = string.Empty;
    private bool _saving;
    private string? _saveMessage;

    protected override async Task OnInitializedAsync()
    {
        var result = await AgentApi.GetInstructionsAsync();
        _instructions = result ?? string.Empty;
        _savedInstructions = _instructions;
    }

    private async Task SaveInstructions()
    {
        _saving = true;
        _saveMessage = null;
        try
        {
            await AgentApi.SaveInstructionsAsync(_instructions);
            _savedInstructions = _instructions;
            _saveMessage = "Saved ✓";
            await Task.Delay(2000);
            _saveMessage = null;
        }
        catch { _saveMessage = "Save failed — try again."; }
        finally { _saving = false; StateHasChanged(); }
    }

    private async Task ClearInstructions()
    {
        _instructions = string.Empty;
        await SaveInstructions();
    }
}
```

---

### Task 9: `src/CoworkWeb/Components/Shared/TaskQueue.razor` — NEW FILE

Create directory `src/CoworkWeb/Components/Shared/` if it doesn't exist, then create this file:

```razor
@* Components/Shared/TaskQueue.razor — running/queued task count badge *@
@inject AgentApiClient AgentApi
@implements IDisposable

@if (_runningCount > 0 || _queuedCount > 0)
{
    <div style="display: flex; align-items: center; gap: 8px; padding: 6px 12px; background: var(--color-surface); border-bottom: 1px solid var(--color-border); font-size: var(--text-xs); color: var(--color-text-secondary);">
        @if (_runningCount > 0)
        {
            <span>
                <span style="display:inline-block;width:7px;height:7px;border-radius:50%;background:var(--color-warning);animation:pulse 1.5s infinite;margin-right:4px;"></span>
                @_runningCount running
            </span>
        }
        @if (_queuedCount > 0)
        {
            <span>· @_queuedCount queued</span>
        }
        <a href="/tasks/history" style="color: var(--color-text-link); text-decoration: none; margin-left: 4px;">View →</a>
    </div>
}

@code {
    private int _runningCount;
    private int _queuedCount;
    private Timer? _timer;

    protected override async Task OnInitializedAsync()
    {
        await RefreshQueueAsync();
        // CRITICAL: Poll at 10s — NOT 2s. Task queue doesn't need sub-second freshness.
        _timer = new Timer(async _ => await InvokeAsync(async () =>
        {
            await RefreshQueueAsync();
            StateHasChanged();
        }), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
    }

    private async Task RefreshQueueAsync()
    {
        try
        {
            var tasks = await AgentApi.GetTaskHistoryAsync();
            _runningCount = tasks.Count(t => t.Status == "running");
            _queuedCount  = tasks.Count(t => t.Status == "queued");
        }
        catch { /* Non-fatal — stale count is acceptable */ }
    }

    // CRITICAL: Dispose must cancel the timer to prevent dangling polls after navigation
    public void Dispose() => _timer?.Dispose();
}
```

---

### Task 10: `src/CoworkWeb/Services/AgentApiClient.cs` — MODIFY

The existing file already has `CancelTaskAsync` but it uses POST to `/tasks/{taskId}/cancel`. Update it to use DELETE `/tasks/{taskId}` instead. Also add `GetInstructionsAsync` and `SaveInstructionsAsync`:

1. **Update `CancelTaskAsync`** — change from POST `/tasks/{taskId}/cancel` to DELETE `/tasks/{taskId}`:
   ```csharp
   public async Task CancelTaskAsync(string taskId, CancellationToken ct = default)
   {
       var client = CreateClient();
       try { await client.DeleteAsync($"/tasks/{taskId}", ct); }
       catch { /* Non-fatal */ }
   }
   ```

2. **Add `GetInstructionsAsync`**:
   ```csharp
   public async Task<string?> GetInstructionsAsync(CancellationToken ct = default)
   {
       var client = CreateClient();
       var resp = await client.GetAsync("/users/me/instructions", ct);
       if (!resp.IsSuccessStatusCode) return null;
       var body = await resp.Content.ReadFromJsonAsync<InstructionsResponse>(cancellationToken: ct);
       return body?.Text;
   }
   ```

3. **Add `SaveInstructionsAsync`**:
   ```csharp
   public async Task SaveInstructionsAsync(string text, CancellationToken ct = default)
   {
       var client = CreateClient();
       var resp = await client.PutAsJsonAsync("/users/me/instructions", new { text }, ct);
       resp.EnsureSuccessStatusCode();
   }
   ```

4. **Add private record** for deserialization (at the bottom of the file, alongside other records):
   ```csharp
   private record InstructionsResponse(string Text, string? UpdatedAt);
   ```
   Note: This must be a private record inside the class, OR a file-scoped record. Use `file record` pattern like the existing `TaskListResponse`.

---

### Task 11: `src/CoworkWeb/Components/Layout/MainLayout.razor` — MODIFY

Two changes:

1. **Add Settings nav link** to the `<nav>` bar (after "My Tasks"):
   ```razor
   <a href="/settings" style="font-size: var(--text-sm); color: var(--color-text-secondary); text-decoration: none;">Settings</a>
   ```

2. **Add `<TaskQueue />` component** between `<FipNavBar>` and `<nav>`:
   ```razor
   <FipNavBar ... />
   
   <TaskQueue />
   
   <nav ...>
   ```
   
   Add the using directive if needed. Since `TaskQueue.razor` is in `Components/Shared/`, it should be auto-discovered by Blazor if the `_Imports.razor` includes the namespace, or add it explicitly.

---

## Summary of Critical Constraints

1. **Lua script atomic** — `tryStartTask()` MUST use `redis.eval(LUA_TRY_START, { keys: [...], arguments: [...] })`. No separate GET + INCR.
2. **Hash tag `{userId}`** — queue keys must use `cowork:user:{${userId}}:running_count` and `cowork:user:{${userId}}:queue` for Redis cluster slot co-location.
3. **Floor at 0** — `onTaskFinished()` must floor running_count at 0 after decr.
4. **buildSearchForgeTool closure** — MUST be a factory function called per-task, userId/userEmail in closure.
5. **Cache key includes userId** — `cowork:forge-cache:${userId}:${hash}`, not `cowork:forge-cache:${hash}`.
6. **No instruction text in CloudWatch** — audit log only records `{ length: instructions.length }`.
7. **Cancellation after message** — check `cowork:cancel:${taskId}` after each message in the for-await loop, OUTSIDE hooks.
8. **TaskQueue polls at 10s** — `TimeSpan.FromSeconds(10)`, not 2s.
9. **TaskQueue Dispose()** — `public void Dispose() => _timer?.Dispose();`
10. **ensureConnected promise-cache** — use `_connectPromise ??= ...` pattern, remove old `_connected` boolean.

## File Boundary
ALL changes must be within `/home/fredw/projects/fip/cowork/`. Verify with:
```bash
cd /home/fredw/projects/fip && git diff --name-only HEAD | grep -v "^cowork/" | head -5
```
