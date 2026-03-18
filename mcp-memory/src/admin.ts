import { pool, initDb } from './db';
import bcrypt from 'bcrypt';
import crypto from 'crypto';
import { invalidateUserCache } from './auth';

const command = process.argv[2];
const args = process.argv.slice(3);

function parseArgs(argv: string[]): Record<string, string> {
  const result: Record<string, string> = {};
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i].replace(/^--/, '');
    result[key] = argv[i + 1];
  }
  return result;
}

async function addUser(username: string, email: string, scope = 'user'): Promise<void> {
  const plaintext = crypto.randomBytes(32).toString('hex');
  const hash = await bcrypt.hash(plaintext, 12);
  const result = await pool.query<{ id: string }>(
    `INSERT INTO cc_memory_users (username, email, api_token, scope)
     VALUES ($1, $2, $3, $4) RETURNING id`,
    [username, email, hash, scope]
  );
  console.log(`✓ User created: ${username} (${email})`);
  console.log(`  ID: ${result.rows[0].id}`);
  console.log(`  Token (save this — shown once): ${plaintext}`);
}

async function resetToken(username: string): Promise<void> {
  const plaintext = crypto.randomBytes(32).toString('hex');
  const hash = await bcrypt.hash(plaintext, 12);
  const result = await pool.query<{ id: string }>(
    `UPDATE cc_memory_users SET api_token = $1, last_used_at = NULL WHERE username = $2 RETURNING id`,
    [hash, username]
  );
  if (result.rowCount === 0) {
    console.error(`User not found: ${username}`);
    process.exit(1);
  }
  invalidateUserCache();
  console.log(`✓ Token reset for ${username}`);
  console.log(`  New token (save this — shown once): ${plaintext}`);
}

async function listUsers(): Promise<void> {
  const result = await pool.query<{
    id: string;
    username: string;
    email: string;
    scope: string;
    is_active: boolean;
    created_at: Date;
    last_used_at: Date | null;
  }>(
    `SELECT id, username, email, scope, is_active, created_at, last_used_at FROM cc_memory_users ORDER BY created_at`
  );
  for (const row of result.rows) {
    const status = row.is_active ? '✓' : '✗';
    console.log(`${status} ${row.username} (${row.email}) scope=${row.scope} last_used=${row.last_used_at?.toISOString() || 'never'}`);
  }
}

async function main(): Promise<void> {
  await initDb();
  const opts = parseArgs(args);

  switch (command) {
    case 'add-user':
      if (!opts['username'] || !opts['email']) {
        console.error('Usage: add-user --username <u> --email <e> [--scope admin]');
        process.exit(1);
      }
      await addUser(opts['username'], opts['email'], opts['scope'] || 'user');
      break;
    case 'reset-token':
      if (!opts['username']) {
        console.error('Usage: reset-token --username <u>');
        process.exit(1);
      }
      await resetToken(opts['username']);
      break;
    case 'list-users':
      await listUsers();
      break;
    default:
      console.error('Commands: add-user, reset-token, list-users');
      process.exit(1);
  }

  await pool.end();
}

main().catch(console.error);
