"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.memorySearch = memorySearch;
const db_1 = require("../db");
const embed_1 = require("../embed");
async function memorySearch(params, user) {
    const limit = Math.min(params.limit ?? 10, 20);
    const embedding = await (0, embed_1.embedText)(params.query);
    const embeddingStr = JSON.stringify(embedding);
    let query;
    let queryParams;
    if (params.project) {
        query = `
      SELECT id, user_id, scope, project, content, entry_type, source,
             created_at, metadata,
             1 - (embedding <=> $1::vector) AS similarity
      FROM cc_memory_entries
      WHERE
        (scope = 'org' OR user_id = $2)
        AND (expires_at IS NULL OR expires_at > NOW())
        AND (project = $3 OR project IS NULL)
      ORDER BY embedding <=> $1::vector
      LIMIT $4
    `;
        queryParams = [embeddingStr, user.id, params.project, limit * 2];
    }
    else {
        query = `
      SELECT id, user_id, scope, project, content, entry_type, source,
             created_at, metadata,
             1 - (embedding <=> $1::vector) AS similarity
      FROM cc_memory_entries
      WHERE
        (scope = 'org' OR user_id = $2)
        AND (expires_at IS NULL OR expires_at > NOW())
      ORDER BY embedding <=> $1::vector
      LIMIT $3
    `;
        queryParams = [embeddingStr, user.id, limit * 2];
    }
    const result = await db_1.pool.query(query, queryParams);
    return deduplicateAndRank(result.rows, limit);
}
function deduplicateAndRank(rows, limit) {
    const seen = new Set();
    const deduped = [];
    for (const row of rows) {
        const key = row.content.slice(0, 100);
        if (!seen.has(key)) {
            seen.add(key);
            deduped.push(row);
        }
        if (deduped.length >= limit)
            break;
    }
    return deduped;
}
