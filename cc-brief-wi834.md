# CC Brief: WI834 — FAIT Cowork Sprint 2

## Working Directory
`/home/fredw/projects/fip/cowork/`

## Rules
- Touch ONLY files inside `/home/fredw/projects/fip/cowork/`
- Do NOT touch fip/fait/, fip/firm/, fip/forms/, fip/shared/FipShared/
- Use two separate Redis clients in taskStore.ts (commands + subscribe)

---

## Task 1 — NEW: `src/CoworkAgent/src/services/taskStore.ts`

Create this file from scratch. It replaces the in-memory `taskStreams` Map.

```typescript
import { createClient } from 'redis';

const REDIS_URL = process.env.REDIS_URL;
if (!REDIS_URL) throw new Error('REDIS_URL env var required');
if (!REDIS_URL.startsWith('rediss://')) {
  console.warn('WARNING: REDIS_URL does not use TLS (rediss://)');
}

// ⚠️ CRITICAL: TWO separate Redis connections.
// redis    = commands (hSet, get, set, zAdd, rPush, expire, publish)
// redisSub = SUBSCRIBE ONLY — never call commands on this one
const redis    = createClient({ url: REDIS_URL });
const redisSub = createClient({ url: REDIS_URL });

await redis.connect();
await redisSub.connect();

export const TASK_TTL_SECONDS    = 7 * 24 * 60 * 60;   // 7 days
export const APPROVAL_TIMEOUT_MS = 5 * 60 * 1000;       // 5 minutes

// ── Task metadata ─────────────────────────────────────────────────────────

export interface TaskMeta {
  status:       'running' | 'completed' | 'failed';
  userId:       string;
  userEmail:    string;
  prompt:       string;
  createdAt:    string;
  completedAt:  string;
  outputFiles:  string; // JSON array
}

export async function createTaskMeta(
  taskId: string,
  meta: Omit<TaskMeta, 'status' | 'completedAt' | 'outputFiles'>
): Promise<void> {
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
    status:      'completed',
    completedAt: new Date().toISOString(),
    outputFiles: JSON.stringify(outputFiles),
  });
}

export async function updateTaskFailed(taskId: string): Promise<void> {
  await redis.hSet(`cowork:task:${taskId}`, {
    status:      'failed',
    completedAt: new Date().toISOString(),
  });
}

export async function getTaskMeta(taskId: string): Promise<TaskMeta | null> {
  const data = await redis.hGetAll(`cowork:task:${taskId}`);
  if (!data || !data.status) return null;
  return data as unknown as TaskMeta;
}

export async function getUserTaskIds(userId: string, limit = 20): Promise<string[]> {
  return redis.zRange(`cowork:user:${userId}:tasks`, '+inf', '-inf', {
    BY: 'SCORE', REV: true, LIMIT: { offset: 0, count: limit },
  });
}

// ── Approval gate ─────────────────────────────────────────────────────────
// Polls Redis every 200ms. Auto-rejects after APPROVAL_TIMEOUT_MS (5 min).

export async function waitForApproval(approvalId: string): Promise<'approve' | 'reject'> {
  const key      = `cowork:approval:${approvalId}`;
  const deadline = Date.now() + APPROVAL_TIMEOUT_MS;

  while (Date.now() < deadline) {
    const val = await redis.get(key);
    if (val === 'approve') return 'approve';
    if (val === 'reject')  return 'reject';
    await new Promise<void>(r => setTimeout(r, 200)); // 200ms poll — do NOT reduce below 100ms
  }

  return 'reject'; // timeout → auto-reject
}

export async function setApprovalDecision(approvalId: string, decision: 'approve' | 'reject'): Promise<void> {
  await redis.set(`cowork:approval:${approvalId}`, decision, { EX: 30 });
}

// ── SSE streaming via Redis Pub/Sub ───────────────────────────────────────

export function taskChannel(taskId: string): string {
  return `cowork:stream:${taskId}`;
}

export async function publishChunk(taskId: string, chunk: object): Promise<void> {
  const logKey = `cowork:stream:log:${taskId}`;
  // Append to replay log with TTL reset on every push
  await redis.rPush(logKey, JSON.stringify(chunk));
  await redis.expire(logKey, 3600); // 1-hour TTL reset on every push
  // Publish live to subscribers
  await redis.publish(taskChannel(taskId), JSON.stringify(chunk));
}

export async function* subscribeToTask(taskId: string): AsyncGenerator<object> {
  const channel = taskChannel(taskId);

  // Replay missed events (for SSE reconnects mid-task)
  const missed = await redis.lRange(`cowork:stream:log:${taskId}`, 0, -1);
  for (const raw of missed) yield JSON.parse(raw);

  const chunks: object[] = [];
  let resolve: (() => void) | null = null;
  let done = false;

  // ⚠️ subscribe() called ONLY on redisSub — never on redis
  await redisSub.subscribe(channel, (message) => {
    const chunk = JSON.parse(message);
    chunks.push(chunk);
    if (resolve) { resolve(); resolve = null; }
    const t = (chunk as any).type;
    if (t === 'result' || t === 'error') done = true;
  });

  try {
    while (!done) {
      if (chunks.length > 0) { yield chunks.shift()!; }
      else { await new Promise<void>(r => { resolve = r; }); }
    }
    while (chunks.length > 0) yield chunks.shift()!;
  } finally {
    await redisSub.unsubscribe(channel);
  }
}
```

---

## Task 2 — UPDATE: `src/CoworkAgent/src/routes/tasks.ts`

Replace the entire file. Key changes from Sprint 1:
- Remove in-memory `taskStreams` Map
- Add Redis integration via taskStore imports
- Add `GET /tasks` (history list)
- Add `GET /tasks/:id` (single task meta) — check `meta.userId !== authed.userId` → 404
- Add `POST /tasks/:id/approve` and `POST /tasks/:id/reject`
- Replace `fs.rename` with S3 upload (via fileService)
- Replace `taskStreams.get(id)` with `subscribeToTask(id)`

```typescript
import express from 'express';
import multer from 'multer';
import path from 'path';
import fs from 'fs/promises';
import { runTask } from '../agent/runner.js';
import type { AuthedRequest } from '../middleware/auth.js';
import {
  createTaskMeta, getTaskMeta, getUserTaskIds,
  updateTaskComplete, updateTaskFailed,
  publishChunk, subscribeToTask,
  setApprovalDecision,
} from '../services/taskStore.js';
import { uploadInputsToS3 } from '../services/fileService.js';

const router = express.Router();
const upload = multer({ dest: '/tmp/cowork-uploads/' });

export type OutputType = 'html' | 'markdown' | 'csv' | 'docx' | 'txt' | 'other';

export interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'file_output' | 'approval_required' | 'approval_resolved' | 'error';
  text?: string;
  toolName?: string;
  outputType?: OutputType;
  fileName?: string;
  downloadUrl?: string;
  base64?: string;
  sizeBytes?: number;
  approvalId?: string;
  approvalToolName?: string;
  approvalToolInput?: unknown;
  approvalDescription?: string;
}

// ── POST /tasks — create and start a new task ─────────────────────────────
router.post('/', upload.array('files', 5), async (req, res) => {
  const authed = req as AuthedRequest;
  const { prompt } = req.body as { prompt: string };

  if (!prompt?.trim()) {
    res.status(400).json({ error: 'prompt required' });
    return;
  }

  const taskId     = crypto.randomUUID();
  const workingDir = `/tmp/cowork-${taskId}`;
  await fs.mkdir(workingDir, { recursive: true });

  // Upload input files to S3 (replaces local fs.rename)
  const files = req.files as Express.Multer.File[] | undefined;
  if (files && files.length > 0) {
    await uploadInputsToS3(files, taskId);
  }

  // Create task metadata in Redis
  await createTaskMeta(taskId, {
    userId:    authed.userId,
    userEmail: authed.userEmail,
    prompt:    prompt.slice(0, 500),
    createdAt: new Date().toISOString(),
  });

  // Start task async — publishes chunks to Redis Pub/Sub
  startTaskWithRedis(taskId, workingDir, authed.userId, authed.userEmail, prompt).catch(console.error);

  res.json({ taskId });
});

async function startTaskWithRedis(
  taskId: string,
  workingDir: string,
  userId: string,
  userEmail: string,
  prompt: string
): Promise<void> {
  const outputFiles: object[] = [];
  try {
    const gen = runTask({
      taskId,
      userId,
      userEmail,
      prompt,
      workingDir,
      maxBudgetUsd: parseFloat(process.env.COWORK_MAX_BUDGET_USD ?? '0.50'),
      maxTurns:     parseInt(process.env.COWORK_MAX_TURNS ?? '30', 10),
    });

    for await (const chunk of gen) {
      await publishChunk(taskId, chunk);

      if (chunk.type === 'file_output' && chunk.fileName && chunk.downloadUrl) {
        outputFiles.push({
          name:        chunk.fileName,
          type:        chunk.outputType ?? 'other',
          downloadUrl: chunk.downloadUrl,
        });
      }
      if (chunk.type === 'result' || chunk.type === 'error') break;
    }
    await updateTaskComplete(taskId, outputFiles);
  } catch (e: any) {
    await publishChunk(taskId, { type: 'error', text: e.message });
    await updateTaskFailed(taskId);
  }
}

// ── GET /tasks — list user's task history ────────────────────────────────
router.get('/', async (req, res) => {
  const authed = req as AuthedRequest;
  const ids = await getUserTaskIds(authed.userId, 20);

  const tasks = await Promise.all(ids.map(async (id) => {
    const meta = await getTaskMeta(id);
    if (!meta) return null;
    return {
      taskId:       id,
      status:       meta.status,
      prompt:       meta.prompt,
      createdAt:    meta.createdAt,
      completedAt:  meta.completedAt || null,
      outputFiles:  JSON.parse(meta.outputFiles || '[]'),
    };
  }));

  res.json({ tasks: tasks.filter(Boolean) });
});

// ── GET /tasks/:id — get single task metadata ─────────────────────────────
router.get('/:id', async (req, res) => {
  const authed = req as AuthedRequest;
  const { id } = req.params;

  const meta = await getTaskMeta(id);

  // ⚠️ CRITICAL: return 404 (not 403) to avoid leaking task existence
  if (!meta || meta.userId !== authed.userId) {
    res.status(404).json({ error: 'Task not found' });
    return;
  }

  res.json({
    taskId:      id,
    status:      meta.status,
    prompt:      meta.prompt,
    createdAt:   meta.createdAt,
    completedAt: meta.completedAt || null,
    outputFiles: JSON.parse(meta.outputFiles || '[]'),
  });
});

// ── GET /tasks/:id/stream — SSE stream ────────────────────────────────────
router.get('/:id/stream', async (req, res) => {
  const { id } = req.params;

  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  let cancelled = false;
  req.on('close', () => { cancelled = true; });

  try {
    for await (const chunk of subscribeToTask(id)) {
      if (cancelled) break;
      res.write(`data: ${JSON.stringify(chunk)}\n\n`);

      const c = chunk as SseChunk;
      if (c.type === 'result' || c.type === 'error') break;
    }
  } catch (err: any) {
    res.write(`data: ${JSON.stringify({ type: 'error', text: err.message })}\n\n`);
  } finally {
    res.end();
  }
});

// ── POST /tasks/:id/approve — user approves a pending tool call ───────────
router.post('/:id/approve', async (req, res) => {
  const { id } = req.params;
  const { approvalId } = req.body as { approvalId: string };

  if (!approvalId) { res.status(400).json({ error: 'approvalId required' }); return; }

  await setApprovalDecision(approvalId, 'approve');
  res.json({ ok: true });
});

// ── POST /tasks/:id/reject — user rejects a pending tool call ────────────
router.post('/:id/reject', async (req, res) => {
  const { id } = req.params;
  const { approvalId } = req.body as { approvalId: string };

  if (!approvalId) { res.status(400).json({ error: 'approvalId required' }); return; }

  await setApprovalDecision(approvalId, 'reject');
  res.json({ ok: true });
});

export { router as tasksRouter };
```

---

## Task 3 — UPDATE: `src/CoworkAgent/src/agent/runner.ts`

Update runner.ts to:
1. Add approval gate via `waitForApproval` (imported from taskStore)
2. Update `SseChunk` interface to match new type (`file_output` replaces `html_output`, adds `approval_required`/`approval_resolved`)
3. Update `collectOutputFiles` to detect all output types (md, csv, html, docx, txt)
4. Use S3 upload for output files
5. Add `outputType` field to file_output chunks

Keep all existing logic (FORGE context, audit logging, system prompt) intact.

Replace runner.ts with this full content:

```typescript
import path from 'path';
import fs from 'fs/promises';
import crypto from 'crypto';
import { query } from '@anthropic-ai/claude-agent-sdk';
import type { SDKAssistantMessage, SDKResultSuccess } from '@anthropic-ai/claude-agent-sdk';
import { auditLog } from './audit.js';
import { queryForgeContext } from '../services/forgeClient.js';
import { waitForApproval } from '../services/taskStore.js';
import { uploadOutputToS3 } from '../services/fileService.js';

export type OutputType = 'html' | 'markdown' | 'csv' | 'docx' | 'txt' | 'other';

export interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'file_output' | 'approval_required' | 'approval_resolved' | 'error';
  text?: string;
  toolName?: string;
  outputType?: OutputType;
  fileName?: string;
  downloadUrl?: string;
  base64?: string;
  sizeBytes?: number;
  approvalId?: string;
  approvalToolName?: string;
  approvalToolInput?: unknown;
  approvalDescription?: string;
}

interface TaskParams {
  taskId:       string;
  userId:       string;
  userEmail:    string;
  prompt:       string;
  workingDir:   string;
  maxBudgetUsd: number;
  maxTurns:     number;
}

// Patterns that require user approval before execution
const DESTRUCTIVE_PATTERNS = [
  'rm ', 'rmdir', 'del ', '> /', 'sudo', 'chmod', 'mkfs',
  'dd ', 'curl ', 'wget ', '/etc/', '/usr/', '/root/', '/var/',
];

function requiresApproval(toolName: string, toolInput: unknown): boolean {
  if (toolName !== 'Bash') return false;
  const cmd = ((toolInput as any)?.command ?? '').toLowerCase() as string;
  return DESTRUCTIVE_PATTERNS.some(p => cmd.includes(p));
}

function describeApproval(toolName: string, toolInput: unknown): string {
  if (toolName === 'Bash') return `Run shell command: ${(toolInput as any)?.command ?? ''}`;
  return `${toolName}: ${JSON.stringify(toolInput)}`;
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

const SYSTEM_PROMPT = `You are FAIT Cowork — an AI assistant at Fortress Asset Management.
You complete business tasks for non-technical users: creating HTML prototypes, drafting documents,
summarizing files, and analyzing data.

Your working directory contains the user's uploaded files. You create output files there.
Explain each step as you work — users see your progress in real time.

Output guidelines by task type:
- Documents / reports: write a .md file (Markdown). Use headers, bullet points, tables.
- Data analysis: write a .md file for insights + optionally a .csv file for tabular data.
- HTML prototypes: write a .html file (self-contained, inline CSS, no CDN links).
- General text: write a .txt file if no other format is better.
- If creating multiple output files, name them clearly (e.g. report.md, data.csv).

When creating HTML, use inline CSS only (no external CDN links — the output must be self-contained).
When finished, explicitly state the name(s) of the output file(s) you created.

Data sovereignty: You run on Fortress AM's private AWS infrastructure. No data leaves Fortress AM.`;

export async function* runTask(params: TaskParams): AsyncGenerator<SseChunk> {
  await auditLog({ event: 'task_started', ...params });

  let forgeContext = '';
  try {
    forgeContext = await queryForgeContext(params.prompt, params.userId, params.userEmail);
  } catch {
    // Non-fatal — task runs without FORGE context if fetch fails
  }

  const systemPrompt = forgeContext
    ? `${SYSTEM_PROMPT}\n\n## Relevant Knowledge from FORGE\n${forgeContext}`
    : SYSTEM_PROMPT;

  // Closure to emit chunks from within the preToolCall hook
  const pendingChunks: SseChunk[] = [];
  let emitChunk: ((chunk: SseChunk) => void) = (chunk) => pendingChunks.push(chunk);

  try {
    for await (const message of query({
      prompt: params.prompt,
      options: {
        cwd: params.workingDir,
        allowedTools: ['Read', 'Write', 'Edit', 'Bash'],
        maxBudgetUsd: params.maxBudgetUsd,
        maxTurns:     params.maxTurns,
        systemPrompt,
        env: {
          COWORK_TASK_ID:    params.taskId,
          COWORK_USER_ID:    params.userId,
          COWORK_USER_EMAIL: params.userEmail,
        },
        hooks: {
          preToolCall: async (toolName: string, toolInput: unknown) => {
            await auditLog({
              event: 'tool_call',
              taskId:  params.taskId,
              userId:  params.userId,
              data:    { tool: toolName, input: safeSerialize(toolInput) },
            });

            if (requiresApproval(toolName, toolInput)) {
              const approvalId   = crypto.randomUUID();
              const description  = describeApproval(toolName, toolInput);

              await auditLog({
                event: 'approval_requested',
                taskId: params.taskId,
                userId: params.userId,
                data:   { approvalId, tool: toolName, description },
              });

              emitChunk({
                type: 'approval_required',
                approvalId,
                approvalToolName:  toolName,
                approvalToolInput: toolInput,
                approvalDescription: description,
              });

              const decision = await waitForApproval(approvalId);

              await auditLog({
                event: decision === 'approve' ? 'approval_granted' : 'approval_denied',
                taskId: params.taskId,
                userId: params.userId,
                data:   { approvalId, decision },
              });

              emitChunk({
                type:       'approval_resolved',
                approvalId,
                text:       decision === 'approve' ? 'Approved — proceeding' : 'Denied — skipping',
              });

              return { action: decision === 'approve' ? 'allow' : 'block' } as const;
            }

            return { action: 'allow' } as const;
          },
        },
      },
    })) {
      // Emit any chunks buffered during preToolCall
      while (pendingChunks.length > 0) yield pendingChunks.shift()!;

      if (message.type === 'result') {
        const resultMsg = message as SDKResultSuccess;

        const outputs = await collectOutputFiles(params.workingDir, params.taskId);
        for (const chunk of outputs) yield chunk;

        await auditLog({ event: 'task_completed', taskId: params.taskId, userId: params.userId });
        yield { type: 'result', text: resultMsg.result };
      } else if (message.type === 'assistant') {
        const assistantMsg = message as SDKAssistantMessage;
        for (const block of assistantMsg.message.content ?? []) {
          if (block.type === 'text' && block.text?.trim()) {
            yield { type: 'step', text: block.text };
          } else if (block.type === 'tool_use') {
            yield { type: 'tool_call', toolName: block.name, text: describeToolCall(block) };
          }
        }
      }
    }

    // Drain any remaining buffered chunks
    while (pendingChunks.length > 0) yield pendingChunks.shift()!;

  } catch (error: any) {
    await auditLog({
      event: 'task_failed',
      taskId: params.taskId,
      userId: params.userId,
      data:   { error: error.message },
    });
    yield { type: 'error', text: error.message ?? 'Task failed' };
  }
}

async function collectOutputFiles(workingDir: string, taskId: string): Promise<SseChunk[]> {
  const chunks: SseChunk[] = [];
  try {
    const entries = await fs.readdir(workingDir, { withFileTypes: true });

    for (const entry of entries) {
      if (!entry.isFile()) continue;

      const filePath  = path.join(workingDir, entry.name);
      const stat      = await fs.stat(filePath);
      const type      = detectOutputType(entry.name);

      // Upload to S3, get pre-signed download URL
      const downloadUrl = await uploadOutputToS3(filePath, taskId, entry.name);

      // Include base64 content for inline-renderable types (max 512 KB)
      let base64: string | undefined;
      if (['html', 'markdown', 'csv'].includes(type) && stat.size < 512 * 1024) {
        const content = await fs.readFile(filePath, 'utf-8');
        base64 = Buffer.from(content).toString('base64');
      }

      chunks.push({
        type:        'file_output',
        outputType:  type,
        fileName:    entry.name,
        downloadUrl,
        base64,
        sizeBytes:   stat.size,
      });
    }
  } catch { /* Non-fatal */ }
  return chunks;
}

function describeToolCall(block: { name: string; input?: Record<string, unknown> }): string {
  if (block.name === 'Read')  return `Reading ${block.input?.['file_path'] ?? 'file'}`;
  if (block.name === 'Write') return `Writing ${block.input?.['file_path'] ?? 'file'}`;
  if (block.name === 'Edit')  return `Editing ${block.input?.['file_path'] ?? 'file'}`;
  if (block.name === 'Bash')  return `Running: ${String(block.input?.['command'] ?? '').slice(0, 80)}`;
  return `Using ${block.name}`;
}

function safeSerialize(input: unknown): unknown {
  try { return JSON.parse(JSON.stringify(input)); }
  catch { return String(input); }
}
```

---

## Task 4 — NEW: `src/CoworkAgent/src/services/fileService.ts`

```typescript
import { S3Client, PutObjectCommand, GetObjectCommand, ListObjectsV2Command } from '@aws-sdk/client-s3';
import { getSignedUrl } from '@aws-sdk/s3-request-presigner';
import fs from 'fs/promises';
import path from 'path';

const s3     = new S3Client({ region: process.env.AWS_REGION ?? 'us-east-1' });
const BUCKET = process.env.COWORK_S3_BUCKET ?? 'fip-cowork-workspaces';
const PRESIGN_TTL_SECONDS = 900; // 15 minutes

/** Upload a local output file to S3 and return a pre-signed download URL. */
export async function uploadOutputToS3(
  localPath: string,
  taskId: string,
  fileName: string
): Promise<string> {
  const key  = `tasks/${taskId}/output/${fileName}`;
  const body = await fs.readFile(localPath);

  await s3.send(new PutObjectCommand({
    Bucket: BUCKET,
    Key:    key,
    Body:   body,
    ServerSideEncryption: 'AES256',
  }));

  return getSignedUrl(s3, new GetObjectCommand({ Bucket: BUCKET, Key: key }), {
    expiresIn: PRESIGN_TTL_SECONDS,
  });
}

/** Upload input files (from multer) to S3 and clean up temp files. */
export async function uploadInputsToS3(
  files: Express.Multer.File[],
  taskId: string
): Promise<void> {
  for (const file of files) {
    const key  = `tasks/${taskId}/input/${file.originalname}`;
    const body = await fs.readFile(file.path);
    await s3.send(new PutObjectCommand({
      Bucket: BUCKET,
      Key:    key,
      Body:   body,
      ServerSideEncryption: 'AES256',
    }));
    await fs.unlink(file.path); // Remove multer temp file
  }
}

/** Download all input files from S3 to the task working directory. */
export async function downloadInputsFromS3(taskId: string, workingDir: string): Promise<void> {
  const list = await s3.send(new ListObjectsV2Command({
    Bucket: BUCKET,
    Prefix: `tasks/${taskId}/input/`,
  }));

  for (const obj of list.Contents ?? []) {
    if (!obj.Key) continue;
    const resp     = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: obj.Key }));
    const fileName = path.basename(obj.Key);
    const localPath = path.join(workingDir, fileName);

    const body   = resp.Body as NodeJS.ReadableStream;
    const chunks: Buffer[] = [];
    for await (const chunk of body) chunks.push(Buffer.from(chunk));
    await fs.writeFile(localPath, Buffer.concat(chunks));
  }
}
```

---

## Task 5 — NOTE on s3Service.ts

The S3 logic has been consolidated into `fileService.ts` above (Tasks 4+5 merged). Do NOT create a separate s3Service.ts — keep all S3 logic in fileService.ts.

---

## Task 6 — UPDATE: `src/CoworkWeb/CoworkWeb.csproj`

Add Markdig package reference inside the existing `<ItemGroup>`:

```xml
<PackageReference Include="Markdig" Version="0.37.0" />
```

The final ItemGroup should look like:
```xml
<ItemGroup>
  <PackageReference Include="MudBlazor" Version="7.*" />
  <PackageReference Include="Microsoft.AspNetCore.Authentication.Cookies" Version="*" />
  <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="8.0.*" />
  <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="8.0.*" />
  <PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="*" />
  <PackageReference Include="Markdig" Version="0.37.0" />
  <ProjectReference Include="..\..\..\shared\FipShared\FipShared.csproj" />
</ItemGroup>
```

---

## Task 7 — UPDATE: `src/CoworkWeb/Services/AgentApiClient.cs`

Add these methods and records to the existing AgentApiClient class. Keep all existing code intact.

Add after the existing `OpenStreamAsync` method:

```csharp
// ── Approval ──────────────────────────────────────────────────────────────

/// <summary>Send an approve or reject decision for a pending tool call.</summary>
public async Task SendApprovalAsync(string taskId, string approvalId, bool approve, CancellationToken ct = default)
{
    var client = CreateClient();
    var action = approve ? "approve" : "reject";
    var resp   = await client.PostAsJsonAsync($"/tasks/{taskId}/{action}", new { approvalId }, ct);
    resp.EnsureSuccessStatusCode();
}

// ── Task history ──────────────────────────────────────────────────────────

/// <summary>Get task history for the current user (most recent first, up to 20).</summary>
public async Task<List<TaskSummary>> GetTaskHistoryAsync(CancellationToken ct = default)
{
    var client = CreateClient();
    var resp   = await client.GetAsync("/tasks", ct);
    resp.EnsureSuccessStatusCode();
    var body = await resp.Content.ReadFromJsonAsync<TaskListResponse>(ct: ct);
    return body?.Tasks ?? new List<TaskSummary>();
}

/// <summary>Get metadata for a single task (returns null if not found or not owned by user).</summary>
public async Task<TaskSummary?> GetTaskMetaAsync(string taskId, CancellationToken ct = default)
{
    var client = CreateClient();
    var resp   = await client.GetAsync($"/tasks/{taskId}", ct);
    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
    resp.EnsureSuccessStatusCode();
    return await resp.Content.ReadFromJsonAsync<TaskSummary>(ct: ct);
}

/// <summary>Cancel a running task (sends reject for any pending approval + signals cancellation).</summary>
public async Task CancelTaskAsync(string taskId, CancellationToken ct = default)
{
    var client = CreateClient();
    // Best-effort — ignore errors (task may already be done)
    try { await client.PostAsJsonAsync($"/tasks/{taskId}/cancel", new { }, ct); }
    catch { /* Non-fatal */ }
}
```

Add these records after the existing `StartTaskResponse` record:

```csharp
public record OutputFileSummary(string Name, string Type, string DownloadUrl);

public record TaskSummary(
    string TaskId,
    string Status,
    string Prompt,
    string CreatedAt,
    string? CompletedAt,
    List<OutputFileSummary> OutputFiles);

private record TaskListResponse(List<TaskSummary> Tasks);
```

---

## Task 8 — UPDATE: `src/CoworkWeb/Components/Pages/TaskPage.razor`

Replace the entire TaskPage.razor with this updated version that adds:
- Approval gate state (`_pendingApprovalId`, `_pendingApprovalDescription`)
- Updated `SseChunk` record with all new fields
- History nav link at top
- `ApprovalDialog` render when pending
- Completed-task detection (load from meta instead of re-streaming)
- Delegate output rendering to new `OutputPanel` component

```razor
@page "/tasks/{TaskId}"
@inject AgentApiClient AgentApi
@inject CoworkSessionService Session
@implements IAsyncDisposable

<PageTitle>Task — FAIT Cowork</PageTitle>

<div style="max-width: 800px; margin: 0 auto; padding: 32px 16px; font-family: var(--font-primary);">

    @* History nav *@
    <div style="margin-bottom: 16px;">
        <a href="/tasks/history" style="color: var(--color-text-link); font-size: var(--text-sm);">← Back to tasks</a>
    </div>

    <div style="display:flex; align-items:center; gap:10px; margin-bottom:24px;">
        <div style="width:10px;height:10px;border-radius:50%;background:@(_done ? "var(--color-success)" : "var(--color-gold)");"></div>
        <span style="font-size:var(--text-sm);color:var(--color-text-secondary);">
            @(_done ? "Completed" : "In progress…")
        </span>
    </div>

    <div style="background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-lg);padding:20px;margin-bottom:24px;">
        <h2 style="font-size:var(--text-lg);font-weight:var(--font-semibold);color:var(--color-text-primary);margin:0 0 16px 0;">
            Task Progress
        </h2>
        @if (_steps.Count == 0 && !_done)
        {
            <div style="color:var(--color-text-muted);font-size:var(--text-sm);">Starting…</div>
        }
        <div style="display:flex;flex-direction:column;gap:8px;">
            @{int stepNum = 1;}
            @foreach (var step in _steps)
            {
                <div style="display:flex;gap:10px;align-items:flex-start;">
                    <span style="flex-shrink:0;width:20px;height:20px;border-radius:50%;background:var(--color-primary);color:#fff;font-size:11px;font-weight:var(--font-bold);display:flex;align-items:center;justify-content:center;">
                        @(stepNum++)
                    </span>
                    <span style="font-size:var(--text-sm);color:var(--color-text-primary);line-height:1.5;">@step</span>
                </div>
            }
        </div>
        @if (!_done)
        {
            <div style="margin-top:12px;color:var(--color-text-muted);font-size:var(--text-sm);">
                <span style="animation:pulse 1.5s infinite;">●</span> Claude is working…
            </div>
        }

        @* Approval dialog — rendered inline in the progress panel *@
        @if (_pendingApprovalId is not null)
        {
            <ApprovalDialog TaskId="@TaskId"
                            ApprovalId="@_pendingApprovalId"
                            Description="@(_pendingApprovalDescription ?? "")"
                            OnResolved="HandleApprovalResolved" />
        }
    </div>

    @if (_outputFiles.Count > 0)
    {
        <div style="background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-lg);padding:20px;">
            <h2 style="font-size:var(--text-lg);font-weight:var(--font-semibold);color:var(--color-text-primary);margin:0 0 16px 0;">
                Output
            </h2>
            @foreach (var f in _outputFiles)
            {
                <OutputPanel File="@f" />
            }
        </div>
    }

    @if (_outputText is not null && _outputFiles.Count == 0)
    {
        <div style="background:var(--color-surface);border:1px solid var(--color-border);border-radius:var(--radius-lg);padding:20px;">
            <h2 style="font-size:var(--text-lg);font-weight:var(--font-semibold);color:var(--color-text-primary);margin:0 0 16px 0;">
                Result
            </h2>
            <div style="background:var(--color-bg-page);border:1px solid var(--color-border);border-radius:var(--radius-md);padding:16px;">
                <pre style="white-space:pre-wrap;font-family:var(--font-primary);font-size:var(--text-sm);color:var(--color-text-primary);margin:0;line-height:1.6;">@_outputText</pre>
            </div>
        </div>
    }

    @if (_error is not null)
    {
        <div style="padding:12px 16px;background:var(--color-error-bg);border:1px solid var(--color-error);border-radius:var(--radius-md);color:var(--color-error);font-size:var(--text-sm);">
            ⚠ @_error
        </div>
    }
</div>

@code {
    [Parameter] public string TaskId { get; set; } = string.Empty;

    private List<string>       _steps       = new();
    private bool               _done;
    private string?            _outputText;
    private List<OutputFile>   _outputFiles = new();
    private string?            _error;
    private CancellationTokenSource _cts    = new();

    // Approval gate state
    private string? _pendingApprovalId;
    private string? _pendingApprovalDescription;

    protected override async Task OnInitializedAsync()
    {
        // Check if task is already completed — load from metadata, skip stream
        try
        {
            var meta = await AgentApi.GetTaskMetaAsync(TaskId);
            if (meta?.Status == "completed")
            {
                _done = true;
                foreach (var f in meta.OutputFiles)
                    _outputFiles.Add(new OutputFile(f.Name, f.Type, null, f.DownloadUrl, 0));
                return;
            }
        }
        catch { /* Non-fatal — fall through to stream */ }

        // Task is running or unknown — open SSE stream
        _ = ConsumeStreamAsync(_cts.Token);
    }

    private async Task ConsumeStreamAsync(CancellationToken ct)
    {
        try
        {
            using var stream = await AgentApi.OpenStreamAsync(TaskId, ct);
            using var reader = new System.IO.StreamReader(stream);

            while (!reader.EndOfStream && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (!line.StartsWith("data: ")) continue;

                var json  = line["data: ".Length..];
                var chunk = System.Text.Json.JsonSerializer.Deserialize<SseChunk>(json,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (chunk is null) continue;

                await InvokeAsync(() => { ProcessChunk(chunk); StateHasChanged(); });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await InvokeAsync(() =>
            {
                _error = $"Stream error: {ex.Message}";
                _done  = true;
                StateHasChanged();
            });
        }
    }

    private void ProcessChunk(SseChunk chunk)
    {
        switch (chunk.Type)
        {
            case "step":
            case "tool_call":
                if (!string.IsNullOrWhiteSpace(chunk.Text))
                    _steps.Add(chunk.Text);
                break;

            case "approval_required":
                _pendingApprovalId          = chunk.ApprovalId;
                _pendingApprovalDescription = chunk.ApprovalDescription ?? chunk.Text ?? chunk.ApprovalToolName;
                break;

            case "approval_resolved":
                _pendingApprovalId          = null;
                _pendingApprovalDescription = null;
                if (!string.IsNullOrWhiteSpace(chunk.Text))
                    _steps.Add(chunk.Text);
                break;

            case "file_output":
                if (chunk.FileName is not null && chunk.DownloadUrl is not null)
                    _outputFiles.Add(new OutputFile(
                        chunk.FileName,
                        chunk.OutputType ?? "other",
                        chunk.Base64,
                        chunk.DownloadUrl,
                        chunk.SizeBytes ?? 0));
                break;

            case "result":
                _done       = true;
                _outputText = chunk.Text;
                break;

            case "error":
                _done   = true;
                _error  = chunk.Text ?? "Task failed";
                break;
        }
    }

    private Task HandleApprovalResolved(bool approved)
    {
        _pendingApprovalId          = null;
        _pendingApprovalDescription = null;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _cts.Dispose();
    }

    // ── Records ──────────────────────────────────────────────────────────

    public record OutputFile(
        string  Name,
        string  Type,
        string? Base64,
        string  DownloadUrl,
        long    SizeBytes);

    private record SseChunk(
        string  Type,
        string? Text                = null,
        string? Base64              = null,
        string? FileName            = null,
        string? DownloadUrl         = null,
        string? OutputType          = null,
        long?   SizeBytes           = null,
        string? ApprovalId          = null,
        string? ApprovalToolName    = null,
        string? ApprovalDescription = null
    );
}
```

---

## Task 9 — NEW: `src/CoworkWeb/Components/Shared/OutputPanel.razor`

Create new file. Renders a single output file based on its type.
- markdown → Markdig HTML (UseAdvancedExtensions)
- csv → server-side table, 100-row cap (Take(101) = 1 header + 100 data)
- html → iframe srcdoc sandbox
- docx → download link only
- txt / other → download link only

```razor
@using Markdig
@inject Microsoft.AspNetCore.Components.NavigationManager Nav

<div style="margin-bottom: 20px;">
    @if (File.Type == "markdown")
    {
        <div style="margin-bottom: 8px;">
            <span style="font-size: var(--text-xs); font-weight: var(--font-medium); color: var(--color-text-muted); text-transform: uppercase; letter-spacing: 0.05em;">Markdown Preview — @File.Name</span>
        </div>
        <div class="cowork-markdown" style="background: var(--color-bg-page); border: 1px solid var(--color-border); border-radius: var(--radius-md); padding: 20px; font-size: var(--text-sm); color: var(--color-text-primary); line-height: 1.7;">
            @((MarkupString)RenderMarkdown())
        </div>
        <div style="margin-top: 8px;">
            <a href="@File.DownloadUrl" style="color: var(--color-text-link); font-size: var(--text-xs);">⬇ Download @File.Name</a>
        </div>
    }
    else if (File.Type == "csv")
    {
        <div style="margin-bottom: 8px;">
            <span style="font-size: var(--text-xs); font-weight: var(--font-medium); color: var(--color-text-muted); text-transform: uppercase; letter-spacing: 0.05em;">CSV Preview — @File.Name (first 100 rows)</span>
        </div>
        <div style="overflow-x: auto; border: 1px solid var(--color-border); border-radius: var(--radius-md);">
            <table style="width: 100%; border-collapse: collapse; font-size: var(--text-xs); color: var(--color-text-primary);">
                @{
                    var csvRows = BuildCsvRows();
                    bool isHeader = true;
                }
                @foreach (var row in csvRows)
                {
                    @if (isHeader)
                    {
                        <thead>
                            <tr style="background: var(--color-surface);">
                                @foreach (var cell in row)
                                {
                                    <th style="padding: 8px 12px; text-align: left; border-bottom: 2px solid var(--color-border); font-weight: var(--font-semibold); white-space: nowrap;">@cell</th>
                                }
                            </tr>
                        </thead>
                        <tbody>
                        isHeader = false;
                    }
                    else
                    {
                        <tr style="border-bottom: 1px solid var(--color-border);">
                            @foreach (var cell in row)
                            {
                                <td style="padding: 6px 12px; color: var(--color-text-secondary);">@cell</td>
                            }
                        </tr>
                    }
                }
                </tbody>
            </table>
        </div>
        <div style="margin-top: 8px;">
            <a href="@File.DownloadUrl" style="color: var(--color-text-link); font-size: var(--text-xs);">⬇ Download @File.Name</a>
        </div>
    }
    else if (File.Type == "html")
    {
        <div style="margin-bottom: 8px;">
            <span style="font-size: var(--text-xs); font-weight: var(--font-medium); color: var(--color-text-muted); text-transform: uppercase; letter-spacing: 0.05em;">HTML Preview — @File.Name</span>
        </div>
        <iframe srcdoc="@HtmlContent"
                sandbox="allow-scripts"
                style="width: 100%; height: 420px; border: 1px solid var(--color-border); border-radius: var(--radius-md);"
                title="@File.Name">
        </iframe>
        <div style="margin-top: 8px;">
            <a href="@File.DownloadUrl" style="color: var(--color-text-link); font-size: var(--text-xs);">⬇ Download @File.Name</a>
        </div>
    }
    else
    {
        @* docx, txt, other — download link only *@
        <div style="display: flex; align-items: center; gap: 8px; padding: 12px 16px; background: var(--color-bg-page); border: 1px solid var(--color-border); border-radius: var(--radius-md);">
            <span style="font-size: 18px;">@FileIcon()</span>
            <div style="flex: 1; min-width: 0;">
                <div style="font-size: var(--text-sm); font-weight: var(--font-medium); color: var(--color-text-primary); white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">@File.Name</div>
                @if (File.SizeBytes > 0)
                {
                    <div style="font-size: var(--text-xs); color: var(--color-text-muted);">@FormatSize(File.SizeBytes)</div>
                }
            </div>
            <a href="@File.DownloadUrl"
               style="color: var(--color-text-link); font-size: var(--text-sm); font-weight: var(--font-medium); white-space: nowrap; text-decoration: none;">
                ⬇ Download
            </a>
        </div>
    }
</div>

@code {
    [Parameter, EditorRequired]
    public TaskPage.OutputFile File { get; set; } = default!;

    private string? _decodedContent;

    private string DecodedContent => _decodedContent ??= File.Base64 is not null
        ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(File.Base64))
        : string.Empty;

    private string HtmlContent => File.Base64 is not null ? DecodedContent : string.Empty;

    private string RenderMarkdown()
    {
        if (string.IsNullOrEmpty(DecodedContent)) return string.Empty;
        var pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();
        return Markdown.ToHtml(DecodedContent, pipeline);
    }

    private List<List<string>> BuildCsvRows()
    {
        if (string.IsNullOrEmpty(DecodedContent)) return new();

        var lines = DecodedContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var rows  = lines.Take(101).ToList(); // 1 header + 100 data rows cap

        return rows.Select(line =>
            line.Split(',').Select(cell => cell.Trim().Trim('"')).ToList()
        ).ToList();
    }

    private string FileIcon() => File.Type switch
    {
        "docx" => "📄",
        "txt"  => "📝",
        _      => "📎",
    };

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024)          return $"{bytes} B";
        if (bytes < 1024 * 1024)   return $"{bytes / 1024:F1} KB";
        return $"{bytes / (1024 * 1024):F1} MB";
    }
}
```

---

## Task 10 — NEW: `src/CoworkWeb/Components/Shared/ApprovalDialog.razor`

```razor
@* Components/Shared/ApprovalDialog.razor — approve/reject UI for pending tool calls *@

<div style="margin-top: 12px; border: 2px solid #d97706; border-radius: var(--radius-md); padding: 16px; background: #fffbeb;">
    <div style="font-weight: var(--font-semibold); font-size: var(--text-sm); color: var(--color-text-primary); margin-bottom: 8px;">
        ⚠ Claude wants to perform an action — please review:
    </div>
    <div style="background: var(--color-bg-page); border: 1px solid var(--color-border); border-radius: var(--radius-sm); padding: 10px 12px; font-family: var(--font-mono); font-size: var(--text-sm); color: var(--color-text-primary); margin-bottom: 12px; word-break: break-all;">
        @Description
    </div>
    <div style="display: flex; gap: 8px;">
        <MudButton Variant="Variant.Filled"
                   Color="Color.Warning"
                   Disabled="@_acting"
                   OnClick="HandleApprove"
                   Size="Size.Small">
            @(_acting ? "…" : "✓ Allow")
        </MudButton>
        <MudButton Variant="Variant.Outlined"
                   Color="Color.Error"
                   Disabled="@_acting"
                   OnClick="HandleReject"
                   Size="Size.Small">
            Deny
        </MudButton>
    </div>
    @if (_error is not null)
    {
        <div style="margin-top: 8px; color: var(--color-error, #dc2626); font-size: var(--text-xs);">@_error</div>
    }
</div>

@code {
    [Parameter, EditorRequired] public string TaskId     { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string ApprovalId { get; set; } = string.Empty;
    [Parameter, EditorRequired] public string Description { get; set; } = string.Empty;
    [Parameter] public EventCallback<bool> OnResolved    { get; set; }

    [Inject] private AgentApiClient AgentApi { get; set; } = default!;

    private bool    _acting;
    private string? _error;

    private Task HandleApprove() => Resolve(approve: true);
    private Task HandleReject()  => Resolve(approve: false);

    private async Task Resolve(bool approve)
    {
        _acting = true;
        _error  = null;
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

---

## Task 11 — NEW: `src/CoworkWeb/Components/Pages/TaskHistory.razor`

```razor
@page "/tasks/history"
@inject AgentApiClient AgentApi
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
                     @onclick="@(() => Nav.NavigateTo($"/tasks/{task.TaskId}"))">
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
    private List<TaskSummary> _tasks   = new();
    private bool              _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try   { _tasks = await AgentApi.GetTaskHistoryAsync(); }
        catch { /* Non-fatal — show empty state */ }
        finally { _loading = false; }
    }

    private string FormatTime(string iso)
    {
        if (!DateTime.TryParse(iso, out var dt)) return iso;
        var local = dt.ToLocalTime();
        var diff  = DateTime.Now - local;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalHours   < 1) return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalDays    < 1) return $"{(int)diff.TotalHours}h ago";
        return local.ToString("MMM d, h:mm tt");
    }

    private RenderFragment StatusBadge(string status) => builder =>
    {
        var (color, label) = status switch
        {
            "completed" => ("#16a34a", "Done"),
            "failed"    => ("#dc2626", "Failed"),
            _           => ("#d97706", "Running"),
        };
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "style",
            $"font-size:var(--text-xs);font-weight:var(--font-semibold);color:{color};padding:2px 8px;border-radius:var(--radius-full);border:1px solid {color};");
        builder.AddText(2, label);
        builder.CloseElement();
    };
}
```

---

## Task 12 — UPDATE: `src/CoworkWeb/Components/Layout/MainLayout.razor`

Add a secondary nav bar with "My Tasks" link. Insert it immediately after `<FipNavBar ... />` and before `<main ...>`:

```razor
<nav style="display: flex; gap: 16px; padding: 0 16px; align-items: center; height: 40px; border-bottom: 1px solid var(--color-border); background: var(--color-surface);">
    <a href="/tasks/new"     style="font-size: var(--text-sm); color: var(--color-text-secondary); text-decoration: none;">+ New Task</a>
    <a href="/tasks/history" style="font-size: var(--text-sm); color: var(--color-text-secondary); text-decoration: none;">My Tasks</a>
</nav>
```

---

## package.json — Add Redis + AWS SDK dependencies

Update `/home/fredw/projects/fip/cowork/src/CoworkAgent/package.json` to add:
```json
"redis": "^4.6.0",
"@aws-sdk/client-s3": "^3.750.0",
"@aws-sdk/s3-request-presigner": "^3.750.0"
```

Keep all existing dependencies.

---

## Summary of all files to create/modify

CREATE:
1. src/CoworkAgent/src/services/taskStore.ts
2. src/CoworkAgent/src/services/fileService.ts
3. src/CoworkWeb/Components/Shared/OutputPanel.razor
4. src/CoworkWeb/Components/Shared/ApprovalDialog.razor
5. src/CoworkWeb/Components/Pages/TaskHistory.razor

MODIFY:
6. src/CoworkAgent/src/agent/runner.ts
7. src/CoworkAgent/src/routes/tasks.ts
8. src/CoworkAgent/package.json
9. src/CoworkWeb/CoworkWeb.csproj
10. src/CoworkWeb/Services/AgentApiClient.cs
11. src/CoworkWeb/Components/Pages/TaskPage.razor
12. src/CoworkWeb/Components/Layout/MainLayout.razor
