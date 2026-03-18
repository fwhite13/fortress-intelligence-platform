# FAIT Cowork Sprint 3 — FORGE Injection + Persistent Instructions + Task Queue

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Architecture ref:** `COWORK-ARCHITECTURE-SPEC.md`  
**Depends on:** Sprint 1 + Sprint 2 fully deployed and passing acceptance criteria

---

## Pre-Read: What Was Read

- `COWORK-SPRINT1-SPEC.md` + `COWORK-SPRINT2-SPEC.md` — full S1+S2 file inventory
- `COWORK-ARCHITECTURE-SPEC.md` — S3 goals; FORGE injection pattern; prompt caching decision; Phase 2 items
- `RESEARCH-COWORK.md` — Bedrock prompt caching: 5-minute TTL, fixed per Bedrock (not configurable); `query()` `systemPrompt` is a string (no `cache_control` blocks at SDK level); Agent SDK `allowedTools` array can include custom tools
- `fip/fait-for-excel/src/taskpane/services/faitApi.ts` — `KbSearchResponse` interface: `results: Array<{content, source, score}>`; `topK`; `kbTypes`; `x-api-key` header
- S1's `forgeClient.ts` — already passes `x-user-id` + `x-user-email`; calls `topK: 3`; hardcoded `kbTypes: ['document', 'note']`

**Key constraint discovered during pre-read:** The Agent SDK's `systemPrompt` option is a `string`. It does not accept Anthropic's `TextBlockParam` array with `cache_control` metadata. Therefore, Bedrock prompt caching cannot be applied to the system prompt via the Agent SDK's native options. The workaround (detailed in Feature 1) is to use the Bedrock API directly for the initial context-fetch turn, then continue with the Agent SDK. This is non-trivial. **Sprint 3 decision: defer Bedrock prompt caching to Phase 2.** The token cost without caching is acceptable for MVP usage volume. Cache the FORGE query results in Redis instead (simpler, effective for repeated similar queries).

---

## Sprint 3 Objectives

| # | Feature | Complexity |
|---|---------|------------|
| 1 | FORGE KB injection — deep mode (agent as tool caller) | Medium |
| 2 | Persistent instructions — per-user standing prompt prepended to every task | Small |
| 3 | Task queue — max 3 concurrent per user; queue state in Redis; queue management UI | Medium |

---

## Decision Log

**FORGE injection mode — tool vs. system prompt:** Two architectures are possible:

**Option A — Upfront injection (S1 approach):** Query FORGE before starting the agent, inject top-N results into the system prompt. Simple. Good for general grounding.

**Option B — Agent-as-tool-caller (S3 approach):** Give the agent a `SearchForge` tool. The agent calls it mid-task when it needs KB context. Results come back as a `tool_result` message. Better for tasks where the relevant FORGE content depends on what the agent discovers as it works.

**Decision: Option B (agent as tool caller) — but Option A stays as the default fallback.** The agent always gets Option A upfront injection (top-3 general results) in the system prompt. Additionally, it gets the `SearchForge` tool for on-demand mid-task queries. This maximizes FORGE utilisation without requiring the agent to know to ask first.

The `SearchForge` tool is a **custom tool** — it is not in the Agent SDK's `allowedTools` whitelist of built-in tools. The Agent SDK supports custom tools via the `tools` option in `query()`. The tool implementation runs in Node.js (calls `forgeClient.queryForgeContext()`), and the result is returned as a `tool_result` message.

**Persistent instructions — storage:** Redis Hash `cowork:user:<userId>:instructions`. Not a database — these are short strings (a few sentences at most). Redis is already present from S2. TTL: none (persistent until user deletes). Value max: 2000 characters (enforced server-side).

**Task queue — concurrency limit:** Max 3 concurrent running tasks per user. Enforced in `POST /tasks` handler by checking `cowork:user:<userId>:running_count`. Queue: tasks submitted when the user is at the limit are queued as `status: 'queued'` in Redis and started when a running task completes. No queue re-ordering in Sprint 3 (FIFO only). Cancel is supported for both running and queued tasks.

---

## Feature 1: FORGE KB Injection (Deep Mode)

### What changes vs S1/S2

S1 called `queryForgeContext()` once before launching the agent and injected results into `systemPrompt`. S2 kept this pattern (referenced in the S2 `runner.ts` change note). S3 keeps the upfront injection AND adds the `SearchForge` custom tool.

### `src/services/forgeClient.ts` — Expanded API

Add a raw result return (needed by the SearchForge tool to pass structured results back to the agent):

```typescript
export interface ForgeResult {
  content: string;
  source: string;
  score: number;
}

/**
 * Query FORGE for context and return raw results.
 * Used by SearchForge tool to give the agent structured results it can reason about.
 */
export async function searchForge(
  query: string,
  userId: string,
  userEmail: string,
  options: {
    topK?: number;
    kbTypes?: string[];
  } = {}
): Promise<ForgeResult[]> {
  if (!FORGE_API_KEY) return [];

  const resp = await fetch(`${FORGE_API_URL}/api/haven/kb-search`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'x-api-key':    FORGE_API_KEY,
      'x-user-id':    userId,
      'x-user-email': userEmail,
    },
    body: JSON.stringify({
      query: query.slice(0, 500),
      topK:    options.topK ?? 5,
      kbTypes: options.kbTypes ?? ['document', 'note'],
    }),
  });

  if (!resp.ok) return [];

  const { results } = await resp.json() as { results: ForgeResult[] };
  return results ?? [];
}

/**
 * Format results as a system prompt injection block (upfront context).
 * Used for the initial system prompt — keeps existing S1/S2 behaviour.
 */
export function formatForgeContextBlock(results: ForgeResult[]): string {
  if (results.length === 0) return '';
  return results
    .map((r, i) => `[${i + 1}] Source: ${r.source}\n${r.content.slice(0, 600)}`)
    .join('\n\n');
}

/**
 * Format results as a tool_result content string (mid-task search).
 * Used by the SearchForge tool handler.
 */
export function formatForgeToolResult(results: ForgeResult[]): string {
  if (results.length === 0) {
    return 'No results found in the FORGE knowledge base for this query.';
  }
  return `Found ${results.length} result(s) in the FORGE knowledge base:\n\n` +
    results
      .map((r, i) => `**[${i + 1}] ${r.source}** (relevance: ${(r.score * 100).toFixed(0)}%)\n${r.content.slice(0, 800)}`)
      .join('\n\n---\n\n');
}

// Existing queryForgeContext() remains as a convenience wrapper used by runner.ts upfront injection
export async function queryForgeContext(prompt: string, userId: string, userEmail: string): Promise<string> {
  const results = await searchForge(prompt, userId, userEmail, { topK: 3 });
  return formatForgeContextBlock(results);
}
```

### `src/agent/runner.ts` — Add SearchForge Custom Tool

The Agent SDK's `query()` accepts a `tools` array in options. Each custom tool has a `name`, `description`, `input_schema` (JSON Schema), and the handler is a function called by the SDK when the agent invokes it.

```typescript
import { searchForge, formatForgeToolResult } from '../services/forgeClient';

// Build the SearchForge tool definition
const searchForgeTool = {
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
  // Tool handler — called by Agent SDK when agent uses SearchForge
  handler: async (input: { query: string; topK?: number }, context: { userId: string; userEmail: string }) => {
    await auditLog({
      event: 'forge_search',
      taskId: context.userId, // Use userId as context since taskId isn't directly accessible here
      userId: context.userId,
      data: { query: input.query.slice(0, 200), topK: input.topK },
    });
    const results = await searchForge(input.query, context.userId, context.userEmail, {
      topK: Math.min(input.topK ?? 5, 8),
    });
    return formatForgeToolResult(results);
  },
};
```

**Passing user context to the tool handler:** The Agent SDK's tool handler receives `(input, context)` where `context` is the extra object we pass via the `toolContext` option (or `context` option — check the exact SDK API for the version pinned). Since the tool handler needs `userId` and `userEmail` to call `searchForge`, these must be threaded through. If the SDK doesn't have a `toolContext` option, use a closure:

```typescript
// Closure approach (always works regardless of SDK version):
function buildSearchForgeTool(userId: string, userEmail: string) {
  return {
    name: 'SearchForge',
    description: `...`, // same as above
    input_schema: { /* same as above */ },
    handler: async (input: { query: string; topK?: number }) => {
      await auditLog({ event: 'forge_search', taskId: '(tool)', userId, data: { query: input.query.slice(0, 200) } });
      const results = await searchForge(input.query, userId, userEmail, { topK: Math.min(input.topK ?? 5, 8) });
      return formatForgeToolResult(results);
    },
  };
}
```

**Updated `query()` options in `runTask()`:**

```typescript
for await (const message of query({
  prompt: params.prompt,
  options: {
    cwd: params.workingDir,
    allowedTools: ['Read', 'Write', 'Edit', 'Bash'],  // Built-in tools (unchanged)
    tools: [buildSearchForgeTool(params.userId, params.userEmail)],  // Custom tools (S3 new)
    maxBudgetUsd: params.maxBudgetUsd,
    maxTurns: params.maxTurns,
    systemPrompt,
    // ... hooks unchanged
  },
}))
```

**Note on the Agent SDK `tools` option:** The exact API for registering custom tools must be verified against the pinned SDK version. If the SDK doesn't support a `tools` array on `query()` options (it may require wrapping as a `tool_use` message in the conversation manually), the fallback is to use the `preToolCall` hook approach in reverse — implement a `postToolCall` hook that intercepts `SearchForge` calls and returns the result. Check the SDK changelog before implementing.

### Upfront Injection: Redis Caching for FORGE Results

Since Bedrock prompt caching is not available via the Agent SDK, cache FORGE query results in Redis instead.

**Cache key:** `cowork:forge-cache:<userId>:<queryHash>` where `queryHash = sha256(query.slice(0, 200)).hex().slice(0, 16)`

**TTL:** 10 minutes (FORGE content doesn't change frequently; 10 minutes gives good cache hits for similar tasks)

```typescript
import crypto from 'crypto';

async function queryForgeContextCached(
  prompt: string,
  userId: string,
  userEmail: string
): Promise<string> {
  const redis = await getRedis();
  const hash = crypto.createHash('sha256').update(prompt.slice(0, 200)).digest('hex').slice(0, 16);
  const cacheKey = `cowork:forge-cache:${userId}:${hash}`;

  // Try cache first
  const cached = await redis.get(cacheKey);
  if (cached) return cached;

  // Miss — query FORGE
  const context = await queryForgeContext(prompt, userId, userEmail);
  if (context) {
    await redis.set(cacheKey, context, { EX: 600 }); // 10 minutes
  }
  return context;
}
```

Replace the `queryForgeContext()` call in `runner.ts` with `queryForgeContextCached()`.

### System Prompt Structure (S3 final form)

```
[STATIC SYSTEM PROMPT — same for every task]
You are FAIT Cowork…
<output guidelines>
<security boundaries>

[PERSISTENT INSTRUCTIONS — per user, from Redis]
## Your Standing Instructions
<user's saved instructions, if any>

[FORGE UPFRONT CONTEXT — cached 10 min, per user+query]
## Relevant Knowledge from FORGE
<top-3 results from kb-search>

[SearchForge tool is available for additional on-demand queries]
```

The static portion is the longest (most token-dense). It would benefit from prompt caching, but that requires the Bedrock API directly. Deferred to Phase 2.

---

## Feature 2: Persistent Instructions

### What They Are

Persistent instructions are short, free-text standing instructions that a user configures once. They are prepended to the system prompt on every task they run. Examples:
- "Always use Fortress Asset Management's formal tone. Never say 'I' — always say 'we' or refer to Fortress AM."
- "Assume my audience is institutional investors with 10+ years of experience."
- "When creating documents, always include a 'Key Takeaways' section at the top."

### Storage: Redis Hash

```
Key: cowork:user:<userId>:instructions
Type: Hash
TTL: none (persistent)
Fields:
  text        — the instruction text (max 2000 chars)
  updatedAt   — ISO 8601 timestamp
```

### Node.js: Two New API Endpoints

**`GET /users/me/instructions`:**

```typescript
router.get('/me/instructions', async (req, res) => {
  const authed = req as AuthedRequest;
  const redis = await getRedis();
  const data = await redis.hGetAll(`cowork:user:${authed.userId}:instructions`);
  res.json({ text: data?.text ?? '', updatedAt: data?.updatedAt ?? null });
});
```

**`PUT /users/me/instructions`:**

```typescript
router.put('/me/instructions', async (req, res) => {
  const authed = req as AuthedRequest;
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
```

### `src/agent/runner.ts` — Inject Persistent Instructions

```typescript
// At the start of runTask(), fetch persistent instructions
let persistentInstructions = '';
try {
  const redis = await getRedis();
  const data = await redis.hGetAll(`cowork:user:${params.userId}:instructions`);
  persistentInstructions = data?.text ?? '';
} catch {
  // Non-fatal — proceed without instructions
}
```

In the system prompt assembly:

```typescript
const systemPrompt = [
  STATIC_SYSTEM_PROMPT,
  persistentInstructions
    ? `## Your Standing Instructions\n${persistentInstructions}`
    : '',
  forgeContext
    ? `## Relevant Knowledge from FORGE\n${forgeContext}`
    : '',
].filter(Boolean).join('\n\n');
```

### Blazor: `SettingsPage.razor` (new page)

New page at `/settings`. Houses persistent instructions editor for Sprint 3. Phase 2 adds more settings here.

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

    <!-- Persistent Instructions -->
    <section style="background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: 20px; margin-bottom: 20px;">
        <h2 style="font-size: var(--text-lg); font-weight: var(--font-semibold); color: var(--color-text-primary); margin: 0 0 6px 0;">
            Standing Instructions
        </h2>
        <p style="font-size: var(--text-sm); color: var(--color-text-secondary); margin: 0 0 16px 0;">
            These instructions are prepended to every task. Use them to set your preferred tone, format, or context.
        </p>

        <MudTextField @bind-Value="_instructions"
                      Placeholder='e.g. "Always use Fortress AM formal tone. Include a Key Takeaways section."'
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
        finally { _saving = false; }
    }

    private async Task ClearInstructions()
    {
        _instructions = string.Empty;
        await SaveInstructions();
    }
}
```

### `AgentApiClient.cs` — Add instructions methods

```csharp
public async Task<string?> GetInstructionsAsync(CancellationToken ct = default)
{
    var client = CreateClient();
    var resp = await client.GetAsync("/users/me/instructions", ct);
    if (!resp.IsSuccessStatusCode) return null;
    var body = await resp.Content.ReadFromJsonAsync<InstructionsResponse>(ct: ct);
    return body?.Text;
}

public async Task SaveInstructionsAsync(string text, CancellationToken ct = default)
{
    var client = CreateClient();
    var resp = await client.PutAsJsonAsync("/users/me/instructions", new { text }, ct);
    resp.EnsureSuccessStatusCode();
}

private record InstructionsResponse(string Text, string? UpdatedAt);
```

### Add "Settings" link to nav

`MainLayout.razor` nav bar — add:
```razor
<a href="/settings" style="...">Settings</a>
```

---

## Feature 3: Task Queue

### Concurrency Model

**Limit:** Max 3 concurrent running tasks per user (configurable via `COWORK_MAX_CONCURRENT_TASKS` env var, default 3).

**Enforcement:** `POST /tasks` checks the running count before starting. If at limit, the task is stored with `status: 'queued'` in Redis and added to the user's queue list `cowork:user:<userId>:queue` (a Redis List, FIFO). When any task for that user completes or fails, a queue-drain function promotes the next queued task to running.

**Why 3 and not 1?** Elise and Lauren may start a long document task, then submit a quick summarize-this-file task. Sequential would make them wait. 3 concurrent gives breathing room without creating ECS/Bedrock throughput issues.

### Redis Data Model Changes

**New keys (additions to S2 model):**

| Key | Type | TTL | Purpose |
|-----|------|-----|---------|
| `cowork:user:<userId>:running_count` | String (integer) | No TTL | Current running task count; decremented on complete/fail |
| `cowork:user:<userId>:queue` | List | 7 days | FIFO queue of taskIds waiting to start |

**Updated `cowork:task:<taskId>` hash fields:**

Add one field:
```
status    "queued" | "running" | "completed" | "failed" | "cancelled"
```

### `src/services/taskStore.ts` — Queue Functions

```typescript
const MAX_CONCURRENT = parseInt(process.env.COWORK_MAX_CONCURRENT_TASKS ?? '3', 10);

/** Attempt to start a task: returns 'started' if capacity available, 'queued' if not. */
export async function tryStartTask(taskId: string, userId: string): Promise<'started' | 'queued'> {
  const redis = await getRedis();
  const countKey = `cowork:user:${userId}:running_count`;

  // Atomic increment + check via Lua script (prevents race condition)
  const script = `
    local count = tonumber(redis.call('GET', KEYS[1]) or '0')
    if count < tonumber(ARGV[1]) then
      redis.call('INCR', KEYS[1])
      return 'started'
    else
      redis.call('RPUSH', KEYS[2], ARGV[2])
      return 'queued'
    end
  `;
  const result = await redis.eval(script, {
    keys: [countKey, `cowork:user:${userId}:queue`],
    arguments: [String(MAX_CONCURRENT), taskId],
  });

  const decision = result as string;
  await redis.hSet(`cowork:task:${taskId}`, { status: decision === 'started' ? 'running' : 'queued' });
  return decision === 'started' ? 'started' : 'queued';
}

/** Called when a task finishes (complete, fail, or cancel). Decrements count, promotes next queued task. */
export async function onTaskFinished(userId: string): Promise<string | null> {
  const redis = await getRedis();
  const countKey = `cowork:user:${userId}:running_count`;
  const queueKey = `cowork:user:${userId}:queue`;

  // Decrement running count (floor at 0)
  const newCount = await redis.decr(countKey);
  if (newCount < 0) await redis.set(countKey, '0');

  // Promote next queued task
  const nextTaskId = await redis.lPop(queueKey);
  if (nextTaskId) {
    await redis.hSet(`cowork:task:${nextTaskId}`, { status: 'running' });
    await redis.incr(countKey);
  }
  return nextTaskId ?? null; // Return promoted taskId so caller can start it
}

/** Cancel a task — removes from queue if queued, or marks as cancelled if running. */
export async function cancelTask(taskId: string, userId: string): Promise<void> {
  const redis = await getRedis();
  const meta = await getTaskMeta(taskId);
  if (!meta || meta.userId !== userId) return;

  if (meta.status === 'queued') {
    // Remove from queue list
    await redis.lRem(`cowork:user:${userId}:queue`, 0, taskId);
    await redis.hSet(`cowork:task:${taskId}`, { status: 'cancelled', completedAt: new Date().toISOString() });
  } else if (meta.status === 'running') {
    // Signal cancellation — runner polls cancellation key
    await redis.set(`cowork:cancel:${taskId}`, '1', { EX: 60 });
    await redis.hSet(`cowork:task:${taskId}`, { status: 'cancelled', completedAt: new Date().toISOString() });
    await onTaskFinished(userId);
  }
}
```

### `src/agent/runner.ts` — Cancellation Support

Add a cancellation check inside the Agent SDK loop. The agent SDK doesn't have a built-in cancel; we poll a Redis key:

```typescript
// Inside the for-await loop, after each message:
const cancelled = await redis.get(`cowork:cancel:${params.taskId}`);
if (cancelled) {
  await auditLog({ event: 'task_cancelled', taskId: params.taskId, userId: params.userId });
  // Break the generator — the SSE stream will end with an error chunk
  throw new Error('Task cancelled by user');
}
```

### Updated `routes/tasks.ts` — Queue Integration

**`POST /tasks` — queue check:**

```typescript
// Replace direct startTaskAsync call with:
const decision = await tryStartTask(taskId, authed.userId);

if (decision === 'started') {
  // Start immediately
  startTaskWithRedis(taskId, authed.userId, generateChunks).catch(console.error);
} else {
  // Task is queued — emit a 'queued' status chunk via Pub/Sub
  await publishChunk(taskId, {
    type: 'step',
    text: 'Task is queued — will start when a slot is available.',
  });
  // Emit a 'queued' chunk so the browser knows the status
  await publishChunk(taskId, { type: 'queued', position: await getQueuePosition(taskId, authed.userId) });
}

res.json({ taskId, status: decision });
```

**Updated `startTaskWithRedis()`:**

```typescript
async function startTaskWithRedis(taskId: string, userId: string, genFactory: () => AsyncGenerator<SseChunk>): Promise<void> {
  const outputFiles: object[] = [];
  try {
    for await (const chunk of genFactory()) {
      await redis.rPush(`cowork:stream:log:${taskId}`, JSON.stringify(chunk));
      await redis.expire(`cowork:stream:log:${taskId}`, 3600);
      await publishChunk(taskId, chunk);
      if (chunk.type === 'file_output') {
        outputFiles.push({ name: chunk.fileName, type: chunk.outputType, downloadUrl: chunk.downloadUrl });
      }
      if (chunk.type === 'result' || chunk.type === 'error') break;
    }
    await updateTaskComplete(taskId, outputFiles);
  } catch (e: any) {
    await publishChunk(taskId, { type: 'error', text: e.message });
    await updateTaskFailed(taskId);
  } finally {
    // Always drain the queue on finish
    const nextTaskId = await onTaskFinished(userId);
    if (nextTaskId) {
      const nextMeta = await getTaskMeta(nextTaskId);
      if (nextMeta) {
        await publishChunk(nextTaskId, { type: 'step', text: 'Task starting now…' });
        startTaskWithRedis(nextTaskId, userId, () => generateChunksForTask(nextTaskId, nextMeta)).catch(console.error);
      }
    }
  }
}
```

**Note:** `generateChunksForTask()` needs to reconstruct the generator for the newly promoted queued task. This requires storing the task parameters (prompt, workingDir) in the task metadata hash, not just in the generator closure. Add `prompt` and `workingDir` fields to `createTaskMeta()` — they are already stored (S2 stores `prompt`; add `workingDir`).

**New endpoint: `DELETE /tasks/:id` — cancel:**

```typescript
router.delete('/:id', async (req, res) => {
  const authed = req as AuthedRequest;
  const { id } = req.params;
  await cancelTask(id, authed.userId);
  res.json({ ok: true });
});
```

**New helper: `GET /tasks?userId` — add queue position:**

Update the task list response to include a `queuePosition` field for queued tasks.

```typescript
async function getQueuePosition(taskId: string, userId: string): Promise<number | null> {
  const queue = await redis.lRange(`cowork:user:${userId}:queue`, 0, -1);
  const pos = queue.indexOf(taskId);
  return pos === -1 ? null : pos + 1;
}
```

### Blazor: `TaskQueue.razor` Component (new)

A small queue indicator shown in `TaskHistory.razor` and optionally as a floating badge. Shows running + queued task counts.

```razor
@* Components/Shared/TaskQueue.razor *@
@inject AgentApiClient AgentApi
@inject NavigationManager Nav
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
    private Timer? _pollTimer;

    protected override async Task OnInitializedAsync()
    {
        await Refresh();
        _pollTimer = new Timer(async _ => await InvokeAsync(async () => { await Refresh(); StateHasChanged(); }), null, 10_000, 10_000);
    }

    private async Task Refresh()
    {
        var tasks = await AgentApi.GetTaskHistoryAsync();
        _runningCount = tasks.Count(t => t.Status == "running");
        _queuedCount  = tasks.Count(t => t.Status == "queued");
    }

    public void Dispose() => _pollTimer?.Dispose();
}
```

Add `<TaskQueue />` to `MainLayout.razor` between the FipNavBar and the `<nav>` bar (just below the top nav).

### Blazor: `TaskHistory.razor` — Cancel Button

Add a cancel button to each running/queued task card:

```razor
@if (task.Status is "running" or "queued")
{
    <button @onclick:stopPropagation="true"
            @onclick="@(async () => await CancelTask(task.TaskId))"
            style="background: none; border: 1px solid var(--color-border); color: var(--color-text-muted); border-radius: var(--radius-sm); padding: 2px 8px; font-size: var(--text-xs); cursor: pointer;">
        Cancel
    </button>
}
```

```csharp
private async Task CancelTask(string taskId)
{
    await AgentApi.CancelTaskAsync(taskId);
    await Refresh(); // Reload the task list
}
```

### `AgentApiClient.cs` — Cancel method

```csharp
public async Task CancelTaskAsync(string taskId, CancellationToken ct = default)
{
    var client = CreateClient();
    var resp = await client.DeleteAsync($"/tasks/{taskId}", ct);
    resp.EnsureSuccessStatusCode();
}
```

### `TaskPage.razor` — Queued State

When a task's status is `'queued'`, the SSE stream emits a `queued` chunk. `ProcessChunk()` needs to handle it:

```csharp
// Add to SseChunk record:
int? Position = null,

// Add to ProcessChunk():
case "queued":
    _steps.Add($"Waiting in queue — position {chunk.Position}");
    break;
```

Also add a "Cancel" button at the bottom of the task page for running/queued tasks:

```razor
@if (!_done)
{
    <div style="margin-top: 16px; text-align: right;">
        <MudButton Variant="Variant.Outlined" Color="Color.Error" Size="Size.Small"
                   OnClick="HandleCancel">
            Cancel Task
        </MudButton>
    </div>
}
```

```csharp
private async Task HandleCancel()
{
    _cts.Cancel(); // Stop local SSE consumption
    await AgentApi.CancelTaskAsync(TaskId);
    _done = true;
    _steps.Add("Task cancelled.");
    StateHasChanged();
}
```

---

## Updated New API Route: `src/routes/users.ts`

All user-settings endpoints should live in a dedicated router:

```typescript
import express from 'express';
import type { AuthedRequest } from '../middleware/auth';
import { getRedis } from '../services/taskStore';

const router = express.Router();

router.get('/me/instructions', async (req, res) => { /* ... */ });
router.put('/me/instructions', async (req, res) => { /* ... */ });

export { router as usersRouter };
```

Register in `server.ts`:
```typescript
import { usersRouter } from './routes/users';
app.use('/users', usersRouter);
```

---

## Files Changed Summary

### New: `fip/cowork/src/CoworkAgent/src/`

| File | Purpose |
|------|---------|
| `routes/users.ts` | GET/PUT `/users/me/instructions` |

### New: `fip/cowork/src/CoworkWeb/`

| File | Purpose |
|------|---------|
| `Components/Pages/SettingsPage.razor` | Persistent instructions editor |
| `Components/Shared/TaskQueue.razor` | Running/queued task count indicator |

### Modified: `fip/cowork/src/CoworkAgent/src/`

| File | Change |
|------|--------|
| `services/forgeClient.ts` | Add `searchForge()`, `formatForgeToolResult()`, `formatForgeContextBlock()`; Redis caching wrapper `queryForgeContextCached()` |
| `agent/runner.ts` | Add `SearchForge` custom tool; add persistent instructions fetch; use `queryForgeContextCached()`; add cancellation polling loop |
| `routes/tasks.ts` | Queue integration (`tryStartTask`); add `DELETE /tasks/:id`; update `startTaskWithRedis` to drain queue on finish |
| `services/taskStore.ts` | Add `tryStartTask()`, `onTaskFinished()`, `cancelTask()`, `getQueuePosition()`; add `workingDir` to task metadata; add queue keys to Redis model |
| `server.ts` | Register `usersRouter` |

### Modified: `fip/cowork/src/CoworkWeb/`

| File | Change |
|------|--------|
| `Services/AgentApiClient.cs` | Add `GetInstructionsAsync()`, `SaveInstructionsAsync()`, `CancelTaskAsync()` |
| `Components/Pages/TaskHistory.razor` | Add cancel button per task; add `Refresh()` method |
| `Components/Pages/TaskPage.razor` | Handle `queued` SSE chunk; add cancel button |
| `Components/Layout/MainLayout.razor` | Add `<TaskQueue />` + Settings nav link |

### Environment Variables (additions)

```
COWORK_MAX_CONCURRENT_TASKS=3   ← CoworkAgent container
```

No new infrastructure. Uses Redis (S2), S3 (S2), ElastiCache (S2).

**Total: 3 new files + 8 modified. No new npm packages. No new AWS services.**

---

## Acceptance Criteria

1. **FORGE upfront injection:** Start a task with a prompt referencing Fortress AM funds. Verify the agent's system prompt (via CloudWatch log — log the systemPrompt length in `task_started` event) includes a `## Relevant Knowledge from FORGE` section. The section should have at least 1 result if the FORGE KB has relevant content.

2. **SearchForge tool mid-task:** Start a task where the agent would need mid-task FORGE context ("create an analysis of our flagship fund's Q1 performance"). Verify in the step feed that Claude invokes the `SearchForge` tool (visible as a `tool_call` step) and the results appear in subsequent agent reasoning.

3. **FORGE caching:** Submit two tasks with identical prompts. Verify the second task's FORGE query is served from Redis cache (no FAIT API call) — check by inspecting CloudWatch logs for `forge_search` events. Second task should have no `forge_search` event within 10 minutes of the first.

4. **Persistent instructions — set:** Navigate to `/settings`. Enter instructions, click Save. Navigate to `/settings` again — instructions are still there (loaded from Redis on page init).

5. **Persistent instructions — applied:** Set instructions: "Always include a disclaimer at the top." Start a new task. Verify the agent's output includes the disclaimer (or that the system prompt in CloudWatch includes the instructions text).

6. **Persistent instructions — clear:** Click "Clear" → instructions removed from Redis → next task does not include the instructions.

7. **Queue limit — enforced:** Submit 4 tasks in rapid succession. The first 3 start immediately (status: running). The 4th shows "Waiting in queue — position 1" in the task stream. CloudWatch shows `status: queued` in task metadata.

8. **Queue drain:** When one of the running tasks completes, the queued task starts automatically. The queue task page transitions from "Waiting in queue" to showing active steps. No manual action required.

9. **Cancel queued task:** A task in position 1 of the queue is cancelled. It disappears from the queue. The position counter for any remaining queued tasks updates.

10. **Cancel running task:** A running task is cancelled via the "Cancel Task" button on `TaskPage`. The SSE stream closes. The task card in history shows "cancelled" status. The queue drains to promote the next queued task.

---

## Constraints for CC

- `tryStartTask()` **must use an atomic Lua script** to prevent race conditions. Two simultaneous `POST /tasks` requests arriving within milliseconds can both read `count < 3` and both increment — resulting in 4 concurrent tasks. The Lua script atomically checks and increments in a single Redis round-trip. Do not implement this as separate `GET count` + `INCR` calls.
- `generateChunksForTask()` for queue-promoted tasks needs the original task parameters (prompt, workingDir). These must be stored in `cowork:task:<taskId>` hash at creation time. Add `workingDir` to `createTaskMeta()`. Prompt is already stored (S2).
- `onTaskFinished()` must cap the `running_count` at 0 (`if (newCount < 0) set(countKey, '0')`). If the count goes negative due to a cancelled task racing with a completion, subsequent queue promotions will incorrectly block.
- The `SearchForge` tool handler must be built with a **closure** over `userId` and `userEmail` — not relying on a `toolContext` parameter (which may not exist in the pinned SDK version). The `buildSearchForgeTool(userId, userEmail)` factory pattern is the safe approach.
- Cancellation polling in `runner.ts` checks `cowork:cancel:<taskId>` **after each Agent SDK message** — not inside the `preToolCall` hook. The `preToolCall` hook runs mid-tool; cancellation at that point is ambiguous. Checking after each top-level message is safer.
- The `SettingsPage.razor` character counter (`@_instructions.Length / 2000`) must update in real-time as the user types. Since `@bind-Value` triggers on `oninput` in MudBlazor, this should work automatically. Verify in testing.
- `TaskQueue.razor` polls every 10 seconds (not 2 seconds). Task queue status doesn't need sub-second freshness, and polling every 2 seconds would generate 30 Redis reads/minute per open browser tab. 10 seconds is acceptable.
- Do NOT touch any `fip/fait/`, `fip/firm/`, `fip/forms/`, or `fip/shared/FipShared/` files. All S3 changes are Cowork-only.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify tryStartTask() uses an atomic Lua script (redis.eval with both
          the COUNT check and the INCR/RPUSH in the same script body).
          A two-step GET + INCR is a race condition under concurrent load.
          Check that the script is tested with 4+ simultaneous POST /tasks
          requests before merging.

⚠️  HIGH: Verify onTaskFinished() floors running_count at 0. Check the decr()
          call is followed by: if (newCount < 0) await redis.set(countKey, '0').
          Without this, a series of cancellations can drive the count negative,
          permanently blocking all future task starts for that user.

⚠️  HIGH: Verify the SearchForge tool uses the closure pattern:
          buildSearchForgeTool(userId, userEmail) returns a new tool object
          per task with userId/userEmail captured in the closure. If the tool
          is created once at module level (without the closure), all tasks
          will search FORGE with the first user's identity.

⚠️  MEDIUM: Verify the FORGE cache key includes the userId:
            cowork:forge-cache:<userId>:<queryHash>
            Missing userId means User A's cached results could be returned to
            User B. This is a data leak risk, not just a correctness issue.

⚠️  MEDIUM: Verify the persistent instructions are NOT logged in CloudWatch.
            The audit log should record that instructions were loaded (and their
            length), but NOT the instruction text — it may contain sensitive
            phrasing or internal strategy references.

⚠️  MEDIUM: Verify the cancellation polling in runner.ts checks redis AFTER
            each top-level Agent SDK message, not inside preToolCall or
            postToolCall hooks. Cancellation mid-tool-call leaves the working
            directory in an undefined state.

⚠️  LOW: Verify TaskQueue.razor Dispose() correctly disposes the Timer.
         A missing Dispose will leave a dangling timer that polls Redis every
         10 seconds after the component is removed from the DOM (e.g., user
         navigates to a page without the layout that hosts TaskQueue).

⚠️  LOW: Verify the SettingsPage cancel (Clear) button only appears when
         _instructions.Length > 0. Showing "Clear" when there's nothing to
         clear is confusing UX. Check the @if condition renders correctly
         after a save completes (the saved state updates _savedInstructions).
```

---

## What Sprint 3 Does NOT Include (Explicit Deferrals)

**Bedrock prompt caching:** The Agent SDK's `systemPrompt` is a string — it cannot carry `cache_control` blocks. Direct Bedrock API usage (bypassing the Agent SDK for the initial turn) is required to get caching. This adds significant complexity. FORGE result caching in Redis provides most of the cost benefit with much less complexity. Bedrock cache_control deferred to Phase 2.

**FORGE as a write-back target:** The arch spec lists "FORGE write-back (save outputs to FORGE KB)" as a Phase 2 feature. Sprint 3 is read-only FORGE access. Phase 2: agent gets a `SaveToForge` tool that uploads its output `.md` file to a specified FORGE KB node.

**Queue re-ordering:** FIFO only in Sprint 3. Phase 2: drag-to-reorder in the queue UI.

**Admin panel (usage/cost visibility):** Phase 2. Sprint 3 adds CloudWatch structured logging for cost fields, but no UI.

---

_Spec by Reed Richards | Cowork S3: 3 new files + 8 modified. FORGE deep integration, persistent instructions, FIFO task queue with concurrency limit. No new AWS services._
