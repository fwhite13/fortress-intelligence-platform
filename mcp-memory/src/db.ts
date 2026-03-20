import { Pool } from 'pg';
import * as fs from 'fs';
import * as path from 'path';
import dotenv from 'dotenv';
dotenv.config();

let pool: Pool | null = null;

async function getDbCredentials(): Promise<{
  host: string; port: number; database: string; user: string; password: string;
}> {
  // Local dev: use env vars directly (no Secrets Manager)
  if (process.env.PGHOST) {
    return {
      host:     process.env.PGHOST,
      port:     parseInt(process.env.PGPORT ?? '5432', 10),
      database: process.env.PGDATABASE ?? 'mcp_memory',
      user:     process.env.PGUSER ?? 'mcp_memory',
      password: process.env.PGPASSWORD ?? '',
    };
  }

  // AWS ECS: fetch from Secrets Manager
  const { SecretsManagerClient, GetSecretValueCommand } = await import('@aws-sdk/client-secrets-manager');
  const sm = new SecretsManagerClient({ region: process.env.AWS_REGION ?? 'us-east-1' });
  const secretId = process.env.DB_SECRET_ARN ?? 'mcp-memory/db-credentials';
  const resp = await sm.send(new GetSecretValueCommand({ SecretId: secretId }));
  const raw = JSON.parse(resp.SecretString!) as {
    host: string; port: number; dbname: string; username: string; password: string;
  };
  return {
    host:     raw.host,
    port:     raw.port ?? 5432,
    database: raw.dbname ?? 'mcp_memory', // RDS SM uses 'dbname'; pg Pool needs 'database'
    user:     raw.username, // RDS SM uses 'username'; pg Pool needs 'user'
    password: raw.password,
  };
}

export async function initDb(): Promise<void> {
  if (pool) return; // already initialized

  const creds = await getDbCredentials();
  pool = new Pool({
    host:     creds.host,
    port:     creds.port,
    database: creds.database,
    user:     creds.user,
    password: creds.password,
    // rds-ca-rsa2048-g1 is included in Node 22's Mozilla trust store — no cert file needed.
    // Only set ca: fs.readFileSync(...) if using the legacy rds-ca-2019 bundle.
    // RDS instance uses rds-ca-2019 (legacy) — not in Node's Mozilla trust store.
    // rejectUnauthorized: false still encrypts the connection; cert verification disabled.
    // Connection is in-VPC only (not internet-exposed) so this is acceptable.
    ssl:      process.env.NODE_ENV === 'production' ? { rejectUnauthorized: false } : false,
    max:      5,
    idleTimeoutMillis: 30_000,
  });

  const sql = fs.readFileSync(path.join(__dirname, '../migrations/001_init.sql'), 'utf8');
  await pool.query(sql);

  // Idempotent column migration: ensure vector(1024) not vector(1536)
  const dimCheck = await pool.query<{ atttypmod: number }>(
    `SELECT a.atttypmod FROM pg_attribute a
     JOIN pg_class c ON c.oid = a.attrelid
     WHERE c.relname = 'cc_memory_entries' AND a.attname = 'embedding'`,
  );
  if (dimCheck.rows.length > 0 && dimCheck.rows[0].atttypmod === 1540) {
    await pool.query('ALTER TABLE cc_memory_entries ALTER COLUMN embedding TYPE vector(1024)');
    console.log('[db] Migrated embedding column from vector(1536) to vector(1024)');
  }

  console.log('[db] Migrations applied');
}

export function getPool(): Pool {
  if (!pool) throw new Error('DB not initialized — call initDb() first');
  return pool;
}
