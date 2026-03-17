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
