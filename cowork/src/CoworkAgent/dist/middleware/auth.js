"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.authMiddleware = authMiddleware;
const jsonwebtoken_1 = __importDefault(require("jsonwebtoken"));
const SECRET = process.env.COWORK_INTERNAL_SECRET;
if (!SECRET)
    throw new Error('COWORK_INTERNAL_SECRET env var required');
// Capture as non-nullable after the guard above
const VERIFIED_SECRET = SECRET;
function authMiddleware(req, res, next) {
    const auth = req.headers.authorization;
    if (!auth?.startsWith('Bearer ')) {
        res.status(401).json({ error: 'Missing internal auth token' });
        return;
    }
    try {
        const token = auth.slice(7);
        const payload = jsonwebtoken_1.default.verify(token, VERIFIED_SECRET, {
            issuer: 'cowork-web',
            audience: 'cowork-agent',
        });
        req.userId = payload.sub;
        req.userEmail = payload.email;
        next();
    }
    catch {
        res.status(401).json({ error: 'Invalid internal auth token' });
    }
}
//# sourceMappingURL=auth.js.map