"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.APPROVAL_TIMEOUT_MS = exports.TASK_TTL_SECONDS = void 0;
exports.getRedis = getRedis;
exports.createTaskMeta = createTaskMeta;
exports.updateTaskComplete = updateTaskComplete;
exports.updateTaskFailed = updateTaskFailed;
exports.getTaskMeta = getTaskMeta;
exports.getUserTaskIds = getUserTaskIds;
exports.waitForApproval = waitForApproval;
exports.setApprovalDecision = setApprovalDecision;
exports.taskChannel = taskChannel;
exports.publishChunk = publishChunk;
exports.subscribeToTask = subscribeToTask;
const redis_1 = require("redis");
const REDIS_URL = process.env.REDIS_URL;
if (!REDIS_URL)
    throw new Error('REDIS_URL env var required');
if (!REDIS_URL.startsWith('rediss://') && !REDIS_URL.startsWith('redis://')) {
    throw new Error(`REDIS_URL is not a valid redis:// or rediss:// URL: ${REDIS_URL}`);
}
if (!REDIS_URL.startsWith('rediss://')) {
    console.warn('WARNING: REDIS_URL does not use TLS (rediss://)');
}
// ⚠️ CRITICAL: TWO separate Redis connections.
// redis    = commands (hSet, get, set, zAdd, rPush, expire, publish)
// redisSub = SUBSCRIBE ONLY — never call commands on this one
let _redis = null;
let _redisSub = null;
let _connectPromise = null;
async function ensureConnected() {
    _connectPromise ??= (async () => {
        _redis = (0, redis_1.createClient)({ url: REDIS_URL });
        _redisSub = (0, redis_1.createClient)({ url: REDIS_URL });
        await Promise.all([_redis.connect(), _redisSub.connect()]);
    })();
    return _connectPromise;
}
function redis() { if (!_redis)
    throw new Error('Redis not connected'); return _redis; }
function redisSub() { if (!_redisSub)
    throw new Error('RedisSub not connected'); return _redisSub; }
async function getRedis() {
    await ensureConnected();
    return redis();
}
exports.TASK_TTL_SECONDS = 7 * 24 * 60 * 60; // 7 days
exports.APPROVAL_TIMEOUT_MS = 5 * 60 * 1000; // 5 minutes
async function createTaskMeta(taskId, meta) {
    await ensureConnected();
    const key = `cowork:task:${taskId}`;
    await redis().hSet(key, { ...meta, status: 'running', completedAt: '', outputFiles: '[]' });
    await redis().expire(key, exports.TASK_TTL_SECONDS);
    await redis().zAdd(`cowork:user:${meta.userId}:tasks`, {
        score: Date.now(),
        value: taskId,
    });
    await redis().expire(`cowork:user:${meta.userId}:tasks`, 30 * 24 * 60 * 60);
}
async function updateTaskComplete(taskId, outputFiles) {
    await ensureConnected();
    await redis().hSet(`cowork:task:${taskId}`, {
        status: 'completed',
        completedAt: new Date().toISOString(),
        outputFiles: JSON.stringify(outputFiles),
    });
}
async function updateTaskFailed(taskId) {
    await ensureConnected();
    await redis().hSet(`cowork:task:${taskId}`, {
        status: 'failed',
        completedAt: new Date().toISOString(),
    });
}
async function getTaskMeta(taskId) {
    await ensureConnected();
    const data = await redis().hGetAll(`cowork:task:${taskId}`);
    if (!data || !data.status)
        return null;
    return data;
}
async function getUserTaskIds(userId, limit = 20) {
    await ensureConnected();
    return redis().zRange(`cowork:user:${userId}:tasks`, '+inf', '-inf', {
        BY: 'SCORE', REV: true, LIMIT: { offset: 0, count: limit },
    });
}
// ── Approval gate ─────────────────────────────────────────────────────────
// Polls Redis every 200ms. Auto-rejects after APPROVAL_TIMEOUT_MS (5 min).
async function waitForApproval(approvalId) {
    await ensureConnected();
    const key = `cowork:approval:${approvalId}`;
    const deadline = Date.now() + exports.APPROVAL_TIMEOUT_MS;
    while (Date.now() < deadline) {
        const val = await redis().get(key);
        if (val === 'approve')
            return 'approve';
        if (val === 'reject')
            return 'reject';
        await new Promise(r => setTimeout(r, 200)); // 200ms poll — do NOT reduce below 100ms
    }
    return 'reject'; // timeout → auto-reject
}
async function setApprovalDecision(approvalId, decision) {
    await ensureConnected();
    await redis().set(`cowork:approval:${approvalId}`, decision, { EX: 30 });
}
// ── SSE streaming via Redis Pub/Sub ───────────────────────────────────────
function taskChannel(taskId) {
    return `cowork:stream:${taskId}`;
}
async function publishChunk(taskId, chunk) {
    await ensureConnected();
    const logKey = `cowork:stream:log:${taskId}`;
    // Append to replay log with TTL reset on every push
    await redis().rPush(logKey, JSON.stringify(chunk));
    await redis().expire(logKey, 3600); // 1-hour TTL reset on every push
    // Publish live to subscribers
    await redis().publish(taskChannel(taskId), JSON.stringify(chunk));
}
async function* subscribeToTask(taskId) {
    await ensureConnected();
    const channel = taskChannel(taskId);
    // Replay missed events (for SSE reconnects mid-task)
    const missed = await redis().lRange(`cowork:stream:log:${taskId}`, 0, -1);
    for (const raw of missed)
        yield JSON.parse(raw);
    const chunks = [];
    let resolve = null;
    let done = false;
    // ⚠️ subscribe() called ONLY on redisSub — never on redis
    await redisSub().subscribe(channel, (message) => {
        const chunk = JSON.parse(message);
        chunks.push(chunk);
        if (resolve) {
            resolve();
            resolve = null;
        }
        const t = chunk.type;
        if (t === 'result' || t === 'error')
            done = true;
    });
    try {
        while (!done) {
            if (chunks.length > 0) {
                yield chunks.shift();
            }
            else {
                await new Promise(r => { resolve = r; });
            }
        }
        while (chunks.length > 0)
            yield chunks.shift();
    }
    finally {
        await redisSub().unsubscribe(channel);
    }
}
//# sourceMappingURL=taskStore.js.map