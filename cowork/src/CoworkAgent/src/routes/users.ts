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
