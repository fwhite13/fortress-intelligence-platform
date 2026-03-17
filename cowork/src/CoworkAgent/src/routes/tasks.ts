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
import { tryStartTask, onTaskFinished, cancelTask, getQueuePosition } from '../services/taskQueue.js';
import { uploadInputsToS3 } from '../services/fileService.js';

const router = express.Router();
const upload = multer({ dest: '/tmp/cowork-uploads/' });

export type OutputType = 'html' | 'markdown' | 'csv' | 'docx' | 'txt' | 'other';

export interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'file_output' | 'approval_required' | 'approval_resolved' | 'error' | 'queued';
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
  position?: number; // queue position for 'queued' type
}

// ── POST /tasks — create and start a new task ─────────────────────────────
router.post('/', upload.array('files', 5), async (req, res) => {
  const authed = req as unknown as AuthedRequest;
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

  // Atomically check concurrency limit and either start or queue
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

// ── GET /tasks — list user's task history ────────────────────────────────
router.get('/', async (req, res) => {
  const authed = req as unknown as AuthedRequest;
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
  const authed = req as unknown as AuthedRequest;
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

// ── DELETE /tasks/:id — cancel a task ────────────────────────────────────
router.delete('/:id', async (req, res) => {
  const authed = req as unknown as AuthedRequest;
  const { id } = req.params;
  await cancelTask(id, authed.userId);
  res.json({ ok: true });
});

export { router as tasksRouter };
