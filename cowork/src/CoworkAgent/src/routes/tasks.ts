import express from 'express';
import multer from 'multer';
import path from 'path';
import fs from 'fs/promises';
import { runTask } from '../agent/runner.js';
import type { AuthedRequest } from '../middleware/auth.js';

const router = express.Router();
const upload = multer({ dest: '/tmp/cowork-uploads/' });

interface SseChunk {
  type: 'step' | 'tool_call' | 'result' | 'html_output' | 'file_output' | 'error';
  text?: string;
  toolName?: string;
  base64?: string;
  fileName?: string;
  downloadUrl?: string;
}

// In-memory task store (Sprint 1; replaced with Redis in Sprint 2)
const taskStreams = new Map<string, AsyncGenerator<SseChunk>>();

// POST /tasks — create and start a new task
router.post('/', upload.array('files', 5), async (req, res) => {
  const authed = req as AuthedRequest;
  const { prompt } = req.body as { prompt: string };

  if (!prompt?.trim()) {
    res.status(400).json({ error: 'prompt required' });
    return;
  }

  const taskId = crypto.randomUUID();
  const workingDir = `/tmp/cowork-${taskId}`;
  await fs.mkdir(workingDir, { recursive: true });

  const files = req.files as Express.Multer.File[] | undefined;
  if (files) {
    for (const file of files) {
      const dest = path.join(workingDir, file.originalname);
      await fs.rename(file.path, dest);
    }
  }

  async function* generateChunks(): AsyncGenerator<SseChunk> {
    yield* runTask({
      taskId,
      userId:      authed.userId,
      userEmail:   authed.userEmail,
      prompt,
      workingDir,
      maxBudgetUsd: parseFloat(process.env.COWORK_MAX_BUDGET_USD ?? '0.50'),
      maxTurns:     parseInt(process.env.COWORK_MAX_TURNS ?? '30', 10),
    });
  }

  taskStreams.set(taskId, generateChunks());
  res.json({ taskId });
});

// GET /tasks/:id/stream — SSE stream
router.get('/:id/stream', async (req, res) => {
  const { id } = req.params;
  const gen = taskStreams.get(id);

  if (!gen) {
    res.status(404).json({ error: 'Task not found' });
    return;
  }

  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  try {
    for await (const chunk of gen) {
      res.write(`data: ${JSON.stringify(chunk)}\n\n`);

      if (chunk.type === 'result' || chunk.type === 'error') {
        taskStreams.delete(id);
        break;
      }
    }
  } catch (err: any) {
    res.write(`data: ${JSON.stringify({ type: 'error', text: err.message })}\n\n`);
  } finally {
    res.end();
  }
});

export { router as tasksRouter };
