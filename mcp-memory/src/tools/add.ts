import { getPool } from '../db';
import { embedText } from '../embed';
import { CcMemoryUser } from '../auth';

interface AddParams {
  content: string;
  entry_type?: string;
  project?: string;
  scope?: string;
  confirmed?: boolean;
}

interface AddResult {
  id?: string;
  created_at?: Date;
  confirmation_required?: boolean;
  message?: string;
  preview?: string;
}

export async function memoryAdd(params: AddParams, user: CcMemoryUser): Promise<AddResult> {
  const scope = params.scope || 'personal';
  const entry_type = params.entry_type || 'note';
  const content = params.content.slice(0, 2000);

  if (scope === 'org' && user.scope !== 'admin' && !params.confirmed) {
    return {
      confirmation_required: true,
      message: "This will write to org memory visible to all team members. Call memory_add again with confirmed: true to proceed.",
      preview: content,
    };
  }

  const embedding = await embedText(content);
  const embeddingStr = JSON.stringify(embedding);

  const userId = scope === 'personal' ? user.id : null;

  const result = await getPool().query<{ id: string; created_at: Date }>(
    `INSERT INTO cc_memory_entries (user_id, scope, project, content, entry_type, source, embedding, created_by)
     VALUES ($1, $2, $3, $4, $5, 'manual', $6::vector, $7)
     RETURNING id, created_at`,
    [userId, scope, params.project || null, content, entry_type, embeddingStr, user.id]
  );

  return { id: result.rows[0].id, created_at: result.rows[0].created_at };
}
