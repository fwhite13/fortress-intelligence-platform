"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.memoryDelete = memoryDelete;
const db_1 = require("../db");
async function memoryDelete(params, user) {
    let result;
    if (user.scope === 'admin') {
        result = await (0, db_1.getPool)().query('DELETE FROM cc_memory_entries WHERE id = $1 RETURNING id', [params.id]);
    }
    else {
        result = await (0, db_1.getPool)().query('DELETE FROM cc_memory_entries WHERE id = $1 AND user_id = $2 RETURNING id', [params.id, user.id]);
    }
    if (result.rowCount === 0) {
        return { error: 'Entry not found or permission denied' };
    }
    return { deleted: params.id };
}
