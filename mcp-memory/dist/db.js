"use strict";
var __createBinding = (this && this.__createBinding) || (Object.create ? (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    var desc = Object.getOwnPropertyDescriptor(m, k);
    if (!desc || ("get" in desc ? !m.__esModule : desc.writable || desc.configurable)) {
      desc = { enumerable: true, get: function() { return m[k]; } };
    }
    Object.defineProperty(o, k2, desc);
}) : (function(o, m, k, k2) {
    if (k2 === undefined) k2 = k;
    o[k2] = m[k];
}));
var __setModuleDefault = (this && this.__setModuleDefault) || (Object.create ? (function(o, v) {
    Object.defineProperty(o, "default", { enumerable: true, value: v });
}) : function(o, v) {
    o["default"] = v;
});
var __importStar = (this && this.__importStar) || (function () {
    var ownKeys = function(o) {
        ownKeys = Object.getOwnPropertyNames || function (o) {
            var ar = [];
            for (var k in o) if (Object.prototype.hasOwnProperty.call(o, k)) ar[ar.length] = k;
            return ar;
        };
        return ownKeys(o);
    };
    return function (mod) {
        if (mod && mod.__esModule) return mod;
        var result = {};
        if (mod != null) for (var k = ownKeys(mod), i = 0; i < k.length; i++) if (k[i] !== "default") __createBinding(result, mod, k[i]);
        __setModuleDefault(result, mod);
        return result;
    };
})();
var __importDefault = (this && this.__importDefault) || function (mod) {
    return (mod && mod.__esModule) ? mod : { "default": mod };
};
Object.defineProperty(exports, "__esModule", { value: true });
exports.initDb = initDb;
exports.getPool = getPool;
const pg_1 = require("pg");
const fs = __importStar(require("fs"));
const path = __importStar(require("path"));
const dotenv_1 = __importDefault(require("dotenv"));
dotenv_1.default.config();
let pool = null;
async function getDbCredentials() {
    // Local dev: use env vars directly (no Secrets Manager)
    if (process.env.PGHOST) {
        return {
            host: process.env.PGHOST,
            port: parseInt(process.env.PGPORT ?? '5432', 10),
            database: process.env.PGDATABASE ?? 'mcp_memory',
            user: process.env.PGUSER ?? 'mcp_memory',
            password: process.env.PGPASSWORD ?? '',
        };
    }
    // AWS ECS: fetch from Secrets Manager
    const { SecretsManagerClient, GetSecretValueCommand } = await Promise.resolve().then(() => __importStar(require('@aws-sdk/client-secrets-manager')));
    const sm = new SecretsManagerClient({ region: process.env.AWS_REGION ?? 'us-east-1' });
    const secretId = process.env.DB_SECRET_ARN ?? 'mcp-memory/db-credentials';
    const resp = await sm.send(new GetSecretValueCommand({ SecretId: secretId }));
    const raw = JSON.parse(resp.SecretString);
    return {
        host: raw.host,
        port: raw.port ?? 5432,
        database: raw.dbname ?? 'mcp_memory', // RDS SM uses 'dbname'; pg Pool needs 'database'
        user: raw.username, // RDS SM uses 'username'; pg Pool needs 'user'
        password: raw.password,
    };
}
async function initDb() {
    if (pool)
        return; // already initialized
    const creds = await getDbCredentials();
    pool = new pg_1.Pool({
        host: creds.host,
        port: creds.port,
        database: creds.database,
        user: creds.user,
        password: creds.password,
        // rds-ca-rsa2048-g1 is included in Node 22's Mozilla trust store — no cert file needed.
        // Only set ca: fs.readFileSync(...) if using the legacy rds-ca-2019 bundle.
        // RDS instance uses rds-ca-2019 (legacy) — not in Node's Mozilla trust store.
        // rejectUnauthorized: false still encrypts the connection; cert verification disabled.
        // Connection is in-VPC only (not internet-exposed) so this is acceptable.
        ssl: process.env.NODE_ENV === 'production' ? { rejectUnauthorized: false } : false,
        max: 5,
        idleTimeoutMillis: 30000,
    });
    const sql = fs.readFileSync(path.join(__dirname, '../migrations/001_init.sql'), 'utf8');
    await pool.query(sql);
    // Idempotent column migration: ensure vector(1024) not vector(1536)
    const dimCheck = await pool.query(`SELECT a.atttypmod FROM pg_attribute a
     JOIN pg_class c ON c.oid = a.attrelid
     WHERE c.relname = 'cc_memory_entries' AND a.attname = 'embedding'`);
    if (dimCheck.rows.length > 0 && dimCheck.rows[0].atttypmod === 1540) {
        await pool.query('ALTER TABLE cc_memory_entries ALTER COLUMN embedding TYPE vector(1024)');
        console.log('[db] Migrated embedding column from vector(1536) to vector(1024)');
    }
    console.log('[db] Migrations applied');
}
function getPool() {
    if (!pool)
        throw new Error('DB not initialized — call initDb() first');
    return pool;
}
