"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.usersRouter = void 0;
const express_1 = __importDefault(require("express"));
const taskStore_js_1 = require("../services/taskStore.js");
const router = express_1.default.Router();
exports.usersRouter = router;
// GET /users/me/instructions
router.get('/me/instructions', async (req, res) => {
    const authed = req;
    const redis = await (0, taskStore_js_1.getRedis)();
    const data = await redis.hGetAll(`cowork:user:${authed.userId}:instructions`);
    res.json({ text: data?.text ?? '', updatedAt: data?.updatedAt ?? null });
});
// PUT /users/me/instructions
router.put('/me/instructions', async (req, res) => {
    const authed = req;
    const { text } = req.body;
    if (typeof text !== 'string') {
        res.status(400).json({ error: 'text required' });
        return;
    }
    if (text.length > 2000) {
        res.status(400).json({ error: 'max 2000 characters' });
        return;
    }
    const redis = await (0, taskStore_js_1.getRedis)();
    if (text.trim() === '') {
        await redis.del(`cowork:user:${authed.userId}:instructions`);
    }
    else {
        await redis.hSet(`cowork:user:${authed.userId}:instructions`, {
            text: text.trim(),
            updatedAt: new Date().toISOString(),
        });
    }
    res.json({ ok: true });
});
//# sourceMappingURL=users.js.map