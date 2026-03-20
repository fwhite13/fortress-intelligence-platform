import { getPool } from '../db';
import { CcMemoryUser } from '../auth';

interface DeleteParams {
  id: string;
}

interface DeleteResult {
  deleted?: string;
  error?: string;
}

export async function memoryDelete(params: DeleteParams, user: CcMemoryUser): Promise<DeleteResult> {
  let result;
  if (user.scope === 'admin') {
    result = await getPool().query<{ id: string }>(
      'DELETE FROM cc_memory_entries WHERE id = $1 RETURNING id',
      [params.id]
    );
  } else {
    result = await getPool().query<{ id: string }>(
      'DELETE FROM cc_memory_entries WHERE id = $1 AND user_id = $2 RETURNING id',
      [params.id, user.id]
    );
  }

  if (result.rowCount === 0) {
    return { error: 'Entry not found or permission denied' };
  }
  return { deleted: params.id };
}
