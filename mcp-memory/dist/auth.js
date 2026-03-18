"use strict";
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.invalidateUserCache = invalidateUserCache;
exports.authenticate = authenticate;
exports.requireAuth = requireAuth;
const bcrypt_1 = __importDefault(require("bcrypt"));
const db_1 = require("./db");
let userCache = null;
const CACHE_TTL_MS = 5 * 60 * 1000;
async function getActiveUsers() {
    const now = Date.now();
    if (userCache && now - userCache.fetchedAt < CACHE_TTL_MS) {
        return userCache.users;
    }
    const result = await db_1.pool.query('SELECT id, username, email, api_token, scope, is_active FROM cc_memory_users WHERE is_active = true');
    userCache = { users: result.rows, fetchedAt: now };
    return result.rows;
}
function invalidateUserCache() {
    userCache = null;
}
async function authenticate(req) {
    const auth = req.headers['authorization'];
    const token = auth?.startsWith('Bearer ') ? auth.slice(7) : null;
    if (!token)
        return null;
    const users = await getActiveUsers();
    for (const user of users) {
        if (await bcrypt_1.default.compare(token, user.api_token)) {
            db_1.pool.query('UPDATE cc_memory_users SET last_used_at = NOW() WHERE id = $1', [user.id])
                .catch(() => { });
            return user;
        }
    }
    return null;
}
function requireAuth(handler) {
    return async (req, res) => {
        const user = await authenticate(req);
        if (!user) {
            res.status(401).json({ error: 'Unauthorized' });
            return;
        }
        await handler(req, res, user);
    };
}
