import { pool } from '../db';
import { embedText } from '../embed';
import { CcMemoryUser } from '../auth';

interface SearchParams {
  query: string;
  project?: string;
  limit?: number;
}

interface MemoryRow {
  id: string;
  user_id: string | null;
  scope: string;
  project: string | null;
  content: string;
  entry_type: string;
  source: string;
  created_at: Date;
  metadata: Record<string, unknown>;
  similarity: number;
}

export async function memorySearch(params: SearchParams, user: CcMemoryUser): Promise<MemoryRow[]> {
  const limit = Math.min(params.limit ?? 10, 20);
  const embedding = await embedText(params.query);
  const embeddingStr = JSON.stringify(embedding);

  let query: string;
  let queryParams: unknown[];

  if (params.project) {
    query = `
      SELECT id, user_id, scope, project, content, entry_type, source,
             created_at, metadata,
             1 - (embedding <=> $1::vector) AS similarity
      FROM cc_memory_entries
      WHERE
        (scope = 'org' AND user_id IS NULL OR user_id = $2)
        AND (expires_at IS NULL OR expires_at > NOW())
        AND (project = $3 OR project IS NULL)
      ORDER BY embedding <=> $1::vector
      LIMIT $4
    `;
    queryParams = [embeddingStr, user.id, params.project, limit * 2];
  } else {
    query = `
      SELECT id, user_id, scope, project, content, entry_type, source,
             created_at, metadata,
             1 - (embedding <=> $1::vector) AS similarity
      FROM cc_memory_entries
      WHERE
        (scope = 'org' AND user_id IS NULL OR user_id = $2)
        AND (expires_at IS NULL OR expires_at > NOW())
      ORDER BY embedding <=> $1::vector
      LIMIT $3
    `;
    queryParams = [embeddingStr, user.id, limit * 2];
  }

  const result = await pool.query<MemoryRow>(query, queryParams);
  return deduplicateAndRank(result.rows, limit);
}

function deduplicateAndRank(rows: MemoryRow[], limit: number): MemoryRow[] {
  const seen = new Set<string>();
  const deduped: MemoryRow[] = [];
  for (const row of rows) {
    const key = row.content.slice(0, 100);
    if (!seen.has(key)) {
      seen.add(key);
      deduped.push(row);
    }
    if (deduped.length >= limit) break;
  }
  return deduped;
}
