"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.tasksRouter = void 0;
const express_1 = __importDefault(require("express"));
const multer_1 = __importDefault(require("multer"));
const promises_1 = __importDefault(require("fs/promises"));
const runner_js_1 = require("../agent/runner.js");
const taskStore_js_1 = require("../services/taskStore.js");
const taskQueue_js_1 = require("../services/taskQueue.js");
const fileService_js_1 = require("../services/fileService.js");
const router = express_1.default.Router();
exports.tasksRouter = router;
const upload = (0, multer_1.default)({ dest: '/tmp/cowork-uploads/' });
// ── POST /tasks — create and start a new task ─────────────────────────────
router.post('/', upload.array('files', 5), async (req, res) => {
    const authed = req;
    const { prompt } = req.body;
    if (!prompt?.trim()) {
        res.status(400).json({ error: 'prompt required' });
        return;
    }
    const taskId = crypto.randomUUID();
    const workingDir = `/tmp/cowork-${taskId}`;
    await promises_1.default.mkdir(workingDir, { recursive: true });
    // Upload input files to S3 (replaces local fs.rename)
    const files = req.files;
    if (files && files.length > 0) {
        await (0, fileService_js_1.uploadInputsToS3)(files, taskId);
    }
    // Create task metadata in Redis
    await (0, taskStore_js_1.createTaskMeta)(taskId, {
        userId: authed.userId,
        userEmail: authed.userEmail,
        prompt: prompt.slice(0, 500),
        createdAt: new Date().toISOString(),
    });
    // Atomically check concurrency limit and either start or queue
    const decision = await (0, taskQueue_js_1.tryStartTask)(taskId, authed.userId);
    if (decision === 'started') {
        startTaskWithRedis(taskId, workingDir, authed.userId, authed.userEmail, prompt).catch(console.error);
    }
    else {
        // Task queued — notify via pub/sub
        await (0, taskStore_js_1.publishChunk)(taskId, {
            type: 'step',
            text: 'Task is queued — will start when a slot is available.',
        });
        const position = await (0, taskQueue_js_1.getQueuePosition)(taskId, authed.userId);
        await (0, taskStore_js_1.publishChunk)(taskId, { type: 'queued', position });
    }
    res.json({ taskId, status: decision });
});
async function startTaskWithRedis(taskId, workingDir, userId, userEmail, prompt) {
    const outputFiles = [];
    try {
        const gen = (0, runner_js_1.runTask)({
            taskId,
            userId,
            userEmail,
            prompt,
            workingDir,
            maxBudgetUsd: parseFloat(process.env.COWORK_MAX_BUDGET_USD ?? '0.50'),
            maxTurns: parseInt(process.env.COWORK_MAX_TURNS ?? '30', 10),
        });
        for await (const chunk of gen) {
            await (0, taskStore_js_1.publishChunk)(taskId, chunk);
            if (chunk.type === 'file_output' && chunk.fileName && chunk.downloadUrl) {
                outputFiles.push({
                    name: chunk.fileName,
                    type: chunk.outputType ?? 'other',
                    downloadUrl: chunk.downloadUrl,
                });
            }
            if (chunk.type === 'result' || chunk.type === 'error')
                break;
        }
        await (0, taskStore_js_1.updateTaskComplete)(taskId, outputFiles);
    }
    catch (e) {
        await (0, taskStore_js_1.publishChunk)(taskId, { type: 'error', text: e.message });
        await (0, taskStore_js_1.updateTaskFailed)(taskId);
    }
    finally {
        // Always drain queue on finish
        const nextTaskId = await (0, taskQueue_js_1.onTaskFinished)(userId);
        if (nextTaskId) {
            const nextMeta = await (0, taskStore_js_1.getTaskMeta)(nextTaskId);
            if (nextMeta) {
                await (0, taskStore_js_1.publishChunk)(nextTaskId, { type: 'step', text: 'Task starting now…' });
                const nextWorkingDir = `/tmp/cowork-${nextTaskId}`;
                await promises_1.default.mkdir(nextWorkingDir, { recursive: true }).catch(() => { });
                startTaskWithRedis(nextTaskId, nextWorkingDir, nextMeta.userId, nextMeta.userEmail, nextMeta.prompt).catch(console.error);
            }
        }
    }
}
// ── GET /tasks — list user's task history ────────────────────────────────
router.get('/', async (req, res) => {
    const authed = req;
    const ids = await (0, taskStore_js_1.getUserTaskIds)(authed.userId, 20);
    const tasks = await Promise.all(ids.map(async (id) => {
        const meta = await (0, taskStore_js_1.getTaskMeta)(id);
        if (!meta)
            return null;
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
// ── GET /tasks/:id — get single task metadata ─────────────────────────────
router.get('/:id', async (req, res) => {
    const authed = req;
    const { id } = req.params;
    const meta = await (0, taskStore_js_1.getTaskMeta)(id);
    // ⚠️ CRITICAL: return 404 (not 403) to avoid leaking task existence
    if (!meta || meta.userId !== authed.userId) {
        res.status(404).json({ error: 'Task not found' });
        return;
    }
    res.json({
        taskId: id,
        status: meta.status,
        prompt: meta.prompt,
        createdAt: meta.createdAt,
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
        for await (const chunk of (0, taskStore_js_1.subscribeToTask)(id)) {
            if (cancelled)
                break;
            res.write(`data: ${JSON.stringify(chunk)}\n\n`);
            const c = chunk;
            if (c.type === 'result' || c.type === 'error')
                break;
        }
    }
    catch (err) {
        res.write(`data: ${JSON.stringify({ type: 'error', text: err.message })}\n\n`);
    }
    finally {
        res.end();
    }
});
// ── POST /tasks/:id/approve — user approves a pending tool call ───────────
router.post('/:id/approve', async (req, res) => {
    const { id } = req.params;
    const { approvalId } = req.body;
    if (!approvalId) {
        res.status(400).json({ error: 'approvalId required' });
        return;
    }
    await (0, taskStore_js_1.setApprovalDecision)(approvalId, 'approve');
    res.json({ ok: true });
});
// ── POST /tasks/:id/reject — user rejects a pending tool call ────────────
router.post('/:id/reject', async (req, res) => {
    const { id } = req.params;
    const { approvalId } = req.body;
    if (!approvalId) {
        res.status(400).json({ error: 'approvalId required' });
        return;
    }
    await (0, taskStore_js_1.setApprovalDecision)(approvalId, 'reject');
    res.json({ ok: true });
});
// ── DELETE /tasks/:id — cancel a task ────────────────────────────────────
router.delete('/:id', async (req, res) => {
    const authed = req;
    const { id } = req.params;
    await (0, taskQueue_js_1.cancelTask)(id, authed.userId);
    res.json({ ok: true });
});
//# sourceMappingURL=tasks.js.map