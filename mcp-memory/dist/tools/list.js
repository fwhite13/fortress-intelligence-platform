"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.memoryList = memoryList;
const db_1 = require("../db");
async function memoryList(params, user) {
    const limit = Math.min(params.limit ?? 20, 50);
    const scope = params.scope || 'all';
    let whereClause;
    let queryParams;
    if (scope === 'personal') {
        whereClause = 'user_id = $1';
        queryParams = [user.id, limit];
    }
    else if (scope === 'org') {
        whereClause = "scope = 'org'";
        queryParams = [user.id, limit];
    }
    else {
        whereClause = "(scope = 'org' OR user_id = $1)";
        queryParams = [user.id, limit];
    }
    whereClause += ' AND (expires_at IS NULL OR expires_at > NOW())';
    if (params.project) {
        const paramIndex = queryParams.length + 1;
        whereClause += ` AND project = $${paramIndex}`;
        queryParams = [...queryParams, params.project];
    }
    const query = `
    SELECT id, user_id, scope, project, content, entry_type, source, created_at, metadata
    FROM cc_memory_entries
    WHERE ${whereClause}
    ORDER BY created_at DESC
    LIMIT $2
  `;
    const result = await db_1.pool.query(query, queryParams);
    return result.rows;
}
