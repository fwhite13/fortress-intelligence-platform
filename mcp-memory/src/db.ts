import { Pool } from 'pg';
import * as fs from 'fs';
import * as path from 'path';
import dotenv from 'dotenv';
dotenv.config();

export const pool = new Pool({
  host: process.env.PG_HOST || 'localhost',
  port: parseInt(process.env.PG_PORT || '5433'),
  database: process.env.PG_DB || 'rag',
  user: process.env.PG_USER || 'jarvis',
  password: process.env.PG_PASSWORD,
  max: 10,
  idleTimeoutMillis: 30000,
});

export async function initDb(): Promise<void> {
  const sql = fs.readFileSync(path.join(__dirname, '../migrations/001_init.sql'), 'utf8');
  await pool.query(sql);

  // Ensure embedding column is vector(1024) — idempotent column migration.
  // CREATE TABLE IF NOT EXISTS skips column changes on existing tables, so we
  // check atttypmod: vector(1024) stores typmod=1028, vector(1536) stores typmod=1540.
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
