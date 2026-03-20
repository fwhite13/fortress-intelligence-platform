import bcrypt from 'bcrypt';
import { Request } from 'express';
import { getPool } from './db';

export interface CcMemoryUser {
  id: string;
  username: string;
  email: string;
  api_token: string;
  scope: 'user' | 'admin';
  is_active: boolean;
}

interface CacheEntry {
  users: CcMemoryUser[];
  fetchedAt: number;
}

let userCache: CacheEntry | null = null;
const CACHE_TTL_MS = 5 * 60 * 1000;

async function getActiveUsers(): Promise<CcMemoryUser[]> {
  const now = Date.now();
  if (userCache && now - userCache.fetchedAt < CACHE_TTL_MS) {
    return userCache.users;
  }
  const result = await getPool().query<CcMemoryUser>(
    'SELECT id, username, email, api_token, scope, is_active FROM cc_memory_users WHERE is_active = true'
  );
  userCache = { users: result.rows, fetchedAt: now };
  return result.rows;
}

export function invalidateUserCache(): void {
  userCache = null;
}

export async function authenticate(req: Request): Promise<CcMemoryUser | null> {
  const auth = req.headers['authorization'];
  const token = auth?.startsWith('Bearer ') ? auth.slice(7) : null;
  if (!token) return null;

  const users = await getActiveUsers();
  for (const user of users) {
    if (await bcrypt.compare(token, user.api_token)) {
      getPool().query('UPDATE cc_memory_users SET last_used_at = NOW() WHERE id = $1', [user.id])
        .catch(() => {});
      return user;
    }
  }
  return null;
}

export function requireAuth(handler: (req: Request, res: any, user: CcMemoryUser) => Promise<void>) {
  return async (req: Request, res: any) => {
    const user = await authenticate(req);
    if (!user) {
      res.status(401).json({ error: 'Unauthorized' });
      return;
    }
    await handler(req, res, user);
  };
}
