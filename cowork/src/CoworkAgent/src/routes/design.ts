import express from 'express';
import multer from 'multer';
import crypto from 'crypto';
import { runDesignTask } from '../agents/design/runner.js';
import { saveBrandContext, getBrandContext } from '../services/brandService.js';
import { getRedis } from '../services/taskStore.js';
import { createTaskMeta, updateTaskComplete, updateTaskFailed,
         publishChunk, subscribeToTask } from '../services/taskStore.js';
import { uploadInputsToS3 } from '../services/fileService.js';
import type { AuthedRequest } from '../middleware/auth.js';

const router = express.Router();
const upload = multer({ dest: '/tmp/cowork-uploads/', limits: { fileSize: 10 * 1024 * 1024 } });

// ── POST /agents/design/projects/:projectId/screens ───────────────────────
// Generate a new screen (or 3 variants)
router.post(
  '/projects/:projectId/screens',
  upload.array('refs', 3),
  async (req, res) => {
    const authed    = req as unknown as AuthedRequest;
    const { projectId } = req.params;
    const {
      prompt, deviceTarget = 'responsive',
      variantCount = '1', convertToBlazor = 'false', orgId,
    } = req.body as Record<string, string>;

    if (!prompt?.trim()) { res.status(400).json({ error: 'prompt required' }); return; }

    const taskId   = crypto.randomUUID();
    const screenId = crypto.randomUUID();

    // Upload reference images to S3 if attached
    const files = req.files as Express.Multer.File[] | undefined;
    if (files?.length) await uploadInputsToS3(files, taskId);

    await createTaskMeta(taskId, {
      userId:    authed.userId,
      userEmail: authed.userEmail,
      prompt,
      createdAt: new Date().toISOString(),
    });

    res.json({ taskId, screenId });

    // Run async
    (async () => {
      try {
        await runDesignTask(
          { taskId, userId: authed.userId, userEmail: authed.userEmail,
            orgId: orgId ?? 'fortress-am', projectId, screenId,
            prompt, deviceTarget: deviceTarget as any,
            variantCount: Math.min(parseInt(variantCount, 10), 3) as 1 | 2 | 3,
            convertToBlazor: convertToBlazor === 'true',
          },
          (chunk) => publishChunk(taskId, chunk)
        );
        await updateTaskComplete(taskId, []);
      } catch (err: any) {
        await updateTaskFailed(taskId);
        await publishChunk(taskId, { type: 'error', text: err.message });
      }
    })();
  }
);

// ── POST /agents/design/projects/:projectId/screens/:screenId/edit ────────
// Edit an existing screen (iterative refinement)
router.post(
  '/projects/:projectId/screens/:screenId/edit',
  async (req, res) => {
    const authed = req as unknown as AuthedRequest;
    const { projectId, screenId } = req.params;
    const { prompt, priorHtml, orgId, deviceTarget = 'responsive' } = req.body as Record<string, string>;

    if (!prompt?.trim())    { res.status(400).json({ error: 'prompt required' });    return; }
    if (!priorHtml?.trim()) { res.status(400).json({ error: 'priorHtml required' }); return; }

    const taskId = crypto.randomUUID();
    await createTaskMeta(taskId, {
      userId:    authed.userId,
      userEmail: authed.userEmail,
      prompt,
      createdAt: new Date().toISOString(),
    });

    res.json({ taskId, screenId });

    (async () => {
      try {
        await runDesignTask(
          { taskId, userId: authed.userId, userEmail: authed.userEmail,
            orgId: orgId ?? 'fortress-am', projectId, screenId,
            priorHtml, prompt, deviceTarget: deviceTarget as any,
            variantCount: 1, convertToBlazor: false },
          (chunk) => publishChunk(taskId, chunk)
        );
        await updateTaskComplete(taskId, []);
      } catch (err: any) {
        await updateTaskFailed(taskId);
        await publishChunk(taskId, { type: 'error', text: err.message });
      }
    })();
  }
);

// ── GET /agents/design/projects/:projectId/screens/:screenId/versions ─────
// Get version history for a screen
router.get('/projects/:projectId/screens/:screenId/versions', async (req, res) => {
  const { projectId, screenId } = req.params;
  const orgId = (req.query.orgId as string) ?? 'fortress-am';

  const redis = await getRedis();
  const key   = `design:screen:${orgId}:${projectId}:${screenId}:versions`;
  const raw   = await redis.lRange(key, 0, -1);
  const versions = raw.map((v: string) => JSON.parse(v));
  res.json({ screenId, projectId, versions });
});

// ── GET /agents/design/tasks/:taskId/stream ───────────────────────────────
// SSE stream for design task progress (same pattern as generic tasks)
router.get('/tasks/:taskId/stream', async (req, res) => {
  const { taskId } = req.params;
  res.setHeader('Content-Type', 'text/event-stream');
  res.setHeader('Cache-Control', 'no-cache');
  res.setHeader('Connection', 'keep-alive');
  res.flushHeaders();

  let cancelled = false;
  req.on('close', () => { cancelled = true; });

  try {
    for await (const chunk of subscribeToTask(taskId)) {
      if (cancelled) break;
      res.write(`data: ${JSON.stringify(chunk)}\n\n`);
      if ((chunk as any).type === 'result' || (chunk as any).type === 'error') break;
    }
  } catch (err: any) {
    res.write(`data: ${JSON.stringify({ type: 'error', text: err.message })}\n\n`);
  } finally {
    res.end();
  }
});

// ── GET /agents/design/brand/:orgId ───────────────────────────────────────
// Get brand context for an org
router.get('/brand/:orgId', async (req, res) => {
  const { orgId } = req.params;
  const brand = await getBrandContext(orgId);
  res.json(brand);
});

// ── PUT /agents/design/brand/:orgId ───────────────────────────────────────
// Save brand context for an org (admin only in production)
router.put('/brand/:orgId', async (req, res) => {
  const { orgId } = req.params;
  const brand = req.body;
  await saveBrandContext(orgId, brand);
  res.json({ ok: true });
});

export default router;
