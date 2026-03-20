"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.tryStartTask = tryStartTask;
exports.onTaskFinished = onTaskFinished;
exports.cancelTask = cancelTask;
exports.getQueuePosition = getQueuePosition;
const taskStore_js_1 = require("./taskStore.js");
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
async function tryStartTask(taskId, userId) {
    const redis = await (0, taskStore_js_1.getRedis)();
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
async function onTaskFinished(userId) {
    const redis = await (0, taskStore_js_1.getRedis)();
    const countKey = `cowork:user:{${userId}}:running_count`;
    const queueKey = `cowork:user:{${userId}}:queue`;
    // CRITICAL: Decrement with floor at 0 — prevents negative count from blocking future tasks
    const newCount = await redis.decr(countKey);
    if (newCount < 0)
        await redis.set(countKey, '0');
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
async function cancelTask(taskId, userId) {
    const redis = await (0, taskStore_js_1.getRedis)();
    const meta = await (0, taskStore_js_1.getTaskMeta)(taskId);
    if (!meta || meta.userId !== userId)
        return;
    if (meta.status === 'queued') {
        await redis.lRem(`cowork:user:{${userId}}:queue`, 0, taskId);
        await redis.hSet(`cowork:task:${taskId}`, {
            status: 'cancelled',
            completedAt: new Date().toISOString(),
        });
    }
    else if (meta.status === 'running') {
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
async function getQueuePosition(taskId, userId) {
    const redis = await (0, taskStore_js_1.getRedis)();
    const queue = await redis.lRange(`cowork:user:{${userId}}:queue`, 0, -1);
    const pos = queue.indexOf(taskId);
    return pos === -1 ? null : pos + 1;
}
//# sourceMappingURL=taskQueue.js.map