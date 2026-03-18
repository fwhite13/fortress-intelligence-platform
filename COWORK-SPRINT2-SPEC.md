# FAIT Cowork Sprint 2 — Output Types + Approval Gates + Task History + Redis

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Architecture ref:** `COWORK-ARCHITECTURE-SPEC.md`  
**Depends on:** Sprint 1 (`COWORK-SPRINT1-SPEC.md`) fully deployed and passing acceptance criteria

---

## Pre-Read: What Was Read

- `COWORK-SPRINT1-SPEC.md` — full S1 file inventory; SSE chunk types; in-memory `taskStreams` Map; auto-approve `preToolCall` hook
- `COWORK-ARCHITECTURE-SPEC.md` — approval gate async pattern; destructive command patterns; S3 file storage design
- `fip/fait/src/FortressAI.Web/Components/Chat/MessageBubble.razor` — existing Markdig markdown→HTML rendering with `UseAdvancedExtensions()` pipeline (lines 88–207)
- `fip/fait/src/FortressAI.Web/Services/DocumentService.cs` — existing `DocumentFormat.OpenXml.Wordprocessing` usage; confirms .docx read capability; FIP already carries the OpenXml dependency
- `fip/fait/src/FortressAI.Web/FortressAI.Web.csproj` — Markdig `0.37.0` + DocumentFormat.OpenXml `3.4.1` already in FIP

**Key finding:** FIP already has both Markdig (markdown→HTML) and DocumentFormat.OpenXml (.docx parsing). Cowork uses the same patterns for output rendering — no new research needed.

---

## Sprint 2 Objectives

| # | Feature | Complexity |
|---|---------|------------|
| 1 | Output types — markdown, CSV/table, .docx download | Medium |
| 2 | S3 file storage — upload inputs, download outputs via pre-signed URL | Medium |
| 3 | Approval gate UI — tool call interception, pause/resume, approve/deny | Medium-Large |
| 4 | Redis — replace in-memory `taskStreams` Map; persist task metadata | Medium |
| 5 | Task history page — list past tasks by user, re-open output | Medium |

---

## Decision Log (Up Front)

**Document output format:** The agent writes `.md` (Markdown), not `.docx`. Reasoning:
- Claude can generate clean Markdown natively
- Markdig (already in FAIT) renders it to HTML in Blazor with zero extra dependencies
- `.docx` generation from Markdown requires DocumentFormat.OpenXml + a complex paragraph builder — it's a full document conversion problem
- `.docx` is offered as a **download conversion** (Markdown → OpenXml → .docx) if the user requests it; it is not the primary document output format
- The Cowork system prompt instructs Claude to produce `.md` files for document outputs

**Data analysis output:** Agent produces a `.md` file with analysis text + an optional `.csv` file with extracted/transformed tabular data. Blazor renders the `.md` as HTML. For CSV: Blazor renders the first N rows as an HTML table (server-side, no JS). No chart generation in Sprint 2 (deferred to Sprint 3 — requires Chart.js client-side render or server-side chart service).

**Redis:** ElastiCache Redis (AWS managed) is the Sprint 2 state store. Two uses:
1. **Task streams:** Replace the in-memory `AsyncGenerator<SseChunk>` Map with Redis Pub/Sub — generator publishes chunks to a channel; SSE endpoint subscribes. This allows multiple concurrent SSE consumers and survives container restarts.
2. **Task metadata:** `HSET cowork:task:<taskId>` stores status, userId, prompt, createdAt, completedAt, outputFiles. This powers the task history page.

Redis key TTL: 7 days (task data expires automatically).

**Approval gate async pattern:** The `preToolCall` hook returns a Promise. When a destructive command is detected, the hook publishes an `approval_required` event to Redis Pub/Sub, then **awaits a Redis key** (`cowork:approval:<taskId>`) to be SET. The approve/reject API endpoint sets that key. This creates a cross-request pause/resume that works across container restarts.

---

## Feature 1: Output Types

### What the Agent Produces (System Prompt Addition)

Add output guidance to the system prompt in `runner.ts`:

```
Output guidelines by task type:
- Documents / reports: write a .md file (Markdown). Use headers, bullet points, tables.
- Data analysis: write a .md file for insights + optionally a .csv file for tabular data.
- HTML prototypes: write a .html file (self-contained, inline CSS, no CDN links).
- General text: write a .txt file if no other format is better.
- If creating multiple output files, name them clearly (e.g. report.md, data.csv).
```

### Node.js: `collectOutputFiles()` — Output Type Detection

S1's `collectOutputFiles()` detects only `.html`. S2 extends detection to all output types:

```typescript
export type OutputType = 'html' | 'markdown' | 'csv' | 'docx' | 'txt' | 'other';

export interface OutputFile {
  name: string;
  type: OutputType;
  base64?: string;        // For inline rendering (html, markdown, csv)
  downloadUrl: string;    // Always present — S3 pre-signed URL
  sizeBytes: number;
}

function detectOutputType(filename: string): OutputType {
  const ext = path.extname(filename).toLowerCase();
  const map: Record<string, OutputType> = {
    '.html': 'html',
    '.htm':  'html',
    '.md':   'markdown',
    '.csv':  'csv',
    '.docx': 'docx',
    '.txt':  'txt',
  };
  return map[ext] ?? 'other';
}

async function collectOutputFiles(workingDir: string, taskId: string): Promise<SseChunk[]> {
  const chunks: SseChunk[] = [];
  try {
    const entries = await fs.readdir(workingDir, { withFileTypes: true });

    for (const entry of entries) {
      if (!entry.isFile()) continue;
      const filePath = path.join(workingDir, entry.name);
      const stat = await fs.stat(filePath);
      const type = detectOutputType(entry.name);

      // Upload to S3 and get pre-signed download URL
      const downloadUrl = await uploadOutputToS3(filePath, taskId, entry.name);

      // For inline-renderable types: include base64 content (capped at 512KB)
      let base64: string | undefined;
      if (['html', 'markdown', 'csv'].includes(type) && stat.size < 512 * 1024) {
        const content = await fs.readFile(filePath, 'utf-8');
        base64 = Buffer.from(content).toString('base64');
      }

      chunks.push({
        type: 'file_output',
        outputType: type,
        fileName: entry.name,
        downloadUrl,
        base64,
        sizeBytes: stat.size,
      });
    }
  } catch { /* Non-fatal */ }
  return chunks;
}
```

### Updated `SseChunk` type (add `outputType` and `sizeBytes`)

```typescript
interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'file_output' | 'approval_required' | 'approval_resolved' | 'error';
  // step / result / error
  text?: string;
  // tool_call
  toolName?: string;
  // file_output
  outputType?: OutputType;
  fileName?: string;
  downloadUrl?: string;
  base64?: string;
  sizeBytes?: number;
  // approval_required
  approvalId?: string;
  approvalToolName?: string;
  approvalToolInput?: any;
  approvalDescription?: string;
}
```

**Note:** `html_output` is retired as a separate type. HTML files are now `file_output` with `outputType: 'html'`. Blazor's `ProcessChunk()` routes on `outputType`.

### Blazor: `OutputPanel.razor` Component (extracted from `TaskPage.razor`)

S1 inlined the output panel in `TaskPage.razor`. S2 extracts it as a separate component so it can be reused in the task history page.

`OutputPanel.razor` receives a list of `OutputFile` records and renders each based on its type:

```
html      → <iframe srcdoc="..." sandbox="allow-scripts">
markdown  → @((MarkupString)RenderedHtml)  using Markdig.Markdown.ToHtml()
csv       → <table> rendered server-side from CSV rows (first 100 rows)
docx      → Download link only (no inline render; too complex)
txt       → <pre> block
other     → Download link only
```

**Blazor packages needed:**
- `Markdig` — already in FAIT; add to `CoworkWeb.csproj`
- `DocumentFormat.OpenXml` — NOT needed for Sprint 2. .docx is download-only. Add in Sprint 3 if .docx preview is required.

---

## Feature 2: S3 File Storage

S1 served files from the local container filesystem (`/tasks/files/...`). S2 moves to S3.

### What changes in Node.js

**`src/services/fileService.ts`** — add upload/download functions:

```typescript
import { S3Client, PutObjectCommand, GetObjectCommand } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import fs from 'fs/promises';
import path from 'path';

const s3 = new S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });
const BUCKET = process.env.COWORK_S3_BUCKET ?? 'fip-cowork-workspaces';
const PRESIGN_TTL_SECONDS = 900; // 15 minutes

/** Upload a local file to S3 and return a pre-signed download URL. */
export async function uploadOutputToS3(
  localPath: string,
  taskId: string,
  fileName: string
): Promise<string> {
  const key = `tasks/${taskId}/output/${fileName}`;
  const body = await fs.readFile(localPath);

  await s3.send(new PutObjectCommand({
    Bucket: BUCKET,
    Key: key,
    Body: body,
    ServerSideEncryption: 'AES256',
  }));

  return getSignedUrl(s3, new GetObjectCommand({ Bucket: BUCKET, Key: key }), {
    expiresIn: PRESIGN_TTL_SECONDS,
  });
}

/** Download uploaded input files from S3 to the task working directory. */
export async function downloadInputsFromS3(taskId: string, workingDir: string): Promise<void> {
  // Sprint 2: inputs are uploaded to S3 at task creation, then downloaded here
  // Sprint 1 used multer directly to local disk; S2 switches to S3
  const { ListObjectsV2Command } = await import('@aws-sdk/client-s3');
  const list = await s3.send(new ListObjectsV2Command({
    Bucket: BUCKET,
    Prefix: `tasks/${taskId}/input/`,
  }));

  for (const obj of list.Contents ?? []) {
    if (!obj.Key) continue;
    const resp = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: obj.Key }));
    const fileName = path.basename(obj.Key);
    const localPath = path.join(workingDir, fileName);
    const body = resp.Body as NodeJS.ReadableStream;
    const chunks: Buffer[] = [];
    for await (const chunk of body) chunks.push(Buffer.from(chunk));
    await fs.writeFile(localPath, Buffer.concat(chunks));
  }
}

/** Upload input files from multipart upload to S3. Called in POST /tasks handler. */
export async function uploadInputsToS3(
  files: Express.Multer.File[],
  taskId: string
): Promise<void> {
  for (const file of files) {
    const key = `tasks/${taskId}/input/${file.originalname}`;
    const body = await fs.readFile(file.path);
    await s3.send(new PutObjectCommand({
      Bucket: BUCKET,
      Key: key,
      Body: body,
      ServerSideEncryption: 'AES256',
    }));
    await fs.unlink(file.path); // Clean up multer temp file
  }
}
```

**Updated `POST /tasks` handler** in `routes/tasks.ts`: replace `fs.rename` with `uploadInputsToS3()`. Add `downloadInputsFromS3()` call at the start of `runTask()` before the Agent SDK loop.

---

## Feature 3: Approval Gate UI

This is the most architecturally significant S2 change.

### The Async Pause/Resume Problem

The Agent SDK's `preToolCall` hook is `async` — it awaits its return value before executing the tool. This means we can pause the agent by returning a Promise that doesn't resolve until the user approves. But the approval comes from a **different HTTP request** (the browser POST to `/tasks/:id/approve`). We need cross-request synchronization.

**Solution: Redis-backed approval gate.** When the gate needs approval:
1. Publish `approval_required` SSE event with `approvalId`
2. Create a Promise that polls a Redis key (`cowork:approval:<approvalId>`) every 200ms for up to 5 minutes
3. When the approve/reject endpoint fires, it `SET cowork:approval:<approvalId> "approve"` (or `"reject"`) with a 30-second TTL
4. The polling Promise resolves, and `preToolCall` returns `{ action: 'allow' | 'block' }`

This is simpler than Redis Pub/Sub for the approval case and works with the in-process generator.

### What Requires Approval

**Auto-approved (no user prompt, logged only):**
- `Read` — any file in working dir
- `Write` — any file in working dir  
- `Edit` — any file in working dir
- `Bash` — safe commands: `python3`, `node`, `cat`, `echo`, `ls`, `wc`, `grep`, `sed`, `awk`, `sort`, `uniq`, `head`, `tail`, `jq`, `date`, `mkdir -p`, `cp`, `mv` (within working dir)

**Requires user approval:**
- `Bash` with destructive patterns: `rm `, `rmdir`, `del `, `> /`, `sudo`, `chmod`, `mkfs`, `dd `, `curl` (any external URL), `wget`
- `Bash` with any absolute path outside the working dir

**Always blocked (no approval possible):**
- Any `Bash` command writing to `/etc/`, `/usr/`, `/root/`, `/var/`, `/bin/`, `/sbin/`
- Any tool not in the Phase 1 whitelist

### Updated `runner.ts` — Approval Gate Implementation

```typescript
import { createClient } from 'redis';

// Module-level Redis client (created once per process)
// Connection string from env var REDIS_URL
let _redis: ReturnType<typeof createClient> | null = null;
async function getRedis() {
  if (!_redis) {
    _redis = createClient({ url: process.env.REDIS_URL });
    await _redis.connect();
  }
  return _redis;
}

// Patterns that require user approval
const DESTRUCTIVE_PATTERNS = [
  'rm ', 'rmdir', 'del ', '> /', 'sudo', 'chmod', 'mkfs',
  'dd ', 'curl ', 'wget ', '/etc/', '/usr/', '/root/', '/var/',
];

function requiresApproval(toolName: string, toolInput: any): boolean {
  if (toolName !== 'Bash') return false;
  const cmd: string = (toolInput?.command ?? '').toLowerCase();
  return DESTRUCTIVE_PATTERNS.some(p => cmd.includes(p));
}

function describeApproval(toolName: string, toolInput: any): string {
  if (toolName === 'Bash') return `Run shell command: ${toolInput.command}`;
  return `${toolName}: ${JSON.stringify(toolInput)}`;
}

/**
 * Wait for a Redis key cowork:approval:<approvalId> to be SET.
 * Returns 'approve' or 'reject'. Times out after 5 minutes → auto-reject.
 */
async function waitForApproval(approvalId: string): Promise<'approve' | 'reject'> {
  const redis = await getRedis();
  const key = `cowork:approval:${approvalId}`;
  const deadline = Date.now() + 5 * 60 * 1000; // 5 minutes

  while (Date.now() < deadline) {
    const val = await redis.get(key);
    if (val === 'approve') return 'approve';
    if (val === 'reject')  return 'reject';
    await new Promise(r => setTimeout(r, 200)); // Poll every 200ms
  }

  // Timed out — auto-reject
  return 'reject';
}

// Inside runTask() options.hooks.preToolCall:
preToolCall: async (toolName: string, toolInput: any) => {
  await auditLog({ event: 'tool_call', taskId: params.taskId, userId: params.userId,
                   data: { tool: toolName, input: safeSerialize(toolInput) } });

  if (requiresApproval(toolName, toolInput)) {
    const approvalId = crypto.randomUUID();
    const description = describeApproval(toolName, toolInput);

    await auditLog({ event: 'approval_requested', taskId: params.taskId, userId: params.userId,
                     data: { approvalId, tool: toolName, description } });

    // Emit approval_required SSE event — Blazor shows the approval dialog
    params.onChunk({
      type: 'approval_required',
      approvalId,
      approvalToolName: toolName,
      approvalToolInput: toolInput,
      approvalDescription: description,
    });

    const decision = await waitForApproval(approvalId);

    await auditLog({ event: decision === 'approve' ? 'approval_granted' : 'approval_denied',
                     taskId: params.taskId, userId: params.userId,
                     data: { approvalId, decision } });

    params.onChunk({
      type: 'approval_resolved',
      approvalId,
      text: decision === 'approve' ? 'Approved — proceeding' : 'Denied — skipping',
    });

    return { action: decision === 'approve' ? 'allow' : 'block' };
  }

  return { action: 'allow' };
},
```

### New API Endpoint: `POST /tasks/:id/approve` and `POST /tasks/:id/reject`

Add to `routes/tasks.ts`:

```typescript
// POST /tasks/:id/approve — user approves a pending tool call
router.post('/:id/approve', async (req, res) => {
  const authed = req as AuthedRequest;
  const { id } = req.params;
  const { approvalId } = req.body as { approvalId: string };

  if (!approvalId) { res.status(400).json({ error: 'approvalId required' }); return; }

  const redis = await getRedis();
  // Set the approval key with 30s TTL (runner polls every 200ms, will see it quickly)
  await redis.set(`cowork:approval:${approvalId}`, 'approve', { EX: 30 });

  await auditLog({ event: 'approval_granted_via_api', taskId: id, userId: authed.userId,
                   data: { approvalId } });

  res.json({ ok: true });
});

// POST /tasks/:id/reject — user rejects a pending tool call
router.post('/:id/reject', async (req, res) => {
  const authed = req as AuthedRequest;
  const { id } = req.params;
  const { approvalId } = req.body as { approvalId: string };

  if (!approvalId) { res.status(400).json({ error: 'approvalId required' }); return; }

  const redis = await getRedis();
  await redis.set(`cowork:approval:${approvalId}`, 'reject', { EX: 30 });

  await auditLog({ event: 'approval_denied_via_api', taskId: id, userId: authed.userId,
                   data: { approvalId } });

  res.json({ ok: true });
});
```

### Blazor: `ApprovalDialog.razor` Component

When `TaskPage.razor` receives an `approval_required` SSE chunk, it renders `ApprovalDialog`:

```razor
@* Components/Shared/ApprovalDialog.razor *@

<div style="margin-top: 12px; border: 2px solid var(--color-warning); border-radius: var(--radius-md); padding: 16px; background: var(--color-warning-bg);">
    <div style="font-weight: var(--font-semibold); font-size: var(--text-sm); color: var(--color-text-primary); margin-bottom: 8px;">
        ⚠ Claude wants to perform an action — please review:
    </div>
    <div style="background: var(--color-bg-page); border: 1px solid var(--color-border); border-radius: var(--radius-sm); padding: 10px 12px; font-family: var(--font-mono); font-size: var(--text-sm); color: var(--color-text-primary); margin-bottom: 12px; word-break: break-all;">
        @Description
    </div>
    <div style="display: flex; gap: 8px;">
        <MudButton Variant="Variant.Filled" Color="Color.Warning"
                   Disabled="@_acting"
                   OnClick="HandleApprove"
                   Size="Size.Small">
            @(_acting ? "…" : "✓ Allow")
        </MudButton>
        <MudButton Variant="Variant.Outlined" Color="Color.Error"
                   Disabled="@_acting"
                   OnClick="HandleReject"
                   Size="Size.Small">
            Deny
        </MudButton>
    </div>
    @if (_error is not null)
    {
        <div style="margin-top: 8px; color: var(--color-error); font-size: var(--text-xs);">@_error</div>
    }
</div>

@code {
    [Parameter, EditorRequired] public string TaskId    { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string ApprovalId { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string Description { get; set; } = string.Empty;
    [Parameter] public EventCallback<bool> OnResolved { get; set; }

    [Inject] private AgentApiClient AgentApi { get; set; } = default!;

    private bool   _acting;
    private string? _error;

    private async Task HandleApprove() => await Resolve(approve: true);
    private async Task HandleReject()  => await Resolve(approve: false);

    private async Task Resolve(bool approve)
    {
        _acting = true;
        _error = null;
        try
        {
            await AgentApi.SendApprovalAsync(TaskId, ApprovalId, approve);
            await OnResolved.InvokeAsync(approve);
        }
        catch (Exception ex)
        {
            _error = $"Failed: {ex.Message}";
        }
        finally
        {
            _acting = false;
        }
    }
}
```

### `AgentApiClient.cs` — Add approval methods

```csharp
/// <summary>Send an approve/reject decision for a pending tool call.</summary>
public async Task SendApprovalAsync(string taskId, string approvalId, bool approve, CancellationToken ct = default)
{
    var client = CreateClient();
    var action = approve ? "approve" : "reject";
    var resp = await client.PostAsJsonAsync($"/tasks/{taskId}/{action}", new { approvalId }, ct);
    resp.EnsureSuccessStatusCode();
}
```

### `TaskPage.razor` — Handle Approval State

Add state for pending approval:

```csharp
// Approval gate state
private string? _pendingApprovalId;
private string? _pendingApprovalDescription;
private string? _pendingApprovalToolName;
```

Update `ProcessChunk()`:

```csharp
case "approval_required":
    _pendingApprovalId          = chunk.ApprovalId;
    _pendingApprovalDescription = chunk.ApprovalDescription ?? chunk.Text ?? chunk.ApprovalToolName;
    _pendingApprovalToolName    = chunk.ApprovalToolName;
    break;

case "approval_resolved":
    _pendingApprovalId          = null;
    _pendingApprovalDescription = null;
    if (chunk.Text is not null) _steps.Add(chunk.Text);
    break;
```

Update `SseChunk` record in `TaskPage.razor`:

```csharp
private record SseChunk(
    string Type,
    string? Text             = null,
    string? Base64           = null,
    string? FileName         = null,
    string? DownloadUrl      = null,
    string? OutputType       = null,
    long?   SizeBytes        = null,
    string? ApprovalId       = null,
    string? ApprovalToolName = null,
    string? ApprovalDescription = null
);
```

Add `ApprovalDialog` render in `TaskPage.razor`:

```razor
@if (_pendingApprovalId is not null)
{
    <ApprovalDialog TaskId="@TaskId"
                    ApprovalId="@_pendingApprovalId"
                    Description="@(_pendingApprovalDescription ?? "")"
                    OnResolved="HandleApprovalResolved" />
}
```

```csharp
private async Task HandleApprovalResolved(bool approved)
{
    _pendingApprovalId = null;
    _pendingApprovalDescription = null;
    // The task stream continues automatically once Node.js receives the decision
    // No local action needed — the SSE stream will emit approval_resolved next
    StateHasChanged();
}
```

---

## Feature 4: Redis

### What Goes in Redis

| Key pattern | Type | TTL | Content |
|-------------|------|-----|---------|
| `cowork:task:<taskId>` | Hash | 7 days | Task metadata (userId, status, prompt, createdAt, outputFiles JSON) |
| `cowork:approval:<approvalId>` | String | 30 seconds | `"approve"` or `"reject"` |
| `cowork:user:<userId>:tasks` | Sorted Set | 30 days | taskId members, score = createdAt unix timestamp |

**`cowork:task:<taskId>` hash fields:**

```
status       "running" | "completed" | "failed"
userId       <uuid>
userEmail    <email>
prompt       <text — truncated to 500 chars>
createdAt    <ISO 8601>
completedAt  <ISO 8601 | "">
outputFiles  <JSON array of OutputFile objects>
```

### `src/services/taskStore.ts` (new file)

Replaces the in-memory `taskStreams` Map in `routes/tasks.ts`.

```typescript
import { createClient } from 'redis';

const REDIS_URL = process.env.REDIS_URL;
if (!REDIS_URL) throw new Error('REDIS_URL env var required');

// Two separate Redis connections: one for regular ops, one for Pub/Sub subscribe
const redis   = createClient({ url: REDIS_URL });
const redisSub = createClient({ url: REDIS_URL });

await redis.connect();
await redisSub.connect();

export const TASK_TTL_SECONDS = 7 * 24 * 60 * 60; // 7 days

// ── Task metadata ────────────────────────────────────────────────────────────

export interface TaskMeta {
  status: 'running' | 'completed' | 'failed';
  userId: string;
  userEmail: string;
  prompt: string;
  createdAt: string;
  completedAt: string;
  outputFiles: string;  // JSON array of OutputFile
}

export async function createTaskMeta(taskId: string, meta: Omit<TaskMeta, 'status' | 'completedAt' | 'outputFiles'>): Promise<void> {
  const key = `cowork:task:${taskId}`;
  await redis.hSet(key, { ...meta, status: 'running', completedAt: '', outputFiles: '[]' });
  await redis.expire(key, TASK_TTL_SECONDS);
  await redis.zAdd(`cowork:user:${meta.userId}:tasks`, {
    score: Date.now(),
    value: taskId,
  });
  await redis.expire(`cowork:user:${meta.userId}:tasks`, 30 * 24 * 60 * 60);
}

export async function updateTaskComplete(taskId: string, outputFiles: object[]): Promise<void> {
  await redis.hSet(`cowork:task:${taskId}`, {
    status: 'completed',
    completedAt: new Date().toISOString(),
    outputFiles: JSON.stringify(outputFiles),
  });
}

export async function updateTaskFailed(taskId: string): Promise<void> {
  await redis.hSet(`cowork:task:${taskId}`, {
    status: 'failed',
    completedAt: new Date().toISOString(),
  });
}

export async function getTaskMeta(taskId: string): Promise<TaskMeta | null> {
  const data = await redis.hGetAll(`cowork:task:${taskId}`);
  if (!data || !data.status) return null;
  return data as unknown as TaskMeta;
}

export async function getUserTaskIds(userId: string, limit = 20): Promise<string[]> {
  // Sorted set: highest score (most recent) first
  return redis.zRange(`cowork:user:${userId}:tasks`, '+inf', '-inf', {
    BY: 'SCORE', REV: true, LIMIT: { offset: 0, count: limit }
  });
}

// ── SSE streaming via Redis Pub/Sub ─────────────────────────────────────────
// The task generator publishes chunks; the SSE endpoint subscribes.

export function taskChannel(taskId: string): string {
  return `cowork:stream:${taskId}`;
}

export async function publishChunk(taskId: string, chunk: object): Promise<void> {
  await redis.publish(taskChannel(taskId), JSON.stringify(chunk));
}

export async function* subscribeToTask(taskId: string): AsyncGenerator<object> {
  const channel = taskChannel(taskId);

  // Replay missed events from Redis List (in case SSE reconnects mid-task)
  const missed = await redis.lRange(`cowork:stream:log:${taskId}`, 0, -1);
  for (const raw of missed) yield JSON.parse(raw);

  // Subscribe to live events
  const chunks: object[] = [];
  let resolve: (() => void) | null = null;
  let done = false;

  await redisSub.subscribe(channel, (message) => {
    const chunk = JSON.parse(message);
    chunks.push(chunk);
    if (resolve) { resolve(); resolve = null; }
    // Terminal event: stop subscribing
    if ((chunk as any).type === 'result' || (chunk as any).type === 'error') {
      done = true;
    }
  });

  try {
    while (!done) {
      if (chunks.length > 0) { yield chunks.shift()!; }
      else { await new Promise<void>(r => { resolve = r; }); }
    }
    // Drain remaining
    while (chunks.length > 0) yield chunks.shift()!;
  } finally {
    await redisSub.unsubscribe(channel);
  }
}
```

**Replay log:** Before publishing each chunk, also append it to a Redis List `cowork:stream:log:<taskId>` (with TTL = 1 hour). This allows an SSE client that reconnects mid-task to replay missed events. After task completion, the list becomes irrelevant (the full output is in the task metadata).

### Updated `routes/tasks.ts` — Redis integration

**`POST /tasks`:**
```typescript
// Replace: taskStreams.set(taskId, generateChunks());
// With:
await createTaskMeta(taskId, { userId: authed.userId, userEmail: authed.userEmail, prompt, createdAt: new Date().toISOString() });

// Start task async — publishes chunks via Redis Pub/Sub
startTaskWithRedis(taskId, generateChunks).catch(console.error);
```

**`GET /tasks/:id/stream`:**
```typescript
// Replace: const gen = taskStreams.get(id);
// With:
const gen = subscribeToTask(id);
// ... rest of SSE loop unchanged
```

**`startTaskWithRedis()`:**
```typescript
async function startTaskWithRedis(taskId: string, genFactory: () => AsyncGenerator<SseChunk>): Promise<void> {
  const outputFiles: object[] = [];
  try {
    for await (const chunk of genFactory()) {
      // Append to replay log
      await redis.rPush(`cowork:stream:log:${taskId}`, JSON.stringify(chunk));
      await redis.expire(`cowork:stream:log:${taskId}`, 3600); // 1 hour TTL
      // Publish live
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
  }
}
```

### New API Endpoint: `GET /tasks?userId=<id>` (task list for history)

```typescript
router.get('/', async (req, res) => {
  const authed = req as AuthedRequest;
  const ids = await getUserTaskIds(authed.userId, 20);

  const tasks = await Promise.all(ids.map(async (id) => {
    const meta = await getTaskMeta(id);
    if (!meta) return null;
    return {
      taskId: id,
      status: meta.status,
      prompt: meta.prompt,
      createdAt: meta.createdAt,
      completedAt: meta.completedAt || null,
      outputFiles: JSON.parse(meta.outputFiles || '[]'),
    };
  }));

  res.json({ tasks: tasks.filter(Boolean) });
});
```

### Redis Infrastructure

New ElastiCache Redis cluster in the same VPC as ECS.

**Minimal spec:**
- Engine: Redis 7.x
- Node type: `cache.t4g.small` (1GB, ~$15/month — sufficient for Cowork task state)
- Replicas: 0 (single node for MVP — add replica in Phase 2)
- Auth: Redis AUTH token (stored in AWS Secrets Manager, injected as `REDIS_URL` env var)
- Encryption in transit: yes (`REDIS_URL = rediss://...` with TLS)
- Encryption at rest: yes

`REDIS_URL` format:
```
rediss://:<auth-token>@<cluster-endpoint>:6380
```

This is a DevOps task (Rhodey): provision the ElastiCache cluster, update the ECS task definition for both containers with `REDIS_URL`, set up security groups to allow CoworkAgent → ElastiCache traffic on port 6380.

---

## Feature 5: Task History

### `GET /tasks/history` Blazor page

```razor
@page "/tasks/history"
@inject AgentApiClient AgentApi
@inject CoworkSessionService Session
@inject NavigationManager Nav

<PageTitle>Task History — FAIT Cowork</PageTitle>

<div style="max-width: 800px; margin: 0 auto; padding: 32px 16px;">
    <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px;">
        <h1 style="font-size: var(--text-2xl); font-weight: var(--font-semibold); color: var(--color-text-primary); margin: 0;">
            Your Tasks
        </h1>
        <MudButton Variant="Variant.Filled"
                   OnClick='@(() => Nav.NavigateTo("/tasks/new"))'
                   Style="background: var(--color-btn-gold-bg); color: var(--color-btn-gold-text); font-weight: var(--font-semibold);">
            + New Task
        </MudButton>
    </div>

    @if (_loading)
    {
        <div style="color: var(--color-text-muted); font-size: var(--text-sm);">Loading…</div>
    }
    else if (_tasks.Count == 0)
    {
        <div style="text-align: center; padding: 48px 0; color: var(--color-text-muted);">
            <div style="font-size: 40px; margin-bottom: 8px;">📋</div>
            <div>No tasks yet. <a href="/tasks/new" style="color: var(--color-text-link);">Create your first task</a>.</div>
        </div>
    }
    else
    {
        <div style="display: flex; flex-direction: column; gap: 12px;">
            @foreach (var task in _tasks)
            {
                <div style="background: var(--color-surface); border: 1px solid var(--color-border); border-radius: var(--radius-lg); padding: 16px 20px; cursor: pointer;"
                     @onclick="@(() => OpenTask(task.TaskId))">
                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 16px;">
                        <div style="flex: 1; min-width: 0;">
                            <div style="font-size: var(--text-sm); font-weight: var(--font-medium); color: var(--color-text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
                                @task.Prompt
                            </div>
                            <div style="font-size: var(--text-xs); color: var(--color-text-muted); margin-top: 4px;">
                                @FormatTime(task.CreatedAt)
                                @if (task.OutputFiles.Count > 0)
                                {
                                    <span> · @task.OutputFiles.Count output@(task.OutputFiles.Count == 1 ? "" : "s")</span>
                                }
                            </div>
                        </div>
                        <div style="flex-shrink: 0;">
                            @StatusBadge(task.Status)
                        </div>
                    </div>
                </div>
            }
        </div>
    }
</div>

@code {
    private List<TaskSummary> _tasks = new();
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try { _tasks = await AgentApi.GetTaskHistoryAsync(); }
        catch { /* Non-fatal — show empty state */ }
        finally { _loading = false; }
    }

    private void OpenTask(string taskId) => Nav.NavigateTo($"/tasks/{taskId}");

    private string FormatTime(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        var local = dt.ToLocalTime();
        var diff = DateTime.Now - local;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalHours < 1)   return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays < 1)    return $"{(int)diff.TotalHours}h ago";
        return local.ToString("MMM d, h:mm tt");
    }

    private RenderFragment StatusBadge(string status) => builder =>
    {
        var (color, label) = status switch
        {
            "completed" => ("var(--color-success)", "Done"),
            "failed"    => ("var(--color-error)", "Failed"),
            _           => ("var(--color-warning)", "Running"),
        };
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "style", $"font-size:var(--text-xs);font-weight:var(--font-semibold);color:{color};background:{color.Replace(")", "-bg)")};padding:2px 8px;border-radius:var(--radius-full);");
        builder.AddText(2, label);
        builder.CloseElement();
    };
}
```

### `AgentApiClient.cs` — Add history method

```csharp
public record OutputFileSummary(string Name, string Type, string DownloadUrl);
public record TaskSummary(
    string TaskId, string Status, string Prompt,
    string CreatedAt, string? CompletedAt,
    List<OutputFileSummary> OutputFiles);

public async Task<List<TaskSummary>> GetTaskHistoryAsync(CancellationToken ct = default)
{
    var client = CreateClient();
    var resp = await client.GetAsync("/tasks", ct);
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadFromJsonAsync<TaskListResponse>(ct: ct);
    return body?.Tasks ?? new();
}

private record TaskListResponse(List<TaskSummary> Tasks);
```

### `TaskPage.razor` — History navigation

S2 adds a "← History" link at the top of `TaskPage.razor`:

```razor
<div style="margin-bottom: 16px;">
    <a href="/tasks/history" style="color: var(--color-text-link); font-size: var(--text-sm);">← Back to tasks</a>
</div>
```

When a user navigates to `/tasks/<id>` for a **completed task** (from history), the page must render the stored outputs rather than trying to re-stream. Logic:

```csharp
protected override async Task OnInitializedAsync()
{
    // Try to load existing task metadata first
    var meta = await AgentApi.GetTaskMetaAsync(TaskId);
    if (meta?.Status == "completed")
    {
        _done = true;
        foreach (var f in meta.OutputFiles)
            ProcessOutputFile(f);
        return; // Don't open SSE stream for completed tasks
    }

    // Task is running or unknown — open SSE stream
    _ = ConsumeStreamAsync(_cts.Token);
}
```

Add `AgentApiClient.GetTaskMetaAsync()`:

```csharp
public async Task<TaskSummary?> GetTaskMetaAsync(string taskId, CancellationToken ct = default)
{
    var client = CreateClient();
    var resp = await client.GetAsync($"/tasks/{taskId}", ct);
    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadFromJsonAsync<TaskSummary>(ct: ct);
}
```

New Node.js endpoint `GET /tasks/:id`:

```typescript
router.get('/:id', async (req, res) => {
  const authed = req as AuthedRequest;
  const { id } = req.params;
  const meta = await getTaskMeta(id);
  if (!meta || meta.userId !== authed.userId) { res.status(404).json({ error: 'Not found' }); return; }
  res.json({
    taskId: id,
    status: meta.status,
    prompt: meta.prompt,
    createdAt: meta.createdAt,
    completedAt: meta.completedAt || null,
    outputFiles: JSON.parse(meta.outputFiles || '[]'),
  });
});
```

### Main nav: add history link

`MainLayout.razor` — add a "My Tasks" link in the nav:

```razor
<nav style="display: flex; gap: 16px; padding: 0 16px; align-items: center; height: 40px; border-bottom: 1px solid var(--color-border); background: var(--color-surface);">
    <a href="/tasks/new" style="font-size: var(--text-sm); color: var(--color-text-secondary); text-decoration: none;">+ New Task</a>
    <a href="/tasks/history" style="font-size: var(--text-sm); color: var(--color-text-secondary); text-decoration: none;">My Tasks</a>
</nav>
```

---

## Files Changed Summary

### Modified: `fip/cowork/src/CoworkAgent/`

| File | Change |
|------|--------|
| `src/routes/tasks.ts` | Add Redis Pub/Sub streaming; add `GET /tasks`, `GET /tasks/:id`, `POST /tasks/:id/approve`, `POST /tasks/:id/reject`; switch to S3 uploads |
| `src/agent/runner.ts` | Approval gate with `waitForApproval()`; add FORGE context injection; add `onChunk` callback parameter; update `preToolCall` hook |
| `src/services/fileService.ts` | Replace local filesystem with S3 upload/download |
| `package.json` | Add `redis` (`^4.6.0`), `@aws-sdk/s3-request-presigner`, `@aws-sdk/client-s3` |

### New: `fip/cowork/src/CoworkAgent/src/services/`

| File | Purpose |
|------|---------|
| `taskStore.ts` | Redis-backed task metadata + Pub/Sub streaming + history queries |

### Modified: `fip/cowork/src/CoworkWeb/`

| File | Change |
|------|--------|
| `CoworkWeb.csproj` | Add `Markdig` package |
| `Services/AgentApiClient.cs` | Add `SendApprovalAsync`, `GetTaskHistoryAsync`, `GetTaskMetaAsync` |
| `Components/Pages/TaskPage.razor` | Approval gate state; updated `SseChunk` record; updated `ProcessChunk`; history nav link; completed-task detection |

### New: `fip/cowork/src/CoworkWeb/`

| File | Purpose |
|------|---------|
| `Components/Shared/ApprovalDialog.razor` | Approve/deny UI for pending tool calls |
| `Components/Shared/OutputPanel.razor` | Extracted from TaskPage; multi-type renderer |
| `Components/Pages/TaskHistory.razor` | Task list page |

### Environment Variables (additions)

**CoworkAgent:**
```
REDIS_URL=rediss://:<token>@<endpoint>:6380
COWORK_S3_BUCKET=fip-cowork-workspaces
AWS_REGION=us-east-1
```

**CoworkWeb:** No new env vars (uses AgentApiClient which calls Node.js).

**Infrastructure (Rhodey):**
- New ElastiCache Redis `cache.t4g.small` cluster
- New S3 bucket `fip-cowork-workspaces` (SSE-S3, no public access, 30-day lifecycle)
- ECS task IAM role: add `s3:GetObject`, `s3:PutObject`, `s3:ListObjectsV2` on `fip-cowork-workspaces`

**Total: 4 new files + 6 modified. No Blazor package changes except adding Markdig.**

---

## Acceptance Criteria

1. **Markdown output:** Agent creates a `.md` file → `OutputPanel` renders it as styled HTML using Markdig. Headers, bullet points, bold text, and tables all render correctly.

2. **CSV output:** Agent creates a `.csv` file → `OutputPanel` renders the first 100 rows as an HTML `<table>` with alternating row colors. A download link appears below the table.

3. **HTML output (S1 regression):** Agent creates a `.html` file → `OutputPanel` renders `<iframe srcdoc="...">` with `sandbox="allow-scripts"`. Regression from S1 confirmed working.

4. **S3 upload:** Input files uploaded on task creation go to `s3://fip-cowork-workspaces/tasks/<taskId>/input/`. Output files appear at `s3://fip-cowork-workspaces/tasks/<taskId>/output/`. Confirm in S3 console.

5. **Approval gate — destructive command:** Task prompt causes Claude to run a `rm` command → `ApprovalDialog` appears in the browser with the exact command text → user clicks "Deny" → task continues without executing the command. CloudWatch logs `approval_denied_via_api` event.

6. **Approval gate — approve:** Same scenario → user clicks "Allow" → the command executes → task continues. CloudWatch logs `approval_granted_via_api`.

7. **Approval gate — timeout:** No user action for 5 minutes → gate auto-rejects → task continues with a "Denied — skipping" step logged. (Test with a short timeout override in dev.)

8. **Task history:** Navigate to `/tasks/history` → list of past tasks appears with prompt preview, status badge, and relative time. Clicking a completed task navigates to `/tasks/<id>` and shows the stored outputs (no re-streaming).

9. **Redis persistence:** Restart the CoworkAgent container mid-test. SSE client reconnects and replays missed events from the Redis stream log. Task history survives the restart.

10. **User isolation in history:** Elise's history page shows only Elise's tasks. Lauren's shows only Lauren's. The Node.js `GET /tasks/:id` endpoint returns 404 for cross-user access.

---

## Constraints for CC

- `waitForApproval()` polls Redis every 200ms — do NOT reduce to <100ms (Redis rate limits). Do NOT increase to >500ms (UX suffers — approval dialog appears frozen).
- `subscribeToTask()` in `taskStore.ts` requires **two separate Redis connections** (one for regular commands, one for Pub/Sub subscribe). Redis clients in subscribe mode cannot execute regular commands. A single-connection approach will throw `"Connection in subscribe mode"` errors.
- `Markdig.Markdown.ToHtml(content, Pipeline)` — the pipeline must include `.UseAdvancedExtensions()` (matches FAIT's `MessageBubble.razor` line 90). Without this, tables and strikethrough won't render.
- CSV rendering is **server-side only** — do not use JavaScript CSV parsers. Blazor reads the base64-decoded content, splits on newlines, splits each line on commas, renders `<table>`. Cap at 100 rows (no `Skip`/`Take` needed — just `lines.Take(101)`).
- The replay log `cowork:stream:log:<taskId>` must have TTL = 1 hour (3600 seconds). Set TTL after every `rPush` call. Without TTL, the log accumulates indefinitely.
- `GET /tasks/:id` must check `meta.userId === authed.userId` before returning data. Cross-user task access is a security issue. Return 404 (not 403) to avoid leaking task existence.
- `REDIS_URL` must start with `rediss://` (double-s) for TLS. `redis://` (no TLS) must not be used in production.
- Do NOT touch any files in `fip/fait/`, `fip/firm/`, `fip/forms/`, or `fip/shared/FipShared/` except `FipModule.cs` (already changed in S1). S2 is Cowork-only changes.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify taskStore.ts uses TWO Redis clients — one for commands (redis)
          and one for subscriptions (redisSub). Check that subscribe() is called
          only on redisSub, and all other operations (hSet, get, set, zAdd,
          rPush, expire, publish) are called on redis. Calling subscribe()
          on the same client used for hSet will throw at runtime.

⚠️  HIGH: Verify GET /tasks/:id checks meta.userId === authed.userId.
          A missing ownership check leaks any user's task data to any
          authenticated Cowork user. Return 404, not 403.

⚠️  HIGH: Verify waitForApproval() sets a 5-minute deadline and auto-rejects
          on timeout. Without a timeout, a task that shows an approval dialog
          but the user navigates away will pause indefinitely, holding an
          Agent SDK context in memory until the container restarts.

⚠️  MEDIUM: Verify the replay log TTL is set on every rPush call (not just once
            at stream creation). If a task runs for 45 minutes, a TTL set only
            at creation may expire before the task completes.

⚠️  MEDIUM: Verify the Markdig Pipeline uses UseAdvancedExtensions(). Check
            OutputPanel.razor for the MarkdownPipeline builder call. Without
            this, tables in .md output won't render as <table> elements.

⚠️  MEDIUM: Verify REDIS_URL starts with "rediss://" (TLS) in the ECS task
            definition. Do not accept "redis://" in the prod environment.

⚠️  MEDIUM: Verify ApprovalDialog.OnResolved is called correctly. The Blazor
            EventCallback fires when the user approves or rejects. The TaskPage
            handler (HandleApprovalResolved) clears _pendingApprovalId.
            Confirm that the SSE stream is NOT re-opened or reset on approval —
            it should continue streaming automatically once Node.js receives
            the decision.

⚠️  LOW: Verify CSV rendering caps at 100 rows. An agent-generated CSV with
         10,000 rows would render 10,000 <tr> elements in Blazor and freeze
         the browser tab. Confirm lines.Take(101) (101 = 1 header + 100 data).
```

---

_Spec by Reed Richards | Cowork S2: 5 new files + 6 modified. Redis-backed task state, S3 file storage, approval gates, multi-type output rendering, task history._
