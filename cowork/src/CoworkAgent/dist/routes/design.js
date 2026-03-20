"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
const express_1 = __importDefault(require("express"));
const multer_1 = __importDefault(require("multer"));
const crypto_1 = __importDefault(require("crypto"));
const runner_js_1 = require("../agents/design/runner.js");
const brandService_js_1 = require("../services/brandService.js");
const taskStore_js_1 = require("../services/taskStore.js");
const taskStore_js_2 = require("../services/taskStore.js");
const fileService_js_1 = require("../services/fileService.js");
const router = express_1.default.Router();
const upload = (0, multer_1.default)({ dest: '/tmp/cowork-uploads/', limits: { fileSize: 10 * 1024 * 1024 } });
// ── POST /agents/design/projects/:projectId/screens ───────────────────────
// Generate a new screen (or 3 variants)
router.post('/projects/:projectId/screens', upload.array('refs', 3), async (req, res) => {
    const authed = req;
    const { projectId } = req.params;
    const { prompt, deviceTarget = 'responsive', variantCount = '1', convertToBlazor = 'false', orgId, } = req.body;
    if (!prompt?.trim()) {
        res.status(400).json({ error: 'prompt required' });
        return;
    }
    const taskId = crypto_1.default.randomUUID();
    const screenId = crypto_1.default.randomUUID();
    // Upload reference images to S3 if attached
    const files = req.files;
    if (files?.length)
        await (0, fileService_js_1.uploadInputsToS3)(files, taskId);
    await (0, taskStore_js_2.createTaskMeta)(taskId, {
        userId: authed.userId,
        userEmail: authed.userEmail,
        prompt,
        createdAt: new Date().toISOString(),
    });
    res.json({ taskId, screenId });
    // Run async
    (async () => {
        try {
            await (0, runner_js_1.runDesignTask)({ taskId, userId: authed.userId, userEmail: authed.userEmail,
                orgId: orgId ?? 'fortress-am', projectId, screenId,
                prompt, deviceTarget: deviceTarget,
                variantCount: Math.min(parseInt(variantCount, 10), 3),
                convertToBlazor: convertToBlazor === 'true', }, (chunk) => (0, taskStore_js_2.publishChunk)(taskId, chunk));
            await (0, taskStore_js_2.updateTaskComplete)(taskId, []);
        }
        catch (err) {
            await (0, taskStore_js_2.updateTaskFailed)(taskId);
            await (0, taskStore_js_2.publishChunk)(taskId, { type: 'error', text: err.message });
        }
    })();
});
// ── POST /agents/design/projects/:projectId/screens/:screenId/edit ────────
// Edit an existing screen (iterative refinement)
router.post('/projects/:projectId/screens/:screenId/edit', async (req, res) => {
    const authed = req;
    const { projectId, screenId } = req.params;
    const { prompt, priorHtml, orgId, deviceTarget = 'responsive' } = req.body;
    if (!prompt?.trim()) {
        res.status(400).json({ error: 'prompt required' });
        return;
    }
    if (!priorHtml?.trim()) {
        res.status(400).json({ error: 'priorHtml required' });
        return;
    }
    const taskId = crypto_1.default.randomUUID();
    await (0, taskStore_js_2.createTaskMeta)(taskId, {
        userId: authed.userId,
        userEmail: authed.userEmail,
        prompt,
        createdAt: new Date().toISOString(),
    });
    res.json({ taskId, screenId });
    (async () => {
        try {
            await (0, runner_js_1.runDesignTask)({ taskId, userId: authed.userId, userEmail: authed.userEmail,
                orgId: orgId ?? 'fortress-am', projectId, screenId,
                priorHtml, prompt, deviceTarget: deviceTarget,
                variantCount: 1, convertToBlazor: false }, (chunk) => (0, taskStore_js_2.publishChunk)(taskId, chunk));
            await (0, taskStore_js_2.updateTaskComplete)(taskId, []);
        }
        catch (err) {
            await (0, taskStore_js_2.updateTaskFailed)(taskId);
            await (0, taskStore_js_2.publishChunk)(taskId, { type: 'error', text: err.message });
        }
    })();
});
// ── GET /agents/design/projects/:projectId/screens/:screenId/versions ─────
// Get version history for a screen
router.get('/projects/:projectId/screens/:screenId/versions', async (req, res) => {
    const { projectId, screenId } = req.params;
    const orgId = req.query.orgId ?? 'fortress-am';
    const redis = await (0, taskStore_js_1.getRedis)();
    const key = `design:screen:${orgId}:${projectId}:${screenId}:versions`;
    const raw = await redis.lRange(key, 0, -1);
    const versions = raw.map((v) => JSON.parse(v));
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
        for await (const chunk of (0, taskStore_js_2.subscribeToTask)(taskId)) {
            if (cancelled)
                break;
            res.write(`data: ${JSON.stringify(chunk)}\n\n`);
            if (chunk.type === 'result' || chunk.type === 'error')
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
// ── GET /agents/design/brand/:orgId ───────────────────────────────────────
// Get brand context for an org
router.get('/brand/:orgId', async (req, res) => {
    const { orgId } = req.params;
    const brand = await (0, brandService_js_1.getBrandContext)(orgId);
    res.json(brand);
});
// ── PUT /agents/design/brand/:orgId ───────────────────────────────────────
// Save brand context for an org (admin only in production)
router.put('/brand/:orgId', async (req, res) => {
    const { orgId } = req.params;
    const brand = req.body;
    await (0, brandService_js_1.saveBrandContext)(orgId, brand);
    res.json({ ok: true });
});
exports.default = router;
//# sourceMappingURL=design.js.map