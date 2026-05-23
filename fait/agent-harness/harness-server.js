const express = require('express');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const { mkdirSync } = require('fs');
const { SecretsManagerClient, GetSecretValueCommand } = require('@aws-sdk/client-secrets-manager');
const { BedrockRuntimeClient, ConverseCommand, ConverseStreamCommand, InvokeModelCommand } = require('@aws-sdk/client-bedrock-runtime');
const { S3Client, GetObjectCommand, PutObjectCommand, HeadObjectCommand } = require('@aws-sdk/client-s3');
const { getSignedUrl } = require('@aws-sdk/s3-request-presigner');
const { BedrockAgentRuntimeClient, RetrieveCommand } = require('@aws-sdk/client-bedrock-agent-runtime');
const mysql = require('mysql2/promise');
const { Pool } = require('pg');
const crypto = require('crypto');
let pgPool = null;
const pgProvisionedUsers = new Set();

const bedrockClient = new BedrockRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });
const s3Client = new S3Client({ region: process.env.AWS_REGION || 'us-east-1' });
const bedrockAgentClient = new BedrockAgentRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });

// ─── DB helpers ───────────────────────────────────────────────────────────
async function getDbConnection() {
    return mysql.createConnection({
        host: process.env.FORTRESS_DB_HOST || 'localhost',
        port: parseInt(process.env.FORTRESS_DB_PORT || '3306', 10),
        database: process.env.DB_NAME || 'fait',
        user: process.env.FORTRESS_DB_USER || 'fait',
        password: process.env.FORTRESS_DB_PASS || '',
        ssl: process.env.DB_SSL !== 'false' ? { rejectUnauthorized: false } : false,
        connectTimeout: 10000,
    });
}



// ADO#3240 — Fetch decrypted tokens from Blazor internal API (replaces direct DB queries)
async function getUserTokens(userId) {
    try {
        const base = FAIT_BASE_URL;
        const secret = INTERNAL_API_TOKEN;
        if (!secret) console.warn('[harness] INTERNAL_API_TOKEN not set — /api/internal/user-tokens will return 401');
        // ADO#3286 — normalize userId to lowercase guid format for consistent Blazor endpoint lookup
        const normalizedUserId = (userId || '').trim().toLowerCase();
        if (!normalizedUserId) {
            console.warn('[harness] getUserTokens: empty userId — skipping token lookup');
            return { ms365: null, ado: null };
        }
        const headers = { 'Content-Type': 'application/json' };
        if (secret) headers['X-Internal-Token'] = secret;
        const res = await fetch(`${base}/api/internal/user-tokens/${encodeURIComponent(normalizedUserId)}`, {
            headers
        });
        let responseBody;
        try { responseBody = await res.text(); } catch { responseBody = '(unreadable)'; }
        if (!res.ok) {
            console.warn(`[getUserTokens] status=${res.status} userId=${normalizedUserId} body=${responseBody}`);
            return { ms365: null, ado: null };
        }
        let data;
        try { data = JSON.parse(responseBody); } catch (parseErr) {
            console.error(`[getUserTokens] JSON parse failed status=${res.status} userId=${normalizedUserId} body=${responseBody}`);
            return { ms365: null, ado: null };
        }
        return {
            ms365: data.ms365AccessToken ?? null,
            ado: data.adoPersonalAccessToken ?? null
        };
    } catch (err) {
        console.error('[harness] getUserTokens error:', err.message);
        return { ms365: null, ado: null };
    }
}

// ADO#3309 — Fetch authoritative KB access entitlements for a user
async function fetchKbAccess(userId) {
    const blazorBase = process.env.BLAZOR_BASE_URL || FAIT_BASE_URL;
    const internalToken = INTERNAL_API_TOKEN;
    try {
        const res = await fetch(`${blazorBase}/api/workspace/internal/kb-access?userId=${encodeURIComponent(userId)}`, {
            headers: {
                'X-Internal-Token': internalToken,
                'Accept': 'application/json'
            }
        });
        if (!res.ok) {
            console.warn(`[harness] fetchKbAccess: HTTP ${res.status} for userId=${userId}`);
            return null;
        }
        return await res.json(); // { corpEnabled, personalUserId, authorizedTeamIds }
    } catch (err) {
        console.error(`[harness] fetchKbAccess error:`, err.message);
        return null;
    }
}

const MODEL_ID = 'us.anthropic.claude-sonnet-4-6';
const FAIT_BASE_URL = process.env.FAIT_BASE_URL || 'http://localhost:8080';
const HARNESS_INTERNAL_SECRET = process.env.HARNESS_INTERNAL_SECRET || ''; // legacy — unused
const INTERNAL_API_TOKEN = process.env.INTERNAL_API_TOKEN || '';

// ─── Intervention hold-for-approval ───────────────────────────────────────
const pendingInterventions = new Map(); // interventionId → { resolve, reject }

// §G7 — Track which userIds are currently in a scheduled task turn
const scheduledTaskUsers = new Set();

async function requireApproval(userId, actionType, actionSummary, actionDetails) {
    const interventionId = crypto.randomUUID();

    // §G7: If this user is in a scheduled task context, use async-safe path
    if (scheduledTaskUsers.has(userId)) {
        try {
            const headers = { 'Content-Type': 'application/json' };
            if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
            await fetch(`${FAIT_BASE_URL}/api/scheduled-tasks/approval/request`, {
                method: 'POST',
                headers,
                body: JSON.stringify({
                    ScheduledTaskId: '', // not available at harness level — blank
                    InterventionId: interventionId,
                    ActionType: actionType,
                    ActionSummary: actionSummary,
                    UserId: userId
                })
            });
        } catch (err) {
            console.error('[harness] G7 requireApproval: failed to store approval request:', err.message);
        }
        // Immediately return denied — CC continues without waiting
        return false;
    }

    // §G2: Real-time SignalR path (interactive turns)
    try {
        const headers = { 'Content-Type': 'application/json' };
        if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
        await fetch(`${FAIT_BASE_URL}/api/intervention/request`, {
            method: 'POST',
            headers,
            body: JSON.stringify({ userId, interventionId, actionType, actionSummary, actionDetails })
        });
    } catch (err) {
        console.error('[harness] requireApproval: failed to send intervention request:', err.message);
        throw new Error('Could not reach Blazor to request approval — action cancelled');
    }

    // Wait for user response (timeout: 5 minutes)
    return new Promise((resolve, reject) => {
        pendingInterventions.set(interventionId, { resolve, reject });
        setTimeout(() => {
            if (pendingInterventions.has(interventionId)) {
                pendingInterventions.delete(interventionId);
                reject(new Error('Intervention timed out after 5 minutes — action cancelled'));
            }
        }, 5 * 60 * 1000);
    });
}

const S3_BUCKET = process.env.WORKSPACE_S3_BUCKET || 'fortress-user-workspaces';
const S3_PREFIX = process.env.WORKSPACE_S3_PREFIX || '';
const { existsSync, writeFileSync } = require('fs');
const app = express();

// ─── Secret scrubber ──────────────────────────────────────────────────────
const SECRET_PATTERNS = [
  /Bearer\s+[A-Za-z0-9\-._~+/]+=*/gi,          // Bearer tokens
  /[A-Za-z0-9]{20,}==[A-Za-z0-9]{5,}/g,         // Base64-like secrets
  /sk-[A-Za-z0-9]{20,}/g,                        // OpenAI-style keys
  /AKIA[0-9A-Z]{16}/g,                            // AWS access key IDs
  /(?:password|passwd|secret|token|key)\s*[:=]\s*['"]?[^\s'"]{8,}['"]?/gi,  // key=value patterns
];

function scrubSecrets(text) {
  if (!text) return text;
  let result = text;
  for (const pattern of SECRET_PATTERNS) {
    result = result.replace(new RegExp(pattern.source, pattern.flags), '[REDACTED]');
  }
  return result;
}

// ADO#3564 — Haiku query rewrite for retrieval optimization
const REWRITE_SKIP_WORDS = 8; // skip rewrite if message is under 8 words
async function rewriteQueryForRetrieval(userMessage, userId) {
    // Skip rewrite for short messages
    const wordCount = (userMessage || '').trim().split(/\s+/).filter(Boolean).length;
    if (wordCount < REWRITE_SKIP_WORDS) {
        return userMessage;
    }
    try {
        const prompt = `You are a search query optimizer. Given a user message from a chat conversation, produce a concise, keyword-rich search query that would retrieve the most relevant context for answering it. Strip conversational filler. Extract the core intent. Output only the rewritten query — no explanation, no preamble.\n\nUser message: ${userMessage}\n\nRewritten query:`;
        const response = await bedrockClient.send(new InvokeModelCommand({
            modelId: 'us.anthropic.claude-haiku-4-5-20251001-v1:0',
            contentType: 'application/json',
            accept: 'application/json',
            body: JSON.stringify({
                anthropic_version: 'bedrock-2023-05-31',
                max_tokens: 128,
                messages: [{ role: 'user', content: prompt }]
            })
        }));
        const parsed = JSON.parse(Buffer.from(response.body).toString('utf-8'));
        const rewritten = parsed?.content?.[0]?.text?.trim();
        if (rewritten) {
            console.log(`[harness] query rewrite: original="${userMessage.slice(0, 100)}" rewritten="${rewritten}" userId=${userId}`);
            return rewritten;
        }
        return userMessage;
    } catch (err) {
        console.warn(`[harness] query rewrite failed (non-fatal), using raw message: ${err.message} userId=${userId}`);
        return userMessage;
    }
}

// ADO#3241 — Structured KB retrieval (returns raw results, not formatted text)
async function retrieveFromKbFull(kbId, query, maxResults = 5) {
    const cmd = new RetrieveCommand({
        knowledgeBaseId: kbId,
        retrievalQuery: { text: query },
        retrievalConfiguration: {
            vectorSearchConfiguration: { numberOfResults: maxResults }
        }
    });
    const resp = await bedrockAgentClient.send(cmd);
    return resp.retrievalResults || [];
}

// ADO#3278 — KB retrieval with metadata filter for data isolation
async function retrieveFromKbFiltered(kbId, query, filterKey, filterValue, maxResults = 5) {
    const retrievalConfig = {
        vectorSearchConfiguration: {
            numberOfResults: maxResults
        }
    };

    // Apply metadata filter when provided (ownerId for personal, teamId for team)
    if (filterKey && filterValue !== undefined && filterValue !== null) {
        retrievalConfig.vectorSearchConfiguration.filter = {
            // ADO#3283: teamId is indexed as string (see KbDocumentService.cs teamId!.Value.ToString())
            // ownerId is also indexed as string (userId.ToString()). .toString() coercion is correct.
            equals: {
                key: filterKey,
                value: filterValue.toString()
            }
        };
    }

    const cmd = new RetrieveCommand({
        knowledgeBaseId: kbId,
        retrievalQuery: { text: query },
        retrievalConfiguration: retrievalConfig
    });
    const resp = await bedrockAgentClient.send(cmd);
    return resp.retrievalResults || [];
}

// ADO#3241 — Emit tool_call SSE event
function emitToolCall(res, server, toolName, status, summary) {
    res.write(`event: tool_call\ndata: ${JSON.stringify({ server, toolName, status, summary })}\n\n`);
}

// ADO#3577 — Human-readable CC progress labels
function resolveProgressLabel(toolName, toolInput) {
    const input = (typeof toolInput === 'string' ? toolInput : JSON.stringify(toolInput || '')).toLowerCase();

    if (toolName === 'bash' || toolName === 'computer') {
        if (input.includes('pip install') || input.includes('pip3 install')) return 'Installing dependencies...';
        if (input.includes('openpyxl') || input.includes('.xlsx') || input.includes('xlrd') || input.includes('xlwt')) return 'Building spreadsheet...';
        if (input.includes('pptx') || input.includes('python-pptx')) return 'Building presentation...';
        if (input.includes('docx') || input.includes('python-docx')) return 'Building document...';
        if (input.includes('python ') || input.includes('python3 ') || input.match(/\.py\b/)) return 'Running Python script...';
        if (input.match(/\b(ls|find|cat|head|tail|grep)\b/)) return 'Reading files...';
        if (input.includes('curl ') || input.includes('wget ') || input.includes('requests')) return 'Fetching data...';
        return 'Running command...';
    }
    if (toolName === 'write_file') {
        const filename = extractFilename(toolInput);
        return filename ? `Saving ${filename}...` : 'Saving file...';
    }
    if (toolName === 'read_file') {
        const filename = extractFilename(toolInput);
        return filename ? `Reading ${filename}...` : 'Reading file...';
    }
    if (toolName === 'list_files') return 'Listing files...';
    return 'Working...';
}

function extractFilename(toolInput) {
    try {
        const obj = typeof toolInput === 'string' ? JSON.parse(toolInput) : toolInput;
        const path = obj?.path || obj?.filename || obj?.file_path || '';
        return path ? path.split('/').pop() : null;
    } catch { return null; }
}

// ADO#3241 — Human-readable summaries for builtin tool_call events
function getBuiltinSummary(toolName, toolInput) {
    switch(toolName) {
        case 'search_knowledge_base': return `Searching knowledge base: "${(toolInput.query||'').substring(0,50)}"`;
        case 'search_memory': return 'Searching memory...';
        case 'read_memory': return 'Reading memory...';
        case 'write_memory': return 'Saving to memory...';
        case 'list_workspace_files': return 'Listing workspace files...';
        case 'create_document': return `Creating document: "${toolInput.filename||toolInput.title||'document'}"`;
        case 'read_file': return `Reading file: ${toolInput.path||''}`;
        case 'write_file': return `Saving file: "${toolInput.path || 'file'}"`;
        case 'list_files': return 'Listing files...';
        default: return `${toolName}...`;
    }
}

app.use(express.json({ limit: '10mb' }));

const PORT = process.env.PORT || 3000;
const WORKSPACE_DIR = process.env.WORKSPACE_DIR || '/workspace';

// Health check
app.get('/health', (req, res) => {
    res.json({ status: 'healthy', timestamp: new Date().toISOString() });
});

async function fetchS3File(key) {
    try {
        const cmd = new GetObjectCommand({ Bucket: S3_BUCKET, Key: key });
        const resp = await s3Client.send(cmd);
        const chunks = [];
        for await (const chunk of resp.Body) chunks.push(chunk);
        return Buffer.concat(chunks).toString('utf-8');
    } catch (err) {
        console.warn(`[harness] Could not fetch ${key}: ${err.message}`);
        return null;
    }
}

// ─── Shared Secrets Manager helper ───────────────────────────────────────
async function getSecret(secretArn) {
    const client = new SecretsManagerClient({ region: process.env.AWS_REGION || 'us-east-1' });
    const response = await client.send(new GetSecretValueCommand({ SecretId: secretArn }));
    return response.SecretString;
}

// ─── pgvector Memory Service (ADO#3397 Feature 7.3) ─────────────────────

async function initPgVector() {
    const secretArn = process.env.PGVECTOR_SECRET_ARN;
    if (!secretArn) {
        console.warn('[pgvector] PGVECTOR_SECRET_ARN not set — pgvector disabled');
        pgPool = null;
        return;
    }
    try {
        const secretJson = await getSecret(secretArn);
        const { username, password, host, port, dbname } = JSON.parse(secretJson);
        pgPool = new Pool({
            user: username,
            password,
            host,
            port: parseInt(port),
            database: dbname,
            ssl: { rejectUnauthorized: false },
            max: 5
        });
        await pgPool.query('SELECT 1');
        console.log('[pgvector] connected to fortress_ai PostgreSQL');
    } catch (err) {
        console.warn('[pgvector] connection failed — falling back to md-file memory:', err.message);
        pgPool = null;
    }
}

async function provisionUserSchema(userId) {
    if (!pgPool) return;
    const schemaName = `user_${userId.replace(/-/g, '_')}`;
    await pgPool.query(`CREATE SCHEMA IF NOT EXISTS "${schemaName}"`);
    await pgPool.query(`
        CREATE TABLE IF NOT EXISTS "${schemaName}".memory_chunks (
            id SERIAL PRIMARY KEY,
            source_file VARCHAR(500) NOT NULL,
            chunk_index INT NOT NULL,
            content TEXT NOT NULL,
            embedding vector(1536) NOT NULL,
            updated_at TIMESTAMP NOT NULL DEFAULT NOW()
        )
    `);
    await pgPool.query(`
        CREATE INDEX IF NOT EXISTS memory_chunks_embedding_idx
        ON "${schemaName}".memory_chunks
        USING ivfflat (embedding vector_cosine_ops) WITH (lists = 100)
    `);
    // ADO#3428 — lazy migration: if no chunks yet, migrate existing memory files
    try {
        const countResult = await pgPool.query(`SELECT COUNT(*) FROM "${schemaName}".memory_chunks`);
        const chunkCount = parseInt(countResult.rows[0].count);
        if (chunkCount === 0) {
            console.log(`[pgvector] no chunks for userId=${userId} — lazy migration will run on next write_memory call`);
        }
    } catch (countErr) {
        console.warn(`[pgvector] chunk count check failed (non-fatal): ${countErr.message}`);
    }
    console.log(`[pgvector] schema provisioned for userId=${userId}`);
}

async function embedText(text) {
    const payload = {
        inputText: text.substring(0, 8000),
        dimensions: 1536,
        normalize: true
    };
    const cmd = new InvokeModelCommand({
        modelId: 'amazon.titan-embed-text-v2:0',
        body: JSON.stringify(payload),
        contentType: 'application/json',
        accept: 'application/json'
    });
    const resp = await bedrockClient.send(cmd);
    const result = JSON.parse(Buffer.from(resp.body).toString());
    return result.embedding;
}

async function upsertMemoryChunks(userId, sourceFile, content) {
    if (!pgPool) return;
    if (!pgProvisionedUsers.has(userId)) {
        await provisionUserSchema(userId);
        pgProvisionedUsers.add(userId);
    }
    const schemaName = `user_${userId.replace(/-/g, '_')}`;

    const chunks = [];
    const CHUNK_SIZE = 500, OVERLAP = 50;
    for (let i = 0; i < content.length; i += CHUNK_SIZE - OVERLAP) {
        chunks.push(content.slice(i, i + CHUNK_SIZE));
        if (i + CHUNK_SIZE >= content.length) break;
    }

    await pgPool.query(`DELETE FROM "${schemaName}".memory_chunks WHERE source_file = $1`, [sourceFile]);

    for (let i = 0; i < chunks.length; i++) {
        const embedding = await embedText(chunks[i]);
        await pgPool.query(
            `INSERT INTO "${schemaName}".memory_chunks (source_file, chunk_index, content, embedding) VALUES ($1, $2, $3, $4)`,
            [sourceFile, i, chunks[i], JSON.stringify(embedding)]
        );
    }
    console.log(`[pgvector] upserted ${chunks.length} chunks for userId=${userId} sourceFile=${sourceFile}`);
}

async function searchMemoryChunks(userId, query, topK = 5, threshold = 0.7) {
    if (!pgPool) return null;
    const schemaName = `user_${userId.replace(/-/g, '_')}`;
    try {
        const embedding = await embedText(query);
        const result = await pgPool.query(
            `SELECT content, source_file, 1 - (embedding <=> $1::vector) AS score
             FROM "${schemaName}".memory_chunks
             WHERE 1 - (embedding <=> $1::vector) >= $2
             ORDER BY score DESC
             LIMIT $3`,
            [JSON.stringify(embedding), threshold, topK]
        );
        return result.rows;
    } catch (err) {
        console.warn(`[pgvector] search failed for userId=${userId}:`, err.message);
        return null;
    }
}

// ─── GCP credential bootstrap ─────────────────────────────────────────────
async function bootstrapGcpCredentials() {
    const secretName = process.env.GCP_STITCH_SECRET_NAME || 'fait-v2/gcp-stitch-service-account';
    try {
        const client = new SecretsManagerClient({ region: process.env.AWS_REGION || 'us-east-1' });
        const response = await client.send(new GetSecretValueCommand({ SecretId: secretName }));

        const secretValue = response.SecretString;
        if (!secretValue) {
            console.warn('[harness] GCP secret is binary or empty — Stitch will be unavailable');
            return;
        }
        // Validate it's parseable JSON (GCP SA keys are JSON objects)
        try {
            JSON.parse(secretValue);
        } catch {
            console.warn('[harness] GCP secret is not valid JSON — Stitch will be unavailable');
            return;
        }

        const credPath = '/tmp/gcp-service-account.json';
        writeFileSync(credPath, secretValue, { mode: 0o600 });
        process.env.GOOGLE_APPLICATION_CREDENTIALS = credPath;
        console.log('[harness] GCP credentials bootstrapped');
    } catch (err) {
        console.warn('[harness] GCP credentials not available — Stitch will be unavailable:', err.message);
    }
}

// ─── Stitch MCP tool routing ───────────────────────────────────────────────
const STITCH_TOOLS = new Set([
    'generate_screen_from_text',
    'extract_design_context',
    'fetch_screen_code',
    'fetch_screen_image',
    'list_projects',
    'list_screens',
    'refine_screen'
]);

async function invokeStitchTool(toolName, args, timeoutMs = 30000) {
    return new Promise((resolve, reject) => {
        const proc = spawn('stitch-mcp', [], { env: process.env });
        let buffer = '';
        let initDone = false;
        let toolDone = false;
        let toolCallId = 2;

        const timer = setTimeout(() => {
            proc.kill();
            reject(new Error(`stitch-mcp timeout after ${timeoutMs}ms`));
        }, timeoutMs);

        proc.stdout.on('data', (chunk) => {
            buffer += chunk.toString();
            const lines = buffer.split('\n');
            buffer = lines.pop(); // keep incomplete line

            for (const line of lines) {
                if (!line.trim()) continue;
                let msg;
                try { msg = JSON.parse(line); } catch { continue; }

                if (!initDone && msg.id === 1) {
                    // Got initialize response — send initialized notification + tool call
                    initDone = true;
                    proc.stdin.write(JSON.stringify({
                        jsonrpc: '2.0',
                        method: 'notifications/initialized'
                    }) + '\n');
                    proc.stdin.write(JSON.stringify({
                        jsonrpc: '2.0',
                        id: toolCallId,
                        method: 'tools/call',
                        params: { name: toolName, arguments: args }
                    }) + '\n');
                } else if (initDone && msg.id === toolCallId) {
                    clearTimeout(timer);
                    proc.kill();
                    toolDone = true;
                    if (msg.error) reject(new Error(msg.error.message || JSON.stringify(msg.error)));
                    else resolve(msg.result);
                }
            }
        });

        proc.stderr.on('data', (d) => console.error('[stitch-mcp stderr]', d.toString()));
        proc.on('exit', (code) => {
            clearTimeout(timer);
            if (!initDone) reject(new Error(`stitch-mcp exited ${code} before initialize response`));
            else if (!toolDone) reject(new Error(`stitch-mcp exited ${code} before tool response`));
        });

        // Send initialize request
        proc.stdin.write(JSON.stringify({
            jsonrpc: '2.0',
            id: 1,
            method: 'initialize',
            params: {
                protocolVersion: '2024-11-05',
                capabilities: {},
                clientInfo: { name: 'fait-v2-harness', version: '1.0.0' }
            }
        }) + '\n');
    });
}

// ─── G4: MCP Tool Allowlist ────────────────────────────────────────────────
// ADO#3109 — Only explicitly allowlisted tools may be dispatched via the generic route.
// Named routes above (graph_*, ado_*, stitch_*, list_workspace_files, search_memory)
// are inherently allowed by virtue of being registered; this catches the catch-all.
const MCP_TOOL_ALLOWLIST = {
    'graph': new Set([
        'graph_list_emails', 'graph_get_email',
        'graph_send_email', 'graph_list_files', 'graph_get_file_content',
        'graph_list_calendar_events'
    ]),
    'ado': new Set([
        'ado_list_work_items', 'ado_get_work_item', 'ado_create_work_item',
        'ado_update_work_item', 'ado_list_projects', 'ado_wiql_query'
    ]),
    'stitch': new Set([
        'stitch_generate_screen', 'stitch_refine_screen', 'stitch_extract_design_dna',
        'generate_screen_from_text', 'extract_design_context', 'fetch_screen_code',
        'fetch_screen_image', 'list_projects', 'list_screens', 'refine_screen'
    ]),
    'brave': new Set(['web_search']),
};

const BUILTIN_TOOLS = new Set([
    'list_workspace_files', 'read_workspace_file', 'search_memory', 'read_memory', 'write_memory', 'create_document',
    'list_files', 'read_file', 'write_file'
]);

// ADO#3218 — MCP tool specs for dynamic toolConfig injection
const MCP_TOOL_SPECS = {
  m365: [
    {
      toolSpec: {
        name: 'graph_list_emails',
        description: 'List recent emails from Microsoft 365 inbox',
        inputSchema: { json: { type: 'object', properties: { max_results: { type: 'number', description: 'Max emails to return (default 10)' } }, required: [] } }
      }
    },
    {
      toolSpec: {
        name: 'graph_get_email',
        description: 'Get full content of a specific email by ID',
        inputSchema: { json: { type: 'object', properties: { message_id: { type: 'string', description: 'Email message ID' } }, required: ['message_id'] } }
      }
    },
    {
      toolSpec: {
        name: 'graph_send_email',
        description: 'Send an email via Microsoft 365',
        inputSchema: { json: { type: 'object', properties: { to: { type: 'string' }, subject: { type: 'string' }, body: { type: 'string' } }, required: ['to', 'subject', 'body'] } }
      }
    },
    {
      toolSpec: {
        name: 'graph_list_calendar_events',
        description: 'List upcoming calendar events from Microsoft 365',
        inputSchema: { json: { type: 'object', properties: { days_ahead: { type: 'number', description: 'Days ahead to look (default 7)' } }, required: [] } }
      }
    }
  ],
  azdo: [
    {
      toolSpec: {
        name: 'ado_list_work_items',
        description: 'List work items from Azure DevOps',
        inputSchema: { json: { type: 'object', properties: { project: { type: 'string' }, state: { type: 'string' }, assignee: { type: 'string' } }, required: [] } }
      }
    },
    {
      toolSpec: {
        name: 'ado_get_work_item',
        description: 'Get details of a specific Azure DevOps work item by ID',
        inputSchema: { json: { type: 'object', properties: { id: { type: 'number', description: 'Work item ID' } }, required: ['id'] } }
      }
    },
    {
      toolSpec: {
        name: 'ado_create_work_item',
        description: 'Create a new work item in Azure DevOps',
        inputSchema: { json: { type: 'object', properties: { project: { type: 'string' }, type: { type: 'string' }, title: { type: 'string' }, description: { type: 'string' } }, required: ['project', 'type', 'title'] } }
      }
    },
    {
      toolSpec: {
        name: 'ado_update_work_item',
        description: 'Update an existing Azure DevOps work item',
        inputSchema: { json: { type: 'object', properties: { id: { type: 'number' }, state: { type: 'string' }, title: { type: 'string' }, comment: { type: 'string' } }, required: ['id'] } }
      }
    },
    {
      toolSpec: {
        name: 'ado_list_projects',
        description: 'List all Azure DevOps projects',
        inputSchema: { json: { type: 'object', properties: {}, required: [] } }
      }
    },
    {
      toolSpec: {
        name: 'ado_wiql_query',
        description: 'Query Azure DevOps work items using WIQL (Work Item Query Language)',
        inputSchema: {
          json: {
            type: 'object',
            properties: {
              query: { type: 'string', description: 'WIQL query string' }
            },
            required: ['query']
          }
        }
      }
    }
  ],
  brave: [
    {
      toolSpec: {
        name: 'web_search',
        description: 'Search the web using Brave Search. Use this when the user asks about current events, recent news, facts, or anything requiring up-to-date information from the internet.',
        inputSchema: {
          json: {
            type: 'object',
            properties: {
              query: { type: 'string', description: 'The search query' },
              count: { type: 'number', description: 'Number of results to return (1-10, default 5)' }
            },
            required: ['query']
          }
        }
      }
    }
  ]
};
// Support all slug variants for Azure DevOps
MCP_TOOL_SPECS['ado']    = MCP_TOOL_SPECS['azdo'];
MCP_TOOL_SPECS['devops'] = MCP_TOOL_SPECS['azdo'];  // DB slug

// ADO#3398 7.7-C — tool manifest assembled at cold start based on active plugin set
function buildToolManifestSection(enabledPlugins) {
    const tools = [
        { name: 'read_memory', use: 'Looking up prior context, preferences, or decisions about a topic' },
        { name: 'write_memory', use: 'User states a preference, decision, or fact worth persisting' },
        { name: 'search_memory', use: 'Searching across all memory topics by keyword' },
        { name: 'create_document', use: 'User asks to create a Word document, report, or structured deliverable' },
        { name: 'list_files', use: 'Listing files the user uploaded to their workspace' },
        { name: 'read_file', use: 'Reading content of a user-uploaded file by path' },
        { name: 'write_file', use: 'Saving text content as a file in the user\'s workspace' },
        { name: 'list_workspace_files', use: 'Seeing assistant-generated artifacts from prior sessions' },
        { name: 'read_workspace_file', use: 'Reading content of an assistant-generated artifact by ID' },
    ];

    if (Array.isArray(enabledPlugins)) {
        if (enabledPlugins.includes('m365') || enabledPlugins.includes('graph')) {
            tools.push(
                { name: 'graph_list_emails', use: 'Listing recent emails from Microsoft 365 inbox' },
                { name: 'graph_get_email', use: 'Getting full content of a specific email by ID' },
                { name: 'graph_list_calendar_events', use: 'Listing upcoming calendar events' },
                { name: 'graph_send_email', use: 'Sending an email via Microsoft 365' }
            );
        }
        if (enabledPlugins.includes('ado') || enabledPlugins.includes('azdo') || enabledPlugins.includes('devops')) {
            tools.push(
                { name: 'ado_list_work_items', use: 'Listing Azure DevOps work items' },
                { name: 'ado_get_work_item', use: 'Getting details of a specific Azure DevOps work item' },
                { name: 'ado_update_work_item', use: 'Updating an Azure DevOps work item' },
                { name: 'ado_create_work_item', use: 'Creating a new Azure DevOps work item' }
            );
        }
        if (enabledPlugins.includes('brave')) {
            tools.push({ name: 'web_search', use: 'Searching the internet for current information' });
        }
    }

    const rows = tools.map(t => `| ${t.name} | ${t.use} |`).join('\n');
    return `## Available Tools\n\n| Tool | Use when |\n|------|----------|\n${rows}`;
}

function isToolAllowed(toolName) {
    // Check against each server's allowlist
    for (const [, tools] of Object.entries(MCP_TOOL_ALLOWLIST)) {
        if (tools.has(toolName)) return true;
    }
    // Built-in harness tools are always allowed
    if (BUILTIN_TOOLS.has(toolName)) return true;
    return false;
}

// Stitch MCP health check
app.get('/tools/stitch/health', (req, res) => {
    const credPath = process.env.GOOGLE_APPLICATION_CREDENTIALS;
    const available = !!(credPath && existsSync(credPath));
    res.json({ available, reason: available ? 'ok' : 'GCP credentials not configured' });
});

// ─── MS365 Graph API tool handlers (ADO#3069) ─────────────────────────────
const GRAPH_BASE = 'https://graph.microsoft.com/v1.0';

async function graphRequest(accessToken, method, path, body) {
    const url = `${GRAPH_BASE}${path}`;
    const opts = {
        method,
        headers: {
            'Authorization': `Bearer ${accessToken}`,
            'Content-Type': 'application/json',
            'Accept': 'application/json',
        },
    };
    if (body) opts.body = JSON.stringify(body);
    const resp = await fetch(url, opts);
    if (!resp.ok) {
        const text = await resp.text();
        throw new Error(`Graph API ${method} ${path} failed (${resp.status}): ${text}`);
    }
    if (resp.status === 204) return null;
    return resp.json();
}

app.post('/tools/graph_list_emails', async (req, res) => {
    const { userId, maxResults = 10, folder = 'inbox' } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    const token = req.body?.ms365Token;
    if (!token) return res.status(401).json({ error: 'MS365 token not available — ensure Microsoft 365 is connected in FAIT settings' });
    try {
        const data = await graphRequest(token, 'GET',
            `/me/mailFolders/${encodeURIComponent(folder)}/messages?$top=${maxResults}&$orderby=receivedDateTime desc&$select=id,subject,from,receivedDateTime,bodyPreview,isRead`
        );
        const emails = (data.value || []).map(m => ({
            id: m.id,
            subject: m.subject,
            from: m.from?.emailAddress?.address,
            receivedDateTime: m.receivedDateTime,
            bodyPreview: m.bodyPreview,
            isRead: m.isRead,
        }));
        res.json({ result: emails });
    } catch (err) {
        console.error('[harness] graph_list_emails error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/graph_get_email', async (req, res) => {
    const { userId, messageId } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!messageId) return res.status(400).json({ error: 'messageId required' });
    const token = req.body?.ms365Token;
    if (!token) return res.status(401).json({ error: 'MS365 token not available — ensure Microsoft 365 is connected in FAIT settings' });
    try {
        const m = await graphRequest(token, 'GET',
            `/me/messages/${encodeURIComponent(messageId)}?$select=id,subject,from,body,receivedDateTime`
        );
        res.json({
            result: {
                id: m.id,
                subject: m.subject,
                from: m.from?.emailAddress?.address,
                body: m.body?.content,
                receivedDateTime: m.receivedDateTime,
            }
        });
    } catch (err) {
        console.error('[harness] graph_get_email error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/graph_list_calendar_events', async (req, res) => {
    const { userId, days = 7 } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    const token = req.body?.ms365Token;
    if (!token) return res.status(401).json({ error: 'MS365 token not available — ensure Microsoft 365 is connected in FAIT settings' });
    try {
        const now = new Date().toISOString();
        const end = new Date(Date.now() + days * 86400000).toISOString();
        const data = await graphRequest(token, 'GET',
            `/me/calendarView?startDateTime=${encodeURIComponent(now)}&endDateTime=${encodeURIComponent(end)}&$orderby=start/dateTime&$select=id,subject,start,end,location,organizer`
        );
        const events = (data.value || []).map(e => ({
            id: e.id,
            subject: e.subject,
            start: e.start,
            end: e.end,
            location: e.location?.displayName,
            organizer: e.organizer?.emailAddress?.address,
        }));
        res.json({ result: events });
    } catch (err) {
        console.error('[harness] graph_list_calendar_events error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/graph_send_email', async (req, res) => {
    const { userId, to, subject, body, cc } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!to || !subject || !body) return res.status(400).json({ error: 'to, subject, and body required' });
    const token = req.body?.ms365Token;
    if (!token) return res.status(401).json({ error: 'MS365 token not available — ensure Microsoft 365 is connected in FAIT settings' });
    try {
        const payload = {
            message: {
                subject,
                body: { contentType: 'Text', content: body },
                toRecipients: [{ emailAddress: { address: to } }],
            }
        };
        if (cc) {
            payload.message.ccRecipients = [{ emailAddress: { address: cc } }];
        }
        // G2 gate: require user approval before sending
        let approved = false;
        try {
            approved = await requireApproval(
                userId,
                'send_email',
                `Send email to ${to}: "${subject}"`,
                JSON.stringify(payload).substring(0, 500)
            );
        } catch (approvalErr) {
            return res.status(200).json({ result: { denied: true, reason: approvalErr.message } });
        }
        if (!approved) {
            return res.status(200).json({ result: { denied: true, reason: 'User denied the action' } });
        }

        await graphRequest(token, 'POST', '/me/sendMail', payload);
        res.json({ result: { sent: true } });
    } catch (err) {
        console.error('[harness] graph_send_email error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── Azure DevOps tool handlers (ADO#3070) ────────────────────────────────
const ADO_ORG = process.env.ADO_ORG || 'FortressAffinityGroup';
const ADO_BASE = `https://dev.azure.com/${ADO_ORG}`;

function adoAuthHeader(pat) {
    return 'Basic ' + Buffer.from(':' + pat).toString('base64');
}

async function adoRequest(pat, method, url, body) {
    const opts = {
        method,
        headers: {
            'Authorization': adoAuthHeader(pat),
            'Content-Type': 'application/json',
            'Accept': 'application/json',
        },
    };
    if (body) opts.body = JSON.stringify(body);
    const resp = await fetch(url, opts);
    if (!resp.ok) {
        const text = await resp.text();
        throw new Error(`ADO ${method} ${url} failed (${resp.status}): ${text}`);
    }
    if (resp.status === 204) return null;
    return resp.json();
}

app.post('/tools/ado_list_work_items', async (req, res) => {
    const { userId, project, iteration, state, assignedTo, top = 20 } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!project) return res.status(400).json({ error: 'project required' });
    const pat = req.body?.adoToken;
    if (!pat) return res.status(401).json({ error: 'ADO token not available — ensure MS/ADO is connected in FAIT settings' });
    try {
        // Sanitize WIQL string params to prevent injection
        const sanitize = (s) => String(s || '').replace(/'/g, "''");
        const safeTop = Math.min(parseInt(top, 10) || 20, 200);
        let wiql = `SELECT [System.Id],[System.Title],[System.State],[System.AssignedTo],[Microsoft.VSTS.Common.Priority] FROM WorkItems WHERE [System.TeamProject] = '${sanitize(project)}'`;
        if (iteration) wiql += ` AND [System.IterationPath] = '${sanitize(iteration)}'`;
        if (state) wiql += ` AND [System.State] = '${sanitize(state)}'`;
        if (assignedTo) wiql += ` AND [System.AssignedTo] = '${sanitize(assignedTo)}'`;
        wiql += ` ORDER BY [System.ChangedDate] DESC`;

        const wiqlUrl = `${ADO_BASE}/${encodeURIComponent(project)}/_apis/wit/wiql?api-version=7.1&$top=${safeTop}`;
        const wiqlResp = await adoRequest(pat, 'POST', wiqlUrl, { query: wiql });
        const ids = (wiqlResp.workItems || []).map(w => w.id);
        if (ids.length === 0) return res.json({ result: [] });

        // Batch fetch work item details
        const fields = 'System.Id,System.Title,System.State,System.AssignedTo,Microsoft.VSTS.Common.Priority';
        const detailUrl = `${ADO_BASE}/_apis/wit/workitems?ids=${ids.join(',')}&fields=${fields}&api-version=7.1`;
        const detailResp = await adoRequest(pat, 'GET', detailUrl, null);
        const items = (detailResp.value || []).map(w => ({
            id: w.id,
            title: w.fields['System.Title'],
            state: w.fields['System.State'],
            assignedTo: w.fields['System.AssignedTo']?.displayName,
            priority: w.fields['Microsoft.VSTS.Common.Priority'],
        }));
        res.json({ result: items });
    } catch (err) {
        console.error('[harness] ado_list_work_items error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/ado_get_work_item', async (req, res) => {
    const { userId, id } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!id) return res.status(400).json({ error: 'id required' });
    const pat = req.body?.adoToken;
    if (!pat) return res.status(401).json({ error: 'ADO token not available — ensure MS/ADO is connected in FAIT settings' });
    try {
        const url = `${ADO_BASE}/_apis/wit/workitems/${id}?api-version=7.1`;
        const w = await adoRequest(pat, 'GET', url, null);
        res.json({
            result: {
                id: w.id,
                title: w.fields['System.Title'],
                state: w.fields['System.State'],
                description: w.fields['System.Description'],
                assignedTo: w.fields['System.AssignedTo']?.displayName,
                priority: w.fields['Microsoft.VSTS.Common.Priority'],
                workItemType: w.fields['System.WorkItemType'],
            }
        });
    } catch (err) {
        console.error('[harness] ado_get_work_item error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/ado_update_work_item', async (req, res) => {
    const { userId, id, state, title, comment } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!id) return res.status(400).json({ error: 'id required' });
    const pat = req.body?.adoToken;
    if (!pat) return res.status(401).json({ error: 'ADO token not available — ensure MS/ADO is connected in FAIT settings' });
    try {
        const project = process.env.ADO_DEFAULT_PROJECT || 'FAIT';
        const url = `${ADO_BASE}/${encodeURIComponent(project)}/_apis/wit/workItems/${id}?api-version=7.1`;
        const ops = [];
        if (state) ops.push({ op: 'add', path: '/fields/System.State', value: state });
        if (title) ops.push({ op: 'add', path: '/fields/System.Title', value: title });
        if (comment) ops.push({ op: 'add', path: '/fields/System.History', value: comment });
        if (ops.length === 0) return res.status(400).json({ error: 'Nothing to update — provide state, title, or comment' });

        // G2 gate: require user approval before updating work item
        const updateSummary = [state && `state→${state}`, title && `title→"${title}"`, comment && 'add comment'].filter(Boolean).join(', ');
        let approved = false;
        try {
            approved = await requireApproval(
                userId,
                'ado_post',
                `Update ADO work item #${id}: ${updateSummary}`,
                JSON.stringify({ id, state, title, comment }).substring(0, 500)
            );
        } catch (approvalErr) {
            return res.status(200).json({ result: { denied: true, reason: approvalErr.message } });
        }
        if (!approved) {
            return res.status(200).json({ result: { denied: true, reason: 'User denied the action' } });
        }

        const resp = await fetch(url, {
            method: 'PATCH',
            headers: {
                'Authorization': adoAuthHeader(pat),
                'Content-Type': 'application/json-patch+json',
                'Accept': 'application/json',
            },
            body: JSON.stringify(ops),
        });
        if (!resp.ok) {
            const text = await resp.text();
            throw new Error(`ADO PATCH ${url} failed (${resp.status}): ${text}`);
        }
        res.json({ result: { updated: true, id } });
    } catch (err) {
        console.error('[harness] ado_update_work_item error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/ado_create_work_item', async (req, res) => {
    const { userId, project, type = 'Task', title, description, priority } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!project) return res.status(400).json({ error: 'project required' });
    if (!title) return res.status(400).json({ error: 'title required' });
    const pat = req.body?.adoToken;
    if (!pat) return res.status(401).json({ error: 'ADO token not available — ensure MS/ADO is connected in FAIT settings' });
    try {
        const url = `${ADO_BASE}/${encodeURIComponent(project)}/_apis/wit/workItems/$${encodeURIComponent(type)}?api-version=7.1`;
        const ops = [
            { op: 'add', path: '/fields/System.Title', value: title },
        ];
        if (description) ops.push({ op: 'add', path: '/fields/System.Description', value: description });
        if (priority) ops.push({ op: 'add', path: '/fields/Microsoft.VSTS.Common.Priority', value: priority });

        // G2 gate: require user approval before creating work item
        let approved = false;
        try {
            approved = await requireApproval(
                userId,
                'ado_post',
                `Create ADO work item in ${project}: [${type}] ${title}`,
                JSON.stringify({ project, type, title, description, priority }).substring(0, 500)
            );
        } catch (approvalErr) {
            return res.status(200).json({ result: { denied: true, reason: approvalErr.message } });
        }
        if (!approved) {
            return res.status(200).json({ result: { denied: true, reason: 'User denied the action' } });
        }

        const resp = await fetch(url, {
            method: 'POST',
            headers: {
                'Authorization': adoAuthHeader(pat),
                'Content-Type': 'application/json-patch+json',
                'Accept': 'application/json',
            },
            body: JSON.stringify(ops),
        });
        if (!resp.ok) {
            const text = await resp.text();
            throw new Error(`ADO POST ${url} failed (${resp.status}): ${text}`);
        }
        const w = await resp.json();
        res.json({
            result: {
                id: w.id,
                title: w.fields['System.Title'],
                url: w._links?.html?.href || w.url,
            }
        });
    } catch (err) {
        console.error('[harness] ado_create_work_item error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── search_memory tool handler (ADO#3102) ────────────────────────────────
app.post('/tools/search_memory', async (req, res) => {
    const { userId, query, topK = 5 } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!query) return res.status(400).json({ error: 'query required' });

    const blazorBase = FAIT_BASE_URL;
    const internalToken = process.env.INTERNAL_API_TOKEN || '';

    try {
        const resp = await fetch(`${blazorBase}/api/memory/search`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({ query, topK, userId }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            throw new Error(`memory/search failed (${resp.status}): ${text}`);
        }
        const results = await resp.json();
        res.json({ results });
    } catch (err) {
        console.error('[harness] search_memory error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── read_memory tool handler (ADO#3188) ─────────────────────────────────
app.post('/tools/read_memory', async (req, res) => {
    const { userId, slug } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!slug) return res.status(400).json({ error: 'slug required' });

    const blazorBase = FAIT_BASE_URL;
    const internalToken = process.env.INTERNAL_API_TOKEN || '';

    try {
        const resp = await fetch(`${blazorBase}/api/memory/read`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({ userId, slug }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            const isHtml = text.trim().startsWith('<') || text.includes('<!DOCTYPE');
            const safeText = isHtml ? `[non-JSON response, HTTP ${resp.status}]` : text.substring(0, 200);
            throw new Error(`memory/read failed (${resp.status}): ${safeText}`);
        }
        const result = await resp.json();
        let mdContent = result.found ? result.content : `Topic '${slug}' not found in memory.`;
        // ADO#3397 — augment read_memory with semantic search
        if (pgPool && result.found) {
            try {
                const semanticChunks = await searchMemoryChunks(userId, slug + ' ' + (req.body.query || ''), 3, 0.6);
                if (semanticChunks && semanticChunks.length > 0) {
                    const semanticContext = semanticChunks.map(c => c.content).join('\n\n');
                    mdContent = `[Semantically relevant]\n${semanticContext}\n\n---\n\n[Full topic]\n${mdContent}`;
                }
            } catch (augmentErr) {
                console.warn(`[pgvector] read_memory augment failed: ${augmentErr.message}`);
            }
        }
        res.json({ content: mdContent });
    } catch (err) {
        console.error('[harness] read_memory error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── write_memory tool handler (ADO#3188) ────────────────────────────────
app.post('/tools/write_memory', async (req, res) => {
    const { userId, slug, title, content } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!slug) return res.status(400).json({ error: 'slug required' });
    if (!content) return res.status(400).json({ error: 'content required' });

    const blazorBase = FAIT_BASE_URL;
    const internalToken = process.env.INTERNAL_API_TOKEN || '';

    try {
        const resp = await fetch(`${blazorBase}/api/memory/write`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({ userId, slug, title: title || slug, content }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            const isHtml = text.trim().startsWith('<') || text.includes('<!DOCTYPE');
            const safeText = isHtml ? `[non-JSON response, HTTP ${resp.status}]` : text.substring(0, 200);
            throw new Error(`memory/write failed (${resp.status}): ${safeText}`);
        }
        // ADO#3397 — embed on write (non-fatal)
        try {
            await upsertMemoryChunks(userId, `memory/${slug}.md`, content);
        } catch (embedErr) {
            console.warn(`[pgvector] embed on write failed (non-fatal): ${embedErr.message}`);
        }
        res.json({ success: true });
    } catch (err) {
        console.error('[harness] write_memory error:', err.message);
        // Best-effort — return success:false on error, do not crash
        res.json({ success: false, error: err.message });
    }
});

// ─── create_document tool handler (ADO#3201) ──────────────────────────────────
app.post('/tools/create_document', async (req, res) => {
    const { userId, conversationId, type, title, sections } = req.body;

    if (type !== 'word') {
        return res.status(400).json({ error: `Unsupported document type: "${type}". Only "word" is supported.` });
    }
    if (!userId || !conversationId) {
        return res.status(400).json({ error: 'userId and conversationId are required' });
    }

    try {
        // 1. Call Blazor API to generate the document bytes
        const headers = { 'Content-Type': 'application/json' };
        if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;

        const genRes = await fetch(`${FAIT_BASE_URL}/api/workspace/generate-document`, {
            method: 'POST',
            headers,
            body: JSON.stringify({ type, title, sections })
        });

        if (!genRes.ok) {
            const errText = await genRes.text();
            const isHtml = errText.trim().startsWith('<') || errText.includes('<!DOCTYPE');
            const safeErr = isHtml
                ? `Document generation failed (HTTP ${genRes.status}). The API returned an unexpected response.`
                : `Document generation failed: ${errText.substring(0, 200)}`;
            return res.status(500).json({ error: safeErr });
        }

        const docBytes = Buffer.from(await genRes.arrayBuffer());
        const sizeBytes = docBytes.length;

        // 2. Sanitize filename and build S3 key
        const sanitized = (title || 'document')
            .toLowerCase()
            .replace(/[^a-z0-9_-]/g, '-')
            .replace(/-+/g, '-')
            .replace(/^-|-$/g, '')
            .substring(0, 100);
        const timestamp = Date.now();
        const filename = `${sanitized}-${timestamp}.docx`;

        // 2a. Resolve (or create) the user's 'general' folder
        let folderGuid;
        {
            const conn2 = await getDbConnection();
            try {
                const [existing] = await conn2.execute(
                    "SELECT id FROM workspace_folders WHERE user_id = ? AND name = 'general' LIMIT 1",
                    [userId]
                );
                if (existing.length > 0) {
                    folderGuid = existing[0].id;
                } else {
                    folderGuid = crypto.randomUUID();
                    await conn2.execute(
                        'INSERT INTO workspace_folders (id, user_id, name, s3_prefix, created_at) VALUES (?, ?, ?, ?, NOW(6))',
                        [folderGuid, userId, 'general', `files/${folderGuid}/`]
                    );
                }
            } finally {
                await conn2.end();
            }
        }

        const s3Key = `workspaces/${userId}/files/${folderGuid}/${filename}`;

        // 3. Upload to S3
        await s3Client.send(new PutObjectCommand({
            Bucket: S3_BUCKET,
            Key: s3Key,
            Body: docBytes,
            ContentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
        }));

        // 4. Insert DB row so file appears in workspace explorer
        {
            const conn3 = await getDbConnection();
            try {
                const fileId = crypto.randomUUID();
                const now = new Date().toISOString().slice(0, 19).replace('T', ' ');
                await conn3.execute(
                    'INSERT INTO user_workspace_uploads (id, user_id, folder_id, filename, mime_type, s3_key, size_bytes, created_at, current_version, source, conversation_id) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
                    [fileId, userId, folderGuid, filename, 'application/vnd.openxmlformats-officedocument.wordprocessingml.document', s3Key, sizeBytes, now, 1, 'assistant', conversationId ?? null]
                );
            } finally {
                await conn3.end();
            }
        }

        res.json({ success: true, filename, s3Key, sizeBytes });
    } catch (err) {
        console.error('[harness] create_document error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── list_files tool handler (ADO#3206, updated ADO#3450) ──────────────────
app.post('/tools/list_files', async (req, res) => {
    const { userId, folder_path = '' } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });

    try {
        const conn = await getDbConnection();
        try {
            // Resolve folder_path to folderId (same traversal as read_file)
            let folderId = null;
            if (folder_path) {
                // workspace_folders is flat (s3_prefix-based, no parent_id)
                // Treat folder_path as folder name — look up top-level folder by name
                const folderName = folder_path.replace(/^\/+|\/+$/g, '').split('/').filter(Boolean)[0];
                if (folderName) {
                    const [folderRows] = await conn.execute(
                        'SELECT id FROM workspace_folders WHERE user_id = ? AND name = ?',
                        [userId, folderName]
                    );
                    if (folderRows.length === 0) {
                        return res.json({ items: [] });
                    }
                    folderId = folderRows[0].id;
                }
            }

            // Query files in resolved folder (or root)
            const fileSql = folderId === null
                ? 'SELECT id, filename, mime_type, s3_key, size_bytes, created_at, folder_id, source, current_version FROM user_workspace_uploads WHERE user_id = ? AND folder_id IS NULL ORDER BY created_at DESC'
                : 'SELECT id, filename, mime_type, s3_key, size_bytes, created_at, folder_id, source, current_version FROM user_workspace_uploads WHERE user_id = ? AND folder_id = ? ORDER BY created_at DESC';
            const fileParams = folderId === null ? [userId] : [userId, folderId];
            const [rows] = await conn.execute(fileSql, fileParams);

            const items = rows.map(r => ({
                id: r.id,
                filename: r.filename,
                mimeType: r.mime_type,
                sizeBytes: r.size_bytes,
                createdAt: r.created_at,
                folderId: r.folder_id,
                source: r.source || 'user',
                currentVersion: r.current_version || 1,
            }));

            res.json({ items });
        } finally {
            await conn.end();
        }
    } catch (err) {
        console.error('[harness] list_files error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── read_file tool handler (ADO#3206) ──────────────────────────────────
app.post('/tools/read_file', async (req, res) => {
    const { userId, file_path } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!file_path) return res.status(400).json({ error: 'file_path required' });

    let conn;
    try {
        conn = await getDbConnection();
        const parts = file_path.replace(/^\/+|\/+$/g, '').split('/');
        const filename = parts.pop();
        const folderPath = parts;

        // Resolve folder (workspace_folders is flat — look up by name only)
        let folderId = null;
        if (folderPath.length > 0) {
            const folderName = folderPath[0]; // top-level only in flat model
            const [folderRows] = await conn.execute(
                'SELECT id FROM workspace_folders WHERE user_id = ? AND name = ?',
                [userId, folderName]
            );
            if (folderRows.length === 0) {
                return res.json({ content: `File not found: ${file_path}` });
            }
            folderId = folderRows[0].id;
        }

        // Find file
        const fileSql = folderId === null
            ? 'SELECT s3_key, mime_type, size_bytes FROM user_workspace_uploads WHERE user_id = ? AND filename = ? AND folder_id IS NULL LIMIT 1'
            : 'SELECT s3_key, mime_type, size_bytes FROM user_workspace_uploads WHERE user_id = ? AND filename = ? AND folder_id = ? LIMIT 1';
        const fileParams = folderId === null ? [userId, filename] : [userId, filename, folderId];
        const [rows] = await conn.execute(fileSql, fileParams);

        if (rows.length === 0) {
            return res.json({ content: `File not found: ${file_path}` });
        }

        const { s3_key, mime_type } = rows[0];

        // Check if text type
        const textMimeTypes = ['text/', 'application/json', 'application/xml', 'application/javascript',
                               'application/x-yaml', 'application/yaml', 'application/csv'];
        const isText = textMimeTypes.some(t => mime_type.startsWith(t)) || mime_type === '';
        if (!isText) {
            return res.json({ content: 'Binary file — cannot read as text. Use download instead.' });
        }

        // Fetch from S3
        const s3Resp = await s3Client.send(new GetObjectCommand({ Bucket: S3_BUCKET, Key: s3_key }));

        const MAX_BYTES = 512000;
        const chunks = [];
        let totalBytes = 0;
        let truncated = false;
        for await (const chunk of s3Resp.Body) {
            if (totalBytes + chunk.length > MAX_BYTES) {
                chunks.push(chunk.slice(0, MAX_BYTES - totalBytes));
                truncated = true;
                break;
            }
            chunks.push(chunk);
            totalBytes += chunk.length;
        }
        let content = Buffer.concat(chunks).toString('utf8');
        if (truncated) content += '\n[Content truncated at 500KB]';

        res.json({ content });
    } catch (err) {
        console.error('[harness] read_file error:', err.message);
        res.status(500).json({ error: err.message });
    } finally {
        if (conn) await conn.end();
    }
});

// ─── write_file tool handler (ADO#3393, updated ADO#3450) ────────────────────
app.post('/tools/write_file', async (req, res) => {
    const { userId, conversationId, path: filePath, content } = req.body;

    // 1. Validate required inputs
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!filePath) return res.status(400).json({ error: 'path is required' });
    if (content === undefined || content === null) return res.status(400).json({ error: 'content is required' });

    // 2. Path safety — reject traversal attempts
    if (filePath.includes('../') || filePath.includes('./') || filePath.startsWith('/')) {
        return res.status(400).json({ error: 'Invalid path: path traversal and absolute paths are not allowed' });
    }

    // 3. Size check — 1MB limit
    const contentBuffer = Buffer.from(content, 'utf8');
    if (contentBuffer.length > 1_048_576) {
        return res.status(400).json({ error: 'Content exceeds 1MB size limit' });
    }

    // 4. Parse path into folder segments + filename
    const parts = filePath.replace(/^\/+|\/+$/g, '').split('/');
    const filename = parts.pop();
    const folderSegments = parts;

    // 5. Detect MIME type from extension
    const ext = filename.split('.').pop()?.toLowerCase() || '';
    const mimeMap = {
        'md': 'text/markdown',
        'txt': 'text/plain',
        'html': 'text/html',
        'htm': 'text/html',
        'json': 'application/json',
        'csv': 'text/csv',
        'xml': 'application/xml',
        'js': 'text/javascript',
        'ts': 'text/typescript',
        'py': 'text/x-python',
        'css': 'text/css',
    };
    const mimeType = mimeMap[ext] || 'text/plain';

    try {
        // 6. Resolve (and create if missing) folder hierarchy
        let folderId = null;
        if (folderSegments.length > 0) {
            const conn2 = await getDbConnection();
            try {
                // workspace_folders is flat (s3_prefix-based, no parent_id)
                // Use first segment as the folder name
                const folderName = folderSegments[0] || 'general';
                const [existingRows] = await conn2.execute(
                    'SELECT id FROM workspace_folders WHERE user_id = ? AND name = ?',
                    [userId, folderName]
                );
                if (existingRows.length > 0) {
                    folderId = existingRows[0].id;
                } else {
                    // Create missing folder in workspace_folders
                    const newFolderId = crypto.randomUUID();
                    const now2 = new Date().toISOString().slice(0, 19).replace('T', ' ');
                    await conn2.execute(
                        'INSERT INTO workspace_folders (id, user_id, name, s3_prefix, created_at) VALUES (?, ?, ?, ?, ?)',
                        [newFolderId, userId, folderName, `files/${newFolderId}/`, now2]
                    );
                    folderId = newFolderId;
                }
            } finally {
                await conn2.end();
            }
        }

        // 7. DB lookup FIRST — to know version number for S3 key
        const conn = await getDbConnection();
        try {
            const now = new Date().toISOString().slice(0, 19).replace('T', ' ');
            const sizeBytes = contentBuffer.length;
            const s3FolderPart = folderId || 'root';

            // Check for existing DB row (folder-aware)
            const folderSql = folderId === null
                ? 'SELECT id, current_version FROM user_workspace_uploads WHERE user_id = ? AND filename = ? AND folder_id IS NULL LIMIT 1'
                : 'SELECT id, current_version FROM user_workspace_uploads WHERE user_id = ? AND filename = ? AND folder_id = ? LIMIT 1';
            const folderParams = folderId === null ? [userId, filename] : [userId, filename, folderId];
            const [existingRows] = await conn.execute(folderSql, folderParams);

            if (existingRows.length > 0) {
                // File exists: bump version, use versioned S3 key
                const existingId = existingRows[0].id;
                const newVersion = (existingRows[0].current_version || 1) + 1;
                const s3Key = `workspaces/${userId}/files/${s3FolderPart}/${filename}`;

                // Write to S3
                await s3Client.send(new PutObjectCommand({
                    Bucket: S3_BUCKET,
                    Key: s3Key,
                    Body: contentBuffer,
                    ContentType: mimeType
                }));

                // Update DB row + insert version record
                await conn.execute(
                    'UPDATE user_workspace_uploads SET s3_key = ?, size_bytes = ?, current_version = ?, source = ?, conversation_id = ? WHERE id = ?',
                    [s3Key, sizeBytes, newVersion, 'assistant', conversationId ?? null, existingId]
                );
                const versionId = crypto.randomUUID();
                await conn.execute(
                    'INSERT INTO workspace_file_versions (id, file_id, version_number, s3_key, size_bytes, created_at, created_by, conversation_id) VALUES (?, ?, ?, ?, ?, ?, ?, ?)',
                    [versionId, existingId, newVersion, s3Key, sizeBytes, now, 'assistant', conversationId ?? null]
                );
                console.log(`[harness] write_file: versioned ${filename} → v${newVersion} in folderId=${folderId} (${sizeBytes} bytes)`);
                res.json({ success: true, filename, s3Key, sizeBytes, mimeType, version: newVersion, updated: true });

            } else {
                // New file: v1 S3 key
                const s3Key = `workspaces/${userId}/files/${s3FolderPart}/${filename}`;

                // Write to S3
                await s3Client.send(new PutObjectCommand({
                    Bucket: S3_BUCKET,
                    Key: s3Key,
                    Body: contentBuffer,
                    ContentType: mimeType
                }));

                // Insert upload row + v1 version row
                const fileId = crypto.randomUUID();
                const folderInsertSql = folderId === null
                    ? 'INSERT INTO user_workspace_uploads (id, user_id, folder_id, filename, mime_type, s3_key, size_bytes, created_at, current_version, source) VALUES (?, ?, NULL, ?, ?, ?, ?, ?, 1, ?)'
                    : 'INSERT INTO user_workspace_uploads (id, user_id, folder_id, filename, mime_type, s3_key, size_bytes, created_at, current_version, source) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?)';
                const folderInsertParams = folderId === null
                    ? [fileId, userId, filename, mimeType, s3Key, sizeBytes, now, 'assistant']
                    : [fileId, userId, folderId, filename, mimeType, s3Key, sizeBytes, now, 'assistant'];
                await conn.execute(folderInsertSql, folderInsertParams);

                const versionId = crypto.randomUUID();
                await conn.execute(
                    'INSERT INTO workspace_file_versions (id, file_id, version_number, s3_key, size_bytes, created_at, created_by, conversation_id) VALUES (?, ?, 1, ?, ?, ?, ?, ?)',
                    [versionId, fileId, s3Key, sizeBytes, now, 'assistant', conversationId ?? null]
                );
                console.log(`[harness] write_file: created ${filename} in folderId=${folderId} (${sizeBytes} bytes)`);
                res.json({ success: true, filename, s3Key, sizeBytes, mimeType, version: 1 });
            }
        } finally {
            await conn.end();
        }

    } catch (err) {
        console.error('[harness] write_file error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── read_workspace_file tool handler (ADO#3396) ──────────────────────────
app.post('/tools/read_workspace_file', async (req, res) => {
    const { userId, fileId, path: filePath } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });
    if (!fileId && !filePath) return res.status(400).json({ error: 'fileId or path required' });

    try {
        let s3Key = null;
        let filename = null;
        let mimeType = null;
        let fileRecord = null;

        const conn = await getDbConnection();
        try {
            if (fileId) {
                // Look up in user_workspace_uploads first
                const [uploadRows] = await conn.execute(
                    'SELECT id, filename, mime_type, s3_key, size_bytes FROM user_workspace_uploads WHERE id = ? AND user_id = ? LIMIT 1',
                    [fileId, userId]
                );
                if (uploadRows.length > 0) {
                    fileRecord = uploadRows[0];
                    s3Key = fileRecord.s3_key;
                    filename = fileRecord.filename;
                    mimeType = fileRecord.mime_type;
                }
            } else if (filePath) {
                // Path-based: look up in user_workspace_uploads by filename
                const safeFilename = filePath.split('/').pop();
                const [uploadRows] = await conn.execute(
                    'SELECT id, filename, mime_type, s3_key, size_bytes FROM user_workspace_uploads WHERE user_id = ? AND filename = ? ORDER BY created_at DESC LIMIT 1',
                    [userId, safeFilename]
                );
                if (uploadRows.length > 0) {
                    fileRecord = uploadRows[0];
                    s3Key = fileRecord.s3_key;
                    filename = fileRecord.filename;
                    mimeType = fileRecord.mime_type;
                }
            }
        } finally {
            await conn.end();
        }

        if (!s3Key) {
            return res.status(404).json({ error: 'File not found' });
        }

        // Binary check — only return content for text files
        const textMimeTypes = ['text/', 'application/json', 'application/xml', 'application/javascript'];
        const isText = textMimeTypes.some(t => mimeType?.startsWith(t));

        if (!isText) {
            return res.json({
                filename,
                mimeType,
                s3Key,
                sizeBytes: fileRecord?.size_bytes ? Number(fileRecord.size_bytes) : 0,
                content: null,
                note: 'Binary file — content not returned. Metadata only.'
            });
        }

        // Fetch content from S3
        const s3Resp = await s3Client.send(new GetObjectCommand({
            Bucket: S3_BUCKET,
            Key: s3Key
        }));

        const chunks = [];
        for await (const chunk of s3Resp.Body) {
            chunks.push(chunk);
        }
        const content = Buffer.concat(chunks).toString('utf8');

        res.json({ filename, mimeType, s3Key, content, sizeBytes: content.length });

    } catch (err) {
        console.error('[harness] read_workspace_file error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── Stitch-specific route handlers (ADO#3099) ────────────────────────────
app.post('/tools/stitch_generate_screen', async (req, res) => {
    const credPath = process.env.GOOGLE_APPLICATION_CREDENTIALS;
    if (!credPath || !existsSync(credPath)) {
        return res.status(503).json({ error: 'Stitch unavailable — GCP credentials not configured' });
    }
    const { userId, prompt, design_dna } = req.body || {};
    const args = { prompt };
    if (design_dna !== undefined) args.design_dna = design_dna;
    try {
        const result = await invokeStitchTool('generate_screen_from_text', args);
        // Extract html, screenId, projectId from result
        const content = result?.content;
        let parsed = {};
        if (Array.isArray(content)) {
            const textBlock = content.find(b => b.type === 'text');
            if (textBlock?.text) {
                try { parsed = JSON.parse(textBlock.text); } catch { parsed = { html: textBlock.text }; }
            }
        } else if (typeof result === 'object') {
            parsed = result;
        }
        res.json({
            html: parsed.html || parsed.code || '',
            screenId: parsed.screenId || parsed.screen_id || null,
            projectId: parsed.projectId || parsed.project_id || null,
        });
    } catch (err) {
        console.error('[harness] stitch_generate_screen error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/stitch_refine_screen', async (req, res) => {
    const credPath = process.env.GOOGLE_APPLICATION_CREDENTIALS;
    if (!credPath || !existsSync(credPath)) {
        return res.status(503).json({ error: 'Stitch unavailable — GCP credentials not configured' });
    }
    const { userId, screen_id, prompt } = req.body || {};
    try {
        const result = await invokeStitchTool('refine_screen', { screen_id, prompt });
        const content = result?.content;
        let parsed = {};
        if (Array.isArray(content)) {
            const textBlock = content.find(b => b.type === 'text');
            if (textBlock?.text) {
                try { parsed = JSON.parse(textBlock.text); } catch { parsed = { html: textBlock.text }; }
            }
        } else if (typeof result === 'object') {
            parsed = result;
        }
        res.json({
            html: parsed.html || parsed.code || '',
            screenId: parsed.screenId || parsed.screen_id || null,
            projectId: parsed.projectId || parsed.project_id || null,
        });
    } catch (err) {
        console.error('[harness] stitch_refine_screen error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/stitch_extract_design_dna', async (req, res) => {
    const credPath = process.env.GOOGLE_APPLICATION_CREDENTIALS;
    if (!credPath || !existsSync(credPath)) {
        return res.status(503).json({ error: 'Stitch unavailable — GCP credentials not configured' });
    }
    const { userId, content } = req.body || {};
    try {
        const result = await invokeStitchTool('extract_design_context', { content });
        res.json(result);
    } catch (err) {
        console.error('[harness] stitch_extract_design_dna error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

app.post('/tools/list_workspace_files', async (req, res) => {
    const { userId, folder = '', type = 'all' } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });

    try {
        const conn = await getDbConnection();
        let files = [];

        try {
            // type='files' or 'all': query user_workspace_uploads
            if (type === 'files' || type === 'all') {
                const [uploadRows] = await conn.execute(
                    'SELECT id, filename, mime_type, s3_key, size_bytes, created_at, folder_id, source, current_version FROM user_workspace_uploads WHERE user_id = ? ORDER BY created_at DESC',
                    [userId]
                );
                const uploads = uploadRows.map(r => ({
                    id: r.id,
                    filename: r.filename,
                    mimeType: r.mime_type,
                    s3Key: r.s3_key,
                    sizeBytes: Number(r.size_bytes),
                    createdAt: r.created_at,
                    folderId: r.folder_id,
                    source: r.source || 'user',
                    currentVersion: r.current_version || 1,
                    fileType: 'upload'
                }));
                files.push(...uploads);
            }

            // type='generated' or 'all': query user_workspace_uploads WHERE source IN ('assistant','cc')
            if (type === 'generated' || type === 'all') {
                const [artifactRows] = await conn.execute(
                    "SELECT id, filename, mime_type, s3_key, size_bytes, created_at, conversation_id, source FROM user_workspace_uploads WHERE user_id = ? AND source IN ('assistant','cc') ORDER BY created_at DESC",
                    [userId]
                );
                const artifacts = artifactRows.map(r => ({
                    id: r.id,
                    filename: r.filename,
                    mimeType: r.mime_type,
                    s3Key: r.s3_key,
                    sizeBytes: Number(r.size_bytes),
                    createdAt: r.created_at,
                    conversationId: r.conversation_id,
                    source: r.source || 'assistant',
                    fileType: 'generated'
                }));
                files.push(...artifacts);
            }
        } finally {
            await conn.end();
        }

        res.json({ files, count: files.length, type });
    } catch (err) {
        console.error('[harness] list_workspace_files error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── Brave web_search tool handler (ADO#3240) ─────────────────────────────
app.post('/tools/web_search', async (req, res) => {
    const { query, count = 5 } = req.body || {};
    if (!query) return res.status(400).json({ error: 'query required' });

    // ADO#3286 — use FAIT_BASE_URL (internal service discovery) instead of localhost
    // Harness and Blazor are separate Fargate tasks — localhost does not route to Blazor
    const braveLocalUrl = `${FAIT_BASE_URL}/internal/mcp/brave`;
    const internalToken = INTERNAL_API_TOKEN;

    try {
        const resp = await fetch(braveLocalUrl, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                ...(internalToken ? { 'X-Internal-Token': internalToken } : {}),
            },
            body: JSON.stringify({
                jsonrpc: '2.0',
                id: '1',
                method: 'tools/call',
                params: {
                    name: 'web_search',
                    arguments: { query, count: Math.min(parseInt(count, 10) || 5, 10) }
                }
            }),
        });
        if (!resp.ok) {
            const text = await resp.text();
            throw new Error(`Brave MCP call failed (${resp.status}): ${text}`);
        }
        const mcpResponse = await resp.json();
        // MCP response: { jsonrpc, id, result: { content: [{ type, text }], isError } }
        const content = mcpResponse?.result?.content;
        const text = Array.isArray(content) ? content.map(c => c.text || '').join('\n') : JSON.stringify(mcpResponse);
        if (mcpResponse?.result?.isError) {
            throw new Error(`Brave search error: ${text}`);
        }
        res.json({ result: text });
    } catch (err) {
        console.error('[harness] web_search error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── Write tool classification ─────────────────────────────────────────────
const WRITE_TOOL_PATTERNS = /create|update|delete|write|send|add|remove|modify|\bset\b/i;
const KB_WRITE_PATTERNS = /kb_write|kb_upsert|kb_create|knowledge_write/i;

function isWriteTool(toolName) {
    return WRITE_TOOL_PATTERNS.test(toolName);
}

function isKbWriteTool(toolName) {
    return KB_WRITE_PATTERNS.test(toolName);
}

// Tool dispatch — generic MCP tools with per-connector enforcement (ADO#3101) + KB write enforcement (ADO#3106)
app.post('/tools/:toolName', async (req, res) => {
    const { toolName } = req.params;
    const args = req.body || {};
    const reqPluginAgentId = args.pluginAgentId ?? null;
    const reqMcpServerPermissions = args.mcpServerPermissions ?? null; // JSON array of {serverId, read, write}
    const reqKbWriteAllowed = args.kbWriteAllowed ?? true;

    // ── ADO#3106: KB write enforcement ──────────────────────────────────
    if (reqPluginAgentId && isKbWriteTool(toolName)) {
        if (!reqKbWriteAllowed) {
            console.warn(`[harness] KB write blocked: tool=${toolName}, pluginAgentId=${reqPluginAgentId}`);
            return res.status(403).json({ error: 'KB write not permitted for this agent' });
        }
    }

    // ── ADO#3101: MCP server write enforcement ───────────────────────────
    if (reqPluginAgentId && reqMcpServerPermissions) {
        let permissions;
        try {
            permissions = typeof reqMcpServerPermissions === 'string'
                ? JSON.parse(reqMcpServerPermissions)
                : reqMcpServerPermissions;
        } catch {
            permissions = [];
        }

        // Determine which server this tool belongs to (if any)
        // Convention: toolName may be prefixed with serverId_ or passed as args.serverId
        const serverId = args.serverId ?? args.server_id ?? null;
        if (serverId) {
            const perm = permissions.find(p => p.serverId === serverId);
            if (!perm) {
                console.warn(`[harness] MCP tool blocked: server ${serverId} not in allowed list for pluginAgentId=${reqPluginAgentId}`);
                return res.status(403).json({ error: 'MCP server not allowed for this agent' });
            }
            if (!perm.write && isWriteTool(toolName)) {
                console.warn(`[harness] MCP write blocked: tool=${toolName}, server=${serverId}, pluginAgentId=${reqPluginAgentId}`);
                return res.status(403).json({ error: 'Write access not allowed for this MCP server' });
            }
        }
    }

    // ── G4: MCP Tool Allowlist check (ADO#3109) ───────────────────────────
    if (!isToolAllowed(toolName)) {
        console.warn(`[harness] /tools/${toolName}: rejected — not in MCP_TOOL_ALLOWLIST`);
        return res.status(403).json({ error: `Tool '${toolName}' is not in the allowed tool list` });
    }

    // ── Stitch MCP tools ──────────────────────────────────────────────────
    if (!STITCH_TOOLS.has(toolName)) {
        return res.status(404).json({ error: `Unknown tool: ${toolName}` });
    }

    try {
        const result = await invokeStitchTool(toolName, args);
        res.json({ result });
    } catch (err) {
        console.error(`[harness] Tool ${toolName} error:`, err.message);
        res.status(500).json({ error: err.message });
    }
});

async function executeKbSearch(query, kbType, userId, kbAccess, kbFlags) {
    // ADO#3316 — Preference gate: check KbFlags BEFORE entitlement check
    if (kbFlags !== null && kbFlags !== undefined) {
        if (kbType === 'corp') {
            const corpEnabled = kbFlags.CorpKbEnabled ?? kbFlags.corpKbEnabled ?? null;
            if (corpEnabled === false) {
                console.warn(`[harness] executeKbSearch: corp KB disabled in user preferences for userId=${userId}`);
                return { text: 'Knowledge base access not authorized.', sources: [] };
            }
        }
        if (kbType === 'personal') {
            const personalEnabled = kbFlags.PersonalKbEnabled ?? kbFlags.personalKbEnabled ?? null;
            if (personalEnabled === false) {
                console.warn(`[harness] executeKbSearch: personal KB disabled in user preferences for userId=${userId}`);
                return { text: 'Knowledge base access not authorized.', sources: [] };
            }
        }
        if (kbType === 'team') {
            const teamEnabled = kbFlags.TeamKbEnabled ?? kbFlags.teamKbEnabled ?? null;
            if (teamEnabled === false) {
                console.warn(`[harness] executeKbSearch: team KB disabled in user preferences for userId=${userId}`);
                return { text: 'Knowledge base access not authorized.', sources: [] };
            }
        }
    } else {
        // kbFlags is null/undefined — ADO#3350: fail-CLOSED for personal/team, fail-open for corp
        if (kbType === 'personal') {
            console.warn(`[harness] executeKbSearch: kbFlags null — blocking personal KB for userId=${userId}`);
            return { text: 'Knowledge base access not authorized.', sources: [] };
        }
        if (kbType === 'team') {
            console.warn(`[harness] executeKbSearch: kbFlags null — blocking team KB for userId=${userId}`);
            return { text: 'Knowledge base access not authorized.', sources: [] };
        }
        // corp: fail-open acceptable — shared resource, no per-user filter
        console.debug(`[harness] executeKbSearch: kbFlags null — allowing corp KB for userId=${userId}`);
    }

    // ADO#3309 — Access enforcement: verify user is entitled to the requested kbType
    if (kbAccess) {
        if (kbType === 'corp' && !kbAccess.corpEnabled) {
            console.warn(`[harness] executeKbSearch: corp KB not authorized for userId=${userId}`);
            return { text: 'Knowledge base access not authorized.', sources: [] };
        }
        if (kbType === 'personal' && (!kbAccess.personalUserId || kbAccess.personalUserId !== userId)) {
            console.warn(`[harness] executeKbSearch: personal KB userId mismatch for userId=${userId}`);
            return { text: 'Knowledge base access not authorized.', sources: [] };
        }
        if (kbType === 'team') {
            if (!kbAccess.authorizedTeamIds || kbAccess.authorizedTeamIds.length === 0) {
                console.warn(`[harness] executeKbSearch: no authorized teams for userId=${userId}`);
                return { text: 'Knowledge base access not authorized.', sources: [] };
            }
        }
    } else {
        // fail-open is ONLY safe for corp (no per-user filter needed)
        // personal/team require verified entitlements — deny if unavailable
        if (kbType === 'personal' || kbType === 'team') {
            console.warn(`[harness] executeKbSearch: kbAccess unavailable — denying ${kbType} KB access for userId=${userId}`);
            return { text: 'Knowledge base access verification unavailable. Please try again.', sources: [] };
        }
        // corp falls through to unfiltered retrieve (corp KB is org-wide, no per-user filter needed)
    }

    // For team KB: use authorizedTeamIds from kbAccess (not model input)
    // For personal KB: use personalUserId from kbAccess (not model input)
    if (kbType === 'team' && kbAccess?.authorizedTeamIds?.length > 0) {
        // ADO#3446: intersect authorized teams with user-selected teams from kbFlags
        const selectedTeamIds = (kbFlags?.TeamIds ?? kbFlags?.teamIds) ?? null;
        const effectiveTeamIds = selectedTeamIds && selectedTeamIds.length > 0
            ? kbAccess.authorizedTeamIds.filter(id => selectedTeamIds.includes(id))
            : kbAccess.authorizedTeamIds; // fall back to all authorized if no selection (teamKbEnabled=true, teamIds=null)
        console.log(`[harness] executeKbSearch: team KB effectiveTeamIds=${JSON.stringify(effectiveTeamIds)} (selected=${JSON.stringify(selectedTeamIds)}, authorized=${JSON.stringify(kbAccess.authorizedTeamIds)}) for userId=${userId}`);
        // Retrieve from each authorized team and merge results
        const kbId = process.env.TEAM_KB_ID;
        if (!kbId) {
            console.warn(`[harness] KB search: no TEAM_KB_ID configured`);
            return { text: 'No knowledge base configured for type: team', sources: [] };
        }
        try {
            const allResults = [];
            for (const teamId of effectiveTeamIds) {
                const results = await retrieveFromKbFiltered(kbId, query, 'teamId', teamId, 5);
                allResults.push(...results);
            }
            if (allResults.length === 0) return { text: 'No results found.', sources: [] };
            const text = allResults.map((r, i) => `[${i+1}] ${r.content?.text || ''}`).join('\n\n');
            const sources = allResults.map(r => ({
                title: (r.location?.s3Location?.uri || r.location?.confluenceLocation?.url || '').split('/').pop() || 'Document',
                excerpt: (r.content?.text || '').substring(0, 200)
            }));
            return { text, sources };
        } catch (err) {
            console.error(`[harness] KB search error (team):`, err.message);
            return { text: `KB search failed: ${err.message}`, sources: [] };
        }
    }

    if (kbType === 'personal' && kbAccess?.personalUserId) {
        const kbId = process.env.PERSONAL_KB_ID;
        if (!kbId) {
            console.warn(`[harness] KB search: no PERSONAL_KB_ID configured`);
            return { text: 'No knowledge base configured for type: personal', sources: [] };
        }
        try {
            const results = await retrieveFromKbFiltered(kbId, query, 'ownerId', kbAccess.personalUserId, 5);
            if (results.length === 0) return { text: 'No results found.', sources: [] };
            const text = results.map((r, i) => `[${i+1}] ${r.content?.text || ''}`).join('\n\n');
            const sources = results.map(r => ({
                title: (r.location?.s3Location?.uri || r.location?.confluenceLocation?.url || '').split('/').pop() || 'Document',
                excerpt: (r.content?.text || '').substring(0, 200)
            }));
            return { text, sources };
        } catch (err) {
            console.error(`[harness] KB search error (personal):`, err.message);
            return { text: `KB search failed: ${err.message}`, sources: [] };
        }
    }

    // Corp KB or fail-open fallback (no kbAccess): use original RetrieveCommand (no filter)
    const kbId = kbType === 'corp' ? process.env.CORP_KB_ID
               : kbType === 'team' ? process.env.TEAM_KB_ID
               : process.env.PERSONAL_KB_ID;

    if (!kbId) {
        console.warn(`[harness] KB search: no KB ID configured for type ${kbType}`);
        return { text: `No knowledge base configured for type: ${kbType}`, sources: [] };
    }

    try {
        const cmd = new RetrieveCommand({
            knowledgeBaseId: kbId,
            retrievalQuery: { text: query },
            retrievalConfiguration: {
                vectorSearchConfiguration: { numberOfResults: 5 }
            }
        });
        const resp = await bedrockAgentClient.send(cmd);
        const results = resp.retrievalResults || [];
        if (results.length === 0) return { text: 'No results found.', sources: [] };
        const text = results.map((r, i) => `[${i+1}] ${r.content?.text || ''}`).join('\n\n');
        const sources = results.map(r => ({
            title: (r.location?.s3Location?.uri || r.location?.confluenceLocation?.url || '').split('/').pop() || 'Document',
            excerpt: (r.content?.text || '').substring(0, 200)
        }));
        return { text, sources };
    } catch (err) {
        console.error(`[harness] KB search error:`, err.message);
        return { text: `KB search failed: ${err.message}`, sources: [] };
    }
}

const ARTIFACT_EXTENSIONS = ['.docx', '.xlsx', '.pptx', '.html', '.pdf', '.zip'];
const ARTIFACT_TYPES = { '.docx': 'word', '.xlsx': 'excel', '.pptx': 'powerpoint', '.html': 'html', '.pdf': 'pdf', '.zip': 'zip' };

async function scanAndUploadArtifacts(userId, workspaceDir, conversationId) {
    const artifactsDir = path.join(workspaceDir, 'artifacts');
    if (!fs.existsSync(artifactsDir)) return null;

    const files = fs.readdirSync(artifactsDir);
    const artifacts = files.filter(f => ARTIFACT_EXTENSIONS.includes(path.extname(f).toLowerCase()));
    if (artifacts.length === 0) return null;

    // Upload the most recently modified artifact
    const latestFile = artifacts
        .map(f => ({ name: f, mtime: fs.statSync(path.join(artifactsDir, f)).mtime }))
        .sort((a, b) => b.mtime - a.mtime)[0];

    const filePath = path.join(artifactsDir, latestFile.name);
    const fileBuffer = fs.readFileSync(filePath);
    const ext = path.extname(latestFile.name).toLowerCase();

    const contentTypes = {
        '.docx': 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
        '.xlsx': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
        '.pptx': 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
        '.html': 'text/html',
        '.pdf': 'application/pdf',
        '.zip': 'application/zip',
    };

    // Resolve or create the user's 'general' folder
    let folderGuid;
    {
        const conn = await getDbConnection();
        try {
            const [existing] = await conn.execute(
                "SELECT id FROM workspace_folders WHERE user_id = ? AND name = 'general' LIMIT 1",
                [userId]
            );
            if (existing.length > 0) {
                folderGuid = existing[0].id;
            } else {
                folderGuid = crypto.randomUUID();
                await conn.execute(
                    'INSERT INTO workspace_folders (id, user_id, name, s3_prefix, created_at) VALUES (?, ?, ?, ?, NOW(6))',
                    [folderGuid, userId, 'general', `files/${folderGuid}/`]
                );
            }
        } finally {
            conn.end();
        }
    }

    const s3Key = `workspaces/${userId}/files/${folderGuid}/${latestFile.name}`;

    await s3Client.send(new PutObjectCommand({
        Bucket: S3_BUCKET,
        Key: s3Key,
        Body: fileBuffer,
        ContentType: contentTypes[ext] || 'application/octet-stream',
    }));

    // Insert DB row so artifact appears in workspace explorer
    try {
        const conn = await getDbConnection();
        try {
            const fileId = crypto.randomUUID();
            const now = new Date().toISOString().slice(0, 19).replace('T', ' ');
            await conn.execute(
                'INSERT INTO user_workspace_uploads (id, user_id, folder_id, filename, mime_type, s3_key, size_bytes, created_at, current_version, source, conversation_id) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)',
                [fileId, userId, folderGuid, latestFile.name, contentTypes[ext] || 'application/octet-stream', s3Key, fileBuffer.length, now, 1, 'assistant', conversationId ?? null]
            );
        } finally {
            conn.end();
        }
    } catch (dbErr) {
        console.error('[harness] scanAndUploadArtifacts DB insert failed (non-fatal):', dbErr.message);
    }

    console.log(`[harness] Uploaded artifact ${latestFile.name} to s3://${S3_BUCKET}/${s3Key}`);
    return {
        s3Key,
        fileName: latestFile.name,
        artifactType: ARTIFACT_TYPES[ext] || 'file',
        artifactUrl: `s3://${S3_BUCKET}/${s3Key}`,
    };
}

/**
 * Classify whether a request should use the CC task path vs Bedrock conversational path.
 * Returns true = CC, false = Bedrock.
 */

// ADO#3531 — Prune toolResult content beyond last 10 turns
function pruneToolResults(messages, windowSize = 10) {
    const STUB = '[result from prior session — call tool again for fresh data]';
    const pruneBeforeIndex = Math.max(0, messages.length - windowSize);
    return messages.map((msg, idx) => {
        if (idx >= pruneBeforeIndex) return msg; // within window — keep verbatim
        // Check if this message has any toolResult content blocks
        if (!Array.isArray(msg.content)) return msg;
        const hasToolResult = msg.content.some(block => block.toolResult !== undefined);
        if (!hasToolResult) return msg;
        // Replace toolResult content with stub — preserve structure
        return {
            ...msg,
            content: msg.content.map(block => {
                if (block.toolResult === undefined) return block;
                return {
                    toolResult: {
                        ...block.toolResult,
                        content: [{ text: STUB }]
                    }
                };
            })
        };
    });
}

function classifyRequest(message, history) {
    const msg = (message || '').toLowerCase();

    // File extension signals — always CC regardless
    const fileExtensions = /\.(docx|xlsx|pptx|csv|pdf|py|js|ts|json|yaml|xml)\b/i;
    if (fileExtensions.test(msg)) return true;

    // Action verbs (strong CC signals)
    const actionVerbs = /\b(create|build|generate|write|make|produce|analyze|run|execute|compile|draft|develop|implement|code|script|automate)\b/i;

    // Scope signals (multi-step work)
    const scopeSignals = /\b(multi.?step|comprehensive|full|complete|entire|all|section|chapter|report|document|presentation|spreadsheet|dataset)\b/i;

    // Long message with action verb = CC candidate
    if (message.length > 200 && actionVerbs.test(msg)) return true;

    // Action verb + scope signal
    if (actionVerbs.test(msg) && scopeSignals.test(msg)) return true;

    // Prior turn was CC task = stay on CC path
    if (Array.isArray(history) && history.length > 0) {
        const lastTurn = history[history.length - 1];
        if (lastTurn?.role === 'assistant' && lastTurn?.wasCC === true) return true;
    }

    return false;
}

// ─── Preference signal detection (ADO#3093) ───────────────────────────────
const PREFERENCE_PATTERNS = [
    /\bi prefer\b/i,
    /\balways use\b/i,
    /\bcall me\b/i,
    /\bi like\b/i,
    /\bi work in\b/i,
    /\bi am a\b/i,
    /\bmy name is\b/i,
    /\bi want you to\b/i,
    /\bplease always\b/i,
    /\bdon't use\b/i,
    /\bnever use\b/i,
];

function hasPreferenceSignal(text) {
    return PREFERENCE_PATTERNS.some(p => p.test(text));
}

function firePreferenceWrite(userId, message) {
    const headers = { 'Content-Type': 'application/json' };
    if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
    fetch(`${FAIT_BASE_URL}/api/memory/write`, {
        method: 'POST',
        headers,
        body: JSON.stringify({
            userId,
            topicSlug: 'user-preferences',
            content: message,
            source: 'preference-detection',
        }),
    }).catch(err => console.error('[harness] preference-write error:', err.message));
}

// ─── ADO#3559: Task folder model helpers ──────────────────────────────────

function buildLocalSnapshot(dir) {
    // Returns Map<relativePath, {size, mtime}> for all files in dir (recursive)
    const result = new Map();
    if (!fs.existsSync(dir)) return result;
    function walk(current, base) {
        for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
            const full = path.join(current, entry.name);
            const rel = path.relative(base, full);
            if (entry.isDirectory()) walk(full, base);
            else {
                const stat = fs.statSync(full);
                result.set(rel, { size: stat.size, mtime: stat.mtimeMs });
            }
        }
    }
    walk(dir, dir);
    return result;
}

function findDirtyFiles(before, after) {
    // Returns array of relative paths that are new or changed
    const dirty = [];
    for (const [relPath, afterStat] of after) {
        const beforeStat = before.get(relPath);
        if (!beforeStat || beforeStat.size !== afterStat.size || beforeStat.mtime !== afterStat.mtime) {
            dirty.push(relPath);
        }
    }
    return dirty;
}

// ADO#3560 — in-memory map for pending folder confirmations (keyed by conversationId)
// Entry: { resolve, reject, userId }
const folderConfirmMap = new Map();

// ADO#3566 — grace window for late confirms (keyed by conversationId → timestamp resolved)
// If a confirm arrives within 5s of the entry being resolved/expired, accept gracefully
const _recentlyResolvedConfirms = new Map();
const FOLDER_CONFIRM_GRACE_MS = 5000;

function markConfirmResolved(conversationId) {
    _recentlyResolvedConfirms.set(conversationId, Date.now());
    // Cleanup stale entries older than 10s
    const cutoff = Date.now() - 10000;
    for (const [key, ts] of _recentlyResolvedConfirms) {
        if (ts < cutoff) _recentlyResolvedConfirms.delete(key);
    }
}

/**
 * Normalize s3_prefix to always include workspaces/<userId>/ prefix.
 * Old records store just files/<folderId>/ — normalize them on read.
 */
function normalizeS3Prefix(s3Prefix, userId) {
    if (!s3Prefix) return `workspaces/${userId}/files/`;
    // If already starts with workspaces/, it's correct
    if (s3Prefix.startsWith('workspaces/')) return s3Prefix;
    // Otherwise, prepend workspaces/<userId>/
    return `workspaces/${userId}/${s3Prefix}`;
}

async function resolveTaskFolder(userId, taskFolderId) {
    // 1. If taskFolderId provided → verify it exists for this user
    if (taskFolderId) {
        const conn = await getDbConnection();
        try {
            const [rows] = await conn.execute(
                'SELECT id, name, s3_prefix FROM workspace_folders WHERE id = ? AND user_id = ? LIMIT 1',
                [taskFolderId, userId]
            );
            if (rows.length > 0) {
                console.log(`[harness] resolveTaskFolder: found folder id=${taskFolderId} name=${rows[0].name}`);
                const r0 = rows[0];
                return { id: String(r0.id), name: String(r0.name), s3_prefix: normalizeS3Prefix(String(r0.s3_prefix), userId) };
            }
            console.warn(`[harness] resolveTaskFolder: taskFolderId=${taskFolderId} not found for userId=${userId}, falling back`);
        } finally {
            conn.end();
        }
    }
    // 2. Try last_task_folder_id from users table
    {
        const conn = await getDbConnection();
        try {
            const [userRows] = await conn.execute(
                'SELECT last_task_folder_id FROM users WHERE id = ? LIMIT 1',
                [userId]
            );
            if (userRows.length > 0 && userRows[0].last_task_folder_id) {
                const lastFolderId = userRows[0].last_task_folder_id;
                const [folderRows] = await conn.execute(
                    'SELECT id, name, s3_prefix FROM workspace_folders WHERE id = ? AND user_id = ? LIMIT 1',
                    [lastFolderId, userId]
                );
                if (folderRows.length > 0) {
                    console.log(`[harness] resolveTaskFolder: using last_task_folder_id=${lastFolderId} name=${folderRows[0].name}`);
                    const r1 = folderRows[0];
                    return { id: String(r1.id), name: String(r1.name), s3_prefix: normalizeS3Prefix(String(r1.s3_prefix), userId) };
                }
            }
        } finally {
            conn.end();
        }
    }
    // 3. Look up or create /general/ folder
    {
        const conn = await getDbConnection();
        try {
            const [existing] = await conn.execute(
                "SELECT id, name, s3_prefix FROM workspace_folders WHERE user_id = ? AND name = 'general' LIMIT 1",
                [userId]
            );
            if (existing.length > 0) {
                console.log(`[harness] resolveTaskFolder: using existing general folder id=${existing[0].id}`);
                const r2 = existing[0];
                return { id: String(r2.id), name: String(r2.name), s3_prefix: normalizeS3Prefix(String(r2.s3_prefix), userId) };
            }
            // Create it
            const newId = crypto.randomUUID();
            const s3Prefix = `workspaces/${userId}/files/${newId}/`;
            await conn.execute(
                'INSERT INTO workspace_folders (id, user_id, name, s3_prefix, created_at) VALUES (?, ?, ?, ?, NOW(6))',
                [newId, userId, 'general', s3Prefix]
            );
            console.log(`[harness] resolveTaskFolder: created general folder id=${newId} for userId=${userId}`);
            return { id: newId, name: 'general', s3_prefix: normalizeS3Prefix(s3Prefix, userId) };
        } finally {
            conn.end();
        }
    }
}

async function getWorkspaceManifest(userId) {
    try {
        const conn = await getDbConnection();
        try {
            const [folders] = await conn.execute(
                'SELECT wf.name, COUNT(wfi.id) as file_count FROM workspace_folders wf LEFT JOIN user_workspace_uploads wfi ON wfi.folder_id = wf.id WHERE wf.user_id = ? GROUP BY wf.id, wf.name ORDER BY wf.name',
                [userId]
            );
            if (folders.length === 0) return null;
            return folders.map(f => `- ${f.name} (${f.file_count} file${f.file_count !== 1 ? 's' : ''})`).join('\n');
        } finally {
            conn.end();
        }
    } catch (err) {
        console.warn(`[harness] getWorkspaceManifest failed (non-fatal): ${err.message}`);
        return null;
    }
}

// ADO#3560 — Folder picker confirmation endpoint
app.post('/turn/folder-confirm', async (req, res) => {
    const { conversationId, folderId, newFolderName, readOnlyFolderIds } = req.body || {};
    const roIds = Array.isArray(readOnlyFolderIds) ? readOnlyFolderIds : [];
    console.log(`[harness] /turn/folder-confirm: conversationId=${conversationId}, folderId=${folderId}, newFolderName=${newFolderName}, readOnlyFolderIds=${JSON.stringify(roIds)}`);

    if (!conversationId) {
        return res.status(400).json({ error: 'conversationId required' });
    }

    const pending = folderConfirmMap.get(conversationId);
    if (!pending) {
        // ADO#3566 — grace window: if this was recently resolved (within 5s), accept as duplicate gracefully
        const resolvedAt = _recentlyResolvedConfirms.get(conversationId);
        if (resolvedAt && (Date.now() - resolvedAt) < FOLDER_CONFIRM_GRACE_MS) {
            console.warn(`[harness] /turn/folder-confirm: late/duplicate confirm for conversationId=${conversationId} (resolved ${Date.now() - resolvedAt}ms ago) — returning already_resolved`);
            return res.json({ ok: true, status: 'already_resolved' });
        }
        return res.status(404).json({ error: 'No pending folder selection for this conversation' });
    }

    const { resolve, reject, userId: pendingUserId } = pending;
    let resolvedFolderId = folderId;

    try {
        if (newFolderName) {
            if (!/^[a-zA-Z0-9_-]{1,64}$/.test(newFolderName)) {
                return res.status(400).json({ error: 'Invalid folder name' });
            }
            const newId = crypto.randomUUID();
            const s3Prefix = `workspaces/${pendingUserId}/files/${newId}/`;
            const conn = await getDbConnection();
            try {
                await conn.execute(
                    'INSERT INTO workspace_folders (id, user_id, name, s3_prefix, created_at) VALUES (?, ?, ?, ?, NOW(6))',
                    [newId, pendingUserId, newFolderName, s3Prefix]
                );
            } finally {
                conn.end();
            }
            resolvedFolderId = newId;
            console.log(`[harness] /turn/folder-confirm: created new folder id=${newId} name=${newFolderName} for userId=${pendingUserId}`);
        }

        if (!resolvedFolderId) {
            return res.status(400).json({ error: 'folderId or newFolderName required' });
        }

        folderConfirmMap.delete(conversationId);
        markConfirmResolved(conversationId);  // ADO#3566: track for grace window
        resolve({ folderId: resolvedFolderId, readOnlyFolderIds: roIds });
        res.json({ ok: true, folderId: resolvedFolderId });
    } catch (err) {
        console.error(`[harness] /turn/folder-confirm error: ${err.message}`);
        folderConfirmMap.delete(conversationId);
        markConfirmResolved(conversationId);  // ADO#3566: track for grace window even on error
        reject(err);
        res.status(500).json({ error: err.message });
    }
});

app.post('/turn', async (req, res) => {
    console.log('[harness] /turn received: userId=%s, hasMessage=%s, taskMode=%s',
        req.body?.UserId ?? '(none)', !!req.body?.Message, req.body?.TaskMode ?? false);
    console.log(`[harness] /turn: request received. body keys=${Object.keys(req.body || {}).join(',')}, contentType=${req.headers['content-type']}`);
    const rawBody = req.body || {};
    console.log(`[harness] /turn: raw body dump: ${scrubSecrets(JSON.stringify(rawBody).substring(0, 500))}`);

    // Support both PascalCase (legacy) and camelCase (JsonContent.Create default) field names
    const sessionId   = rawBody.SessionId   ?? rawBody.sessionId;
    const userId      = rawBody.UserId      ?? rawBody.userId;
    const message     = rawBody.Message     ?? rawBody.message;
    const systemPrompt= rawBody.SystemPrompt?? rawBody.systemPrompt;
    const history     = rawBody.History     ?? rawBody.history;
    // ADO#3249 — read TaskMode from both field names (Blazor TurnRequest serializes as TaskMode)
    const forceTaskMode = rawBody.ForceTaskMode ?? rawBody.force_task_mode ?? rawBody.TaskMode ?? rawBody.taskMode ?? false;
    const pluginAgentId = rawBody.PluginAgentId ?? rawBody.pluginAgentId ?? null;
    const userEmail       = rawBody.UserEmail       ?? rawBody.userEmail       ?? null;
    const isScheduledTask = rawBody.IsScheduledTask ?? rawBody.isScheduledTask ?? false;
    const kbWriteAllowed  = rawBody.KbWriteAllowed  ?? rawBody.kbWriteAllowed  ?? true;
    const taskFolderId    = rawBody.TaskFolderId    ?? rawBody.taskFolderId    ?? null;
    // ADO#3395 — per-turn model override; null falls back to MODEL_ID env constant
    const modelId = rawBody.Model ?? rawBody.model ?? process.env.MODEL_ID ?? MODEL_ID;

    // ADO#3442/3443: Normalize short model IDs to Bedrock cross-region inference ARNs.
    // ChatView sends ModelInfo.Id (e.g. "claude-sonnet-4-6"); Bedrock needs the full ARN.
    const BEDROCK_MODEL_MAP = {
        'claude-sonnet-4-6':  'us.anthropic.claude-sonnet-4-6',
        'claude-opus-4-6':    'us.anthropic.claude-opus-4-6-v1',
        'claude-haiku-4-5':   'us.anthropic.claude-haiku-4-5-20251001-v1:0',
        'haiku':              'us.anthropic.claude-haiku-4-5-20251001-v1:0',
    };
    const resolvedModelId = BEDROCK_MODEL_MAP[modelId] ?? modelId;

    const conversationId  = rawBody.ConversationId  ?? rawBody.conversationId  ?? '';
    const turnIndex = Array.isArray(history) ? history.length : 0;
    const enabledMcpSlugs = rawBody.EnabledMcpSlugs ?? rawBody.enabledMcpSlugs ?? [];
    // ADO#3249 — scheduled tasks with MCP slugs must use Bedrock path so toolConfig is built.
    // classifyRequest or TaskMode=true would otherwise route them to CC spawn path where
    // toolConfig is never constructed and graph_* tools are invisible to the model.
    const hasMcpTools = Array.isArray(enabledMcpSlugs) && enabledMcpSlugs.length > 0;
    const taskMode = hasMcpTools && isScheduledTask
        ? false  // force Bedrock path — MCP tools require toolConfig, not CC text context
        : (forceTaskMode || classifyRequest(message, history));

    // §G7 — track scheduled task context so requireApproval uses async-safe path
    if (isScheduledTask === true && userId) {
        scheduledTaskUsers.add(userId);
    } else if (userId) {
        scheduledTaskUsers.delete(userId);
    }
    console.log(`[harness] /turn: destructured: userId=${userId}, messageLen=${message?.length}, forceTaskMode=${forceTaskMode}, classifiedTaskMode=${taskMode}, isScheduledTask=${isScheduledTask}, enabledMcpSlugs=[${enabledMcpSlugs.join(',')}], hasMcpTools=${hasMcpTools}, historyLen=${Array.isArray(history) ? history.length : 'n/a'}, sessionId=${sessionId}`);

    if (!userId || !message) {
        console.warn(`[harness] /turn: 400 — userId=${userId}, message=${!!message} — 'userId and message required'`);
        return res.status(400).json({ error: 'userId and message required' });
    }
    if (typeof userId !== 'string' ||
        userId.includes('..') || userId.includes('/') || userId.includes('\\') ||
        !/^[a-zA-Z0-9_-]{1,64}$/.test(userId)) {
        console.warn(`[harness] /turn: 400 — userId failed validation: '${userId}'`);
        return res.status(400).json({ error: 'Invalid userId' });
    }

    // ADO#3240 — Pre-fetch user tokens from Blazor (decrypted MS365 + ADO) for this turn
    // ADO#3299 — getUserTokens must be called unconditionally on every /turn
    const normalizedUserId = (userId || '').trim().toLowerCase();
    const userTokens = await getUserTokens(normalizedUserId);
    console.log(`[harness] /turn: getUserTokens success for userId=${normalizedUserId}, ms365=${!!userTokens?.ms365}, ado=${!!userTokens?.ado}`);

    console.log(`[harness] /turn: validation passed for userId=${normalizedUserId}, starting SSE response`);
    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');

    const sendEvent = (data) => {
        console.log(`[harness] /turn: sendEvent type=${data.type}, contentLen=${data.content?.length ?? 0}, errorMessage=${data.errorMessage ?? ''}`);
        res.write(`data: ${JSON.stringify(data)}\n\n`);
    };

    // §6.1 — load plugin agent soul if pluginAgentId specified
    let pluginAgentSoul = null;
    if (pluginAgentId) {
        try {
            const headers = { 'Accept': 'application/json' };
            if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
            const soulResp = await fetch(`${FAIT_BASE_URL}/api/agents/${encodeURIComponent(pluginAgentId)}/soul`, { headers });
            if (soulResp.ok) {
                const soulData = await soulResp.json();
                if (soulData?.content) pluginAgentSoul = soulData.content;
            } else {
                console.warn(`[harness] /turn: could not load soul for pluginAgentId=${pluginAgentId} — status=${soulResp.status}`);
            }
        } catch (soulErr) {
            console.warn(`[harness] /turn: pluginAgentId soul fetch failed: ${soulErr.message}`);
        }
    }

    // ─── Resumption brief ─────────────────────────────────────────────────────
    if (message === '__resumption_brief__') {
        console.log(`[harness] /turn: resumption brief requested for userId=${userId}`);
        try {
            // Get MEMORY.md last-modified timestamp from S3
            let memoryTimestamp = null;
            try {
                const memKey = `${S3_PREFIX}workspaces/${userId}/memory/MEMORY.md`;
                const headCmd = new HeadObjectCommand({ Bucket: S3_BUCKET, Key: memKey });
                const headResp = await s3Client.send(headCmd);
                memoryTimestamp = headResp.LastModified ? new Date(headResp.LastModified).toLocaleDateString('en-US', { month: 'short', day: 'numeric' }) : null;
            } catch (e) {
                console.warn(`[harness] resumption brief: could not get MEMORY.md timestamp: ${e.message}`);
            }

            // Skip brief entirely if no history and no memory (ADO#3155 Bug 1 fix)
            const hasHistory = Array.isArray(history) && history.length > 0;
            if (!hasHistory && !memoryTimestamp) {
                console.log(`[harness] resumption brief: no history and no MEMORY.md for userId=${userId} — skipping brief`);
                sendEvent({ type: 'done', exitCode: 0 });
                res.end();
                return;
            }

            // Generate contextual summary via Bedrock if history is available
            if (hasHistory) {
                // Build formatted transcript of last 6 messages
                const recentMsgs = history.slice(-6);
                const transcript = recentMsgs.map(m => {
                    const role = m.Role ?? m.role ?? 'unknown';
                    const content = m.Content ?? m.content ?? '';
                    const label = role === 'user' ? 'User' : 'Assistant';
                    return `${label}: ${content.substring(0, 300)}`;
                }).join('\n');

                const summaryPrompt = `You are summarizing a past conversation to help the user remember context.\nBased on these recent messages:\n${transcript}\n\nWrite exactly one sentence starting with "Last time" that summarizes what we were working on. Be specific. Example: "Last time we were debugging the harness SSE pipeline and working on the KB retrieval refactor." Reply with only the sentence, no extra text.`;

                try {
                    const summaryCmd = new ConverseStreamCommand({
                        modelId: resolvedModelId,
                        messages: [{ role: 'user', content: [{ text: summaryPrompt }] }],
                        inferenceConfig: { maxTokens: 80, temperature: 0.3 }
                    });
                    const summaryResp = await bedrockClient.send(summaryCmd);
                    let summaryText = '';
                    for await (const chunk of summaryResp.stream) {
                        if (chunk.contentBlockDelta?.delta?.text) {
                            summaryText += chunk.contentBlockDelta.delta.text;
                        }
                    }
                    summaryText = summaryText.trim();
                    if (summaryText) {
                        sendEvent({ type: 'text', content: summaryText + '\n' });
                    }
                } catch (summaryErr) {
                    console.warn(`[harness] resumption brief: Bedrock summary failed — ${summaryErr.message}`);
                    // Fallback: echo last user message truncated
                    const lastUserTurn = [...history].reverse().find(h => h.Role === 'user' || h.role === 'user');
                    if (lastUserTurn) {
                        const content = lastUserTurn.Content ?? lastUserTurn.content ?? '';
                        const truncated = content.length > 80 ? content.substring(0, 80) + '...' : content;
                        sendEvent({ type: 'text', content: `Last time: ${truncated}\n` });
                    }
                }
            }

            // Append memory timestamp if available
            if (memoryTimestamp) {
                sendEvent({ type: 'text', content: `Memory synced: ${memoryTimestamp}\n` });
            }

            sendEvent({ type: 'done', exitCode: 0 });
        } catch (briefErr) {
            console.error(`[harness] resumption brief error: ${briefErr.message}`);
            sendEvent({ type: 'error', errorMessage: 'Could not load resumption brief.' });
        }
        res.end();
        return;
    }
    // ─── End resumption brief ──────────────────────────────────────────────────

    if (taskMode) {
        console.log(`[harness] /turn: taskMode=true — entering CC spawn path for userId=${userId}`);
        sendEvent({ type: 'mode_switch', payload: JSON.stringify({ reason: 'task_mode' }) });
        sendEvent({ type: 'task_progress', payload: JSON.stringify({ step: 'start', status: 'starting', message: 'Starting Claude Code task...' }) });
        let ended = false;
        const endResponse = (data) => {
            if (ended) return;
            ended = true;
            sendEvent(data);
            res.end();
        };

        // ADO#3576 — 9.2 Pre-Task Confirmation Gate
        // ADO#3913: fires for all taskMode=true turns (explicit ForceTaskMode OR auto-classified), except scheduled tasks
        if (taskMode === true && isScheduledTask !== true) {
            console.log(`[harness] ADO#3576: task gate firing for userId=${userId}, taskMode=${taskMode}`);

            // Load S3 context for gate assessment (same files as regular Bedrock path)
            const gatePrefix = S3_PREFIX || `workspaces/${userId}/`;
            const [gateSoulMd, gateUserMd, gateMemoryMd] = await Promise.all([
                fetchS3File(`${gatePrefix}assistants/SOUL.md`),
                fetchS3File(`${gatePrefix}assistants/USER.md`),
                fetchS3File(`${gatePrefix}memory/MEMORY.md`),
            ]);

            // Build gate system prompt
            const gateSystemParts = [];
            if (pluginAgentSoul) {
                gateSystemParts.push(`## Plugin Agent Identity\n${pluginAgentSoul}`);
            } else if (gateSoulMd) {
                gateSystemParts.push(`## Assistant Identity\n${gateSoulMd}`);
            }
            if (gateUserMd) gateSystemParts.push(`## About the User\n${gateUserMd}`);
            if (gateMemoryMd) gateSystemParts.push(`## Long-Term Memory\n${gateMemoryMd}`);
            if (systemPrompt) gateSystemParts.push(systemPrompt);

            // Inject task gate instructions at end of system prompt
            gateSystemParts.push(`[TASK GATE — read carefully]
You are the FAIT assistant. When Task mode is active, you have the ability to launch Claude Code CLI to execute coding tasks on the user's behalf.
The user has indicated they want to execute a task. Your ONLY job right now is to assess whether you have sufficient requirements to proceed. Do NOT discuss or question your own capabilities.
- If you have a clear objective, know what files/data are involved, and can execute without guessing: write a one-sentence confirmation of what you will do, then end your response with [TASK_PROCEED] on its own line.
- If you need more information: ask your one clarifying question conversationally, then end your response with [TASK_HOLD] on its own line.
You MUST end every response with exactly one of [TASK_PROCEED] or [TASK_HOLD] on the final line. No other ending is acceptable. [TASK_PROCEED] and [TASK_HOLD] will be stripped before display.`);

            const gateSystemPrompt = gateSystemParts.join('\n\n---\n\n');

            // Build gate messages (include history for context)
            const gateMessages = [];
            if (Array.isArray(history)) {
                for (const h of history) {
                    if (h.role && h.content) {
                        gateMessages.push({
                            role: h.role === 'assistant' ? 'assistant' : 'user',
                            content: [{ text: typeof h.content === 'string' ? h.content : (h.Content || '(empty)') }]
                        });
                    }
                }
            }
            gateMessages.push({ role: 'user', content: [{ text: message }] });

            // Coalesce consecutive same-role messages (Bedrock constraint)
            const coalescedGateMessages = [];
            for (const msg of gateMessages) {
                const last = coalescedGateMessages[coalescedGateMessages.length - 1];
                if (last && last.role === msg.role) {
                    const existingText = last.content[0]?.text || '';
                    const newText = msg.content[0]?.text || '';
                    last.content[0].text = `${existingText}\n${newText}`.trim();
                } else {
                    coalescedGateMessages.push(JSON.parse(JSON.stringify(msg))); // deep copy
                }
            }

            let gateResponseText = '';
            try {
                console.log(`[harness] ADO#3924: calling Bedrock gate assessment, modelId=${resolvedModelId}, messages=${coalescedGateMessages.length}`);
                const gateCmd = new ConverseStreamCommand({
                    modelId: resolvedModelId,
                    messages: coalescedGateMessages,
                    system: [{ text: gateSystemPrompt }],
                    inferenceConfig: { maxTokens: 512, temperature: 0.5 }
                });
                const gateResp = await bedrockClient.send(gateCmd);
                for await (const event of gateResp.stream) {
                    if (event.contentBlockDelta?.delta?.text) {
                        gateResponseText += event.contentBlockDelta.delta.text;
                    }
                }
                console.log(`[harness] ADO#3924: gate response (len=${gateResponseText.length}): ${gateResponseText.substring(0, 200)}`);
            } catch (gateErr) {
                // Gate call failed — default to HOLD for safety
                console.error(`[harness] ADO#3924: gate assessment failed, defaulting to task_hold: ${gateErr.message}`);
                gateResponseText = '[TASK_HOLD]';
            }

            // Parse sentinels
            const trimmed = gateResponseText.trimEnd();
            const hasTaskProceed = trimmed.endsWith('[TASK_PROCEED]');
            const hasTaskHold = trimmed.endsWith('[TASK_HOLD]');
            const cleanGateResponse = gateResponseText.replace(/\[TASK_PROCEED\]|\[TASK_HOLD\]/g, '').trim();

            if (hasTaskHold) {
                if (hasTaskProceed) {
                    console.warn(`[harness] ADO#3924: gate → both TASK_HOLD and TASK_PROCEED detected, TASK_HOLD takes priority. Raw: ${gateResponseText.substring(0, 200)}`);
                }
                // TASK_HOLD path — model explicitly requested more info
                console.log(`[harness] ADO#3924: gate → TASK_HOLD for userId=${userId}`);

                // Stream the clean response to user
                if (cleanGateResponse) {
                    sendEvent({ type: 'text', content: cleanGateResponse });
                }

                // Emit task_hold SSE event — Blazor will deselect Task toggle
                sendEvent({ type: 'task_hold' });

                endResponse({ type: 'done', exitCode: 0 });
                return;
            }

            if (!hasTaskProceed) {
                // No sentinel — model did not follow the required format.
                // Safe default: TASK_HOLD. Never silently proceed on ambiguous gate output.
                // ADO#4002: clarifying questions without sentinel should hold, not spawn CC.
                console.warn(`[harness] ADO#4002: gate → no sentinel detected, defaulting to TASK_HOLD. Raw gate response: ${gateResponseText}`);

                // Send whatever the model said as a conversational response (may be a clarifying question)
                if (cleanGateResponse) {
                    sendEvent({ type: 'text', content: cleanGateResponse });
                }

                sendEvent({ type: 'task_hold' });
                endResponse({ type: 'done', exitCode: 0 });
                return;
            }

            // TASK_PROCEED path — confirmed by explicit [TASK_PROCEED] sentinel
            console.log(`[harness] ADO#3924: gate → TASK_PROCEED for userId=${userId}`);
            if (cleanGateResponse) {
                sendEvent({ type: 'text', content: cleanGateResponse });
            }
            // Fall through — CC spawn runs below
        }
        // End ADO#3576 gate

        // ADO#3560 — Folder picker: fast-path or emit folder_required and hold
        const isAutoClassified = !forceTaskMode;
        let taskFolderIdResolved = taskFolderId;

        let readOnlyFolderIdsConfirmed = [];  // ADO#3561 — populated by folder confirm or empty on fast-path

        {
            let useFastPath = false;
            if (isAutoClassified) {
                // Fast path: auto-classified AND user has a last_task_folder_id — skip picker
                try {
                    const connFast = await getDbConnection();
                    try {
                        const [fastRows] = await connFast.execute(
                            'SELECT last_task_folder_id FROM users WHERE id = ? LIMIT 1',
                            [userId]
                        );
                        if (fastRows.length > 0 && fastRows[0].last_task_folder_id) {
                            useFastPath = true;
                            if (!taskFolderIdResolved) {
                                const rawFast = fastRows[0].last_task_folder_id;
                                taskFolderIdResolved = rawFast != null ? (rawFast?.toString?.() ?? String(rawFast)) : null;
                            }
                            console.log(`[harness] ADO#3560 fast-path: auto-classified + last_task_folder_id=${taskFolderIdResolved} — skipping folder picker userId=${userId}`);
                        }
                    } finally {
                        connFast.end();
                    }
                } catch (fastErr) {
                    console.warn(`[harness] ADO#3560 fast-path DB check failed (non-fatal): ${fastErr.message}`);
                }
            }

            if (!useFastPath) {
                // Emit folder_required and hold until /turn/folder-confirm
                let folders = [];
                let lastFolderId = null;
                try {
                    const connFolders = await getDbConnection();
                    try {
                        const [folderRows] = await connFolders.execute(
                            'SELECT id, name, last_used_at as lastUsedAt FROM workspace_folders WHERE user_id = ? ORDER BY COALESCE(last_used_at, created_at) DESC LIMIT 50',
                            [userId]
                        );
                        folders = folderRows.map(r => ({
                            id: r.id?.toString?.() ?? String(r.id),
                            name: r.name?.toString?.() ?? String(r.name),
                            lastUsedAt: r.lastUsedAt
                        }));
                        const [userRows] = await connFolders.execute(
                            'SELECT last_task_folder_id FROM users WHERE id = ? LIMIT 1',
                            [userId]
                        );
                        const rawLastFolder = userRows.length > 0 ? userRows[0].last_task_folder_id : null;
                        lastFolderId = rawLastFolder != null ? (rawLastFolder?.toString?.() ?? String(rawLastFolder)) : null;
                    } finally {
                        connFolders.end();
                    }
                } catch (folderFetchErr) {
                    console.warn(`[harness] ADO#3560 folder fetch failed (non-fatal): ${folderFetchErr.message}`);
                }

                // Auto-create /general/ if no folders exist yet (Candidate E fix)
                if (folders.length === 0) {
                    try {
                        const connCreate = await getDbConnection();
                        try {
                            // Check again inside this connection to avoid race
                            const [existingCheck] = await connCreate.execute(
                                "SELECT id, name FROM workspace_folders WHERE user_id = ? AND name = 'general' LIMIT 1",
                                [userId]
                            );
                            if (existingCheck.length > 0) {
                                const existingId = existingCheck[0].id?.toString?.() ?? String(existingCheck[0].id);
                                folders = [{ id: existingId, name: existingCheck[0].name, lastUsedAt: null }];
                                lastFolderId = existingId;
                            } else {
                                const newGeneralId = crypto.randomUUID();
                                const s3Prefix = `workspaces/${userId}/files/${newGeneralId}/`;
                                await connCreate.execute(
                                    'INSERT INTO workspace_folders (id, user_id, name, s3_prefix, created_at) VALUES (?, ?, ?, ?, NOW(6))',
                                    [newGeneralId, userId, 'general', s3Prefix]
                                );
                                folders = [{ id: newGeneralId, name: 'general', lastUsedAt: null }];
                                lastFolderId = newGeneralId;
                                console.log(`[harness] ADO#3565: auto-created /general/ folder id=${newGeneralId} for userId=${userId}`);
                            }
                        } finally {
                            connCreate.end();
                        }
                    } catch (autoCreateErr) {
                        console.warn(`[harness] ADO#3565: auto-create /general/ failed (non-fatal): ${autoCreateErr.message}`);
                    }
                }

                console.log(`[harness] ADO#3918 folder_required: folderCount=${folders.length} lastFolderId=${lastFolderId} conversationId=${conversationId}`);
                sendEvent({ type: 'folder_required', folders, lastFolderId, conversationId });
                console.log(`[harness] ADO#3560 holding for folder-confirm: conversationId=${conversationId} userId=${userId}`);

                try {
                    const folderConfirmResult = await new Promise((resolve, reject) => {
                        // ADO#3569: expire any stale entry for the same userId with a different conversationId
                        for (const [existingConvId, existingEntry] of folderConfirmMap.entries()) {
                            if (existingEntry.userId === userId && existingConvId !== conversationId) {
                                console.warn(`[harness] /turn: expiring stale folderConfirmMap entry for userId=${userId} (old conversationId=${existingConvId})`);
                                folderConfirmMap.delete(existingConvId);
                                markConfirmResolved(existingConvId);
                                if (existingEntry.reject) existingEntry.reject(new Error('Superseded by new turn'));
                            }
                        }
                        folderConfirmMap.set(conversationId, { resolve, reject, userId });
                        setTimeout(() => {
                            if (folderConfirmMap.has(conversationId)) {
                                folderConfirmMap.delete(conversationId);
                                markConfirmResolved(conversationId);  // ADO#3566: track for grace window after timeout
                                reject(new Error('Folder selection timed out (2 min)'));
                            }
                        }, 120000);
                    });
                    taskFolderIdResolved = folderConfirmResult.folderId;
                    const readOnlyFolderIdsFromPicker = folderConfirmResult.readOnlyFolderIds || [];
                    readOnlyFolderIdsConfirmed = readOnlyFolderIdsFromPicker;
                    console.log(`[harness] ADO#3561 folder confirmed: folderId=${taskFolderIdResolved} readOnlyFolderIds=${JSON.stringify(readOnlyFolderIdsFromPicker)} conversationId=${conversationId}`);
                } catch (timeoutErr) {
                    console.warn(`[harness] ADO#3560 folder confirm error: ${timeoutErr.message}`);
                    endResponse({ type: 'error', errorMessage: 'Folder selection timed out. Please try again.' });
                    return;
                }
            }
        }
        // ── CC spawn path (unchanged) ─────────────────────────────────────
        const userWorkspaceDir = `${WORKSPACE_DIR}/${userId}`;
        let folderLocalDir = userWorkspaceDir; // will be updated after folder resolution
        let folder = null;
        let preSyncSnapshot = new Map();
        try {
            mkdirSync(userWorkspaceDir, { recursive: true });
        } catch (mkErr) {
            return endResponse({ type: 'error', errorMessage: `Cannot create workspace: ${mkErr.message}` });
        }
        // ADO#3559 — resolve task folder early so folderLocalDir is available for context assembly
        try {
            folder = await resolveTaskFolder(userId, taskFolderIdResolved ?? taskFolderId);
            folderLocalDir = `${WORKSPACE_DIR}/${userId}/${folder.id}`;
            mkdirSync(folderLocalDir, { recursive: true });
        } catch (folderErr) {
            console.warn(`[harness] folder resolution failed (non-fatal), using userWorkspaceDir: ${folderErr.message}`);
        }
        // Always load S3 context for task mode
        const prefix = S3_PREFIX || `workspaces/${userId}/`;
        const [soulMd, userMd, memoryMd] = await Promise.all([
            fetchS3File(`${prefix}assistants/SOUL.md`),
            fetchS3File(`${prefix}assistants/USER.md`),
            fetchS3File(`${prefix}memory/MEMORY.md`),
        ]);

        const contextParts = [];
        if (pluginAgentSoul) {
            // §6.1 — plugin agent soul replaces default identity for this turn
            contextParts.push(`## Plugin Agent Identity\n${pluginAgentSoul}`);
        } else if (soulMd) {
            contextParts.push(`## Assistant Identity\n${soulMd}`);
        }
        if (userEmail) contextParts.push(`## User Identity\nEmail: ${userEmail}`);
        contextParts.push(`## Session Identifiers\nuserId: ${userId}`);
        if (userMd) contextParts.push(`## About the User\n${userMd}`);
        // ADO#3564 — rewrite user message for retrieval optimization
        const retrievalQuery = await rewriteQueryForRetrieval(message, userId);
        // ADO#3397 — semantic memory injection (replaces MEMORY.md for vectorized users)
        if (pgPool) {
            const semanticChunks = await searchMemoryChunks(userId, retrievalQuery, 5, 0.7);
            if (semanticChunks && semanticChunks.length > 0) {
                const semanticContext = semanticChunks
                    .map(c => `[${c.source_file}] ${c.content}`)
                    .join('\n\n');
                contextParts.push(`## Relevant Memory\n${semanticContext}`);
                console.log(`[pgvector] injected ${semanticChunks.length} semantic chunks for userId=${userId} (CC path)`);
            } else {
                if (memoryMd) contextParts.push(`## Long-Term Memory\n${memoryMd}`);
            }
        } else {
            if (memoryMd) contextParts.push(`## Long-Term Memory\n${memoryMd}`);
        }
        // ADO#3398 7.7-A — structured system prompt: memory guidance + tool catalog + context awareness
        contextParts.push(`## Memory & Tool Guidance

Before any substantive response about prior work, decisions, people, or preferences: call \`read_memory\` with the relevant slug.
When the user states a preference, makes a decision, or shares a new fact worth keeping: call \`write_memory\`.

## MANDATORY: Workspace File Saving
CRITICAL: When the user asks you to save, create, write, or generate a file for their workspace — you MUST call the \`write_file\` tool. Do NOT describe what you would write. Do NOT provide the content as a text response. ALWAYS call \`write_file\` with the actual content. This is non-negotiable. Saying "I've saved..." without calling write_file is incorrect behavior.

Triggers that REQUIRE a write_file tool call (not an exhaustive list):
- "save this to my workspace"
- "create a file called..."
- "write a [document/report/summary] to workspace"
- "save [content] as [filename]"
- Any request to persist text as a named file

When referencing prior artifacts: call \`list_workspace_files(type=generated)\` first to see what exists.`);
        contextParts.push(buildToolManifestSection(enabledMcpSlugs));
        contextParts.push(`## Context Awareness

You have access to the user's workspace files and memory topics. When the user asks about something you may have worked on before, check workspace artifacts and memory before responding.`);
        contextParts.push(`## Tool Call Policy
- ALWAYS call tools live. Never use prior tool results from conversation history as a substitute for a fresh call.
- If a tool errored in a previous turn, treat it as if it was never called. Retry on this turn.
- Exception: if the user explicitly says "you already checked that" or similar, you may use context.
- Memory reads (read_memory), file reads, email/calendar lookups — always live, every time.
- Do not say you are going to call a tool and then not call it. If you say you will check something, check it.`);
        if (systemPrompt) contextParts.push(systemPrompt);

        // ADO#3398 7.7-B — per-turn workspace brief injection
        try {
            const headers = { 'Content-Type': 'application/json' };
            if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
            const briefResp = await fetch(`${FAIT_BASE_URL}/api/workspace/files?type=generated&limit=3&userId=${encodeURIComponent(userId)}`, { headers });
            if (briefResp.ok) {
                const briefData = await briefResp.json();
                const files = briefData?.files ?? briefData ?? [];
                if (Array.isArray(files) && files.length > 0) {
                    const fileList = files.map(f => `${f.filename || f.Filename} (${new Date(f.createdAt || f.CreatedAt).toLocaleDateString()})`).join(', ');
                    contextParts.push(`## Recent Workspace Artifacts\nRecent assistant-created files: ${fileList}`);
                    console.log(`[harness] workspace brief injected: ${files.length} files for userId=${userId}`);
                }
            }
        } catch (briefErr) {
            console.warn(`[harness] workspace brief injection failed (non-fatal): ${briefErr.message}`);
        }

        // ADO#3398 7.7-B — memory topic keyword pre-fetch stub
        {
            const KNOWN_MEMORY_SLUGS = ['preferences', 'projects', 'role', 'context', 'goals', 'history'];
            const messageLower = (message || '').toLowerCase();
            const matchedSlug = KNOWN_MEMORY_SLUGS.find(slug => messageLower.includes(slug));
            if (matchedSlug) {
                try {
                    const topicContent = await fetchS3File(`${S3_PREFIX || `workspaces/${userId}/`}memory/${matchedSlug}.md`);
                    if (topicContent) {
                        contextParts.push(`## Pre-fetched Memory: ${matchedSlug}\n${topicContent}`);
                        console.log(`[harness] pre-fetched memory topic '${matchedSlug}' for userId=${userId}`);
                    }
                } catch (prefetchErr) {
                    console.warn(`[harness] memory pre-fetch failed for slug '${matchedSlug}': ${prefetchErr.message}`);
                }
            }
        }

        // ADO#3575 — async helper: generate structured task brief via Haiku summarization
        async function generateTaskBrief(hist, bClient, mId, workspaceFiles) {
            if (!hist || hist.length <= 2) return null;
            try {
                const filesSection = workspaceFiles && workspaceFiles.length > 0
                    ? `\n\n## Existing Workspace Files\n${workspaceFiles.map(f => `- ${f}`).join('\n')}`
                    : '\n\n## Existing Workspace Files\nNone';
                const summarizationPrompt = `Produce a structured task brief for a coding agent about to execute a user's request. Based on the conversation history, include:
- Objective: one sentence — what the agent should produce
- Files involved: list each file with its role (e.g. "report.xlsx = source data")
- Constraints and preferences: bullet list of anything the user specified
- Expected output: file type, name if specified, destination folder

Be concise and specific. Output only the brief, no preamble.${filesSection}`;

                const response = await bClient.send(new ConverseCommand({
                    modelId: mId,
                    messages: (() => {
                        const raw = hist.slice(-20);
                        const coalesced = [];
                        for (const msg of raw) {
                            const last = coalesced[coalesced.length - 1];
                            const rawContent = msg.content ?? msg.Content ?? '';
                            const text = typeof rawContent === 'string' ? rawContent
                                : Array.isArray(rawContent) ? rawContent.map(c => c.text ?? c.Text ?? '').join(' ')
                                : '';
                            const role = (msg.role ?? msg.Role ?? 'user') === 'assistant' ? 'assistant' : 'user';
                            if (last && last.role === role) {
                                // Merge consecutive same-role messages
                                last.content[0].text = `${last.content[0].text} ${text}`.trim();
                            } else {
                                coalesced.push({ role, content: [{ text: text || '(empty)' }] });
                            }
                        }
                        coalesced.push({ role: 'user', content: [{ text: summarizationPrompt }] });
                        return coalesced;
                    })(),
                    inferenceConfig: { maxTokens: 500, temperature: 0 }
                }));

                const brief = response.output?.message?.content?.[0]?.text?.trim();
                if (brief && brief.length > 50) {
                    console.log(`[CC spawn] task brief generated (len=${brief.length})`);
                    return brief;
                }
                return null;
            } catch (err) {
                console.warn('[CC spawn] task brief generation failed, using fallback:', err.message);
                return null;
            }
        }

        // ADO#3089 / ADO#3575 — inject session context recap on cold-start CC turns with existing history
        // ADO#3575: first try Haiku-generated task brief; fall back to truncated recap on failure
        const hasHistory = Array.isArray(history) && history.length > 0;
        if (hasHistory) {
            const haiku3575ModelId = BEDROCK_MODEL_MAP['haiku'] || 'us.anthropic.claude-haiku-4-5-20251001-v1:0';
            // ADO#4035: build workspace file list for brief context
            let briefWorkspaceFiles = [];
            try {
                if (folderLocalDir) {
                    briefWorkspaceFiles = fs.readdirSync(folderLocalDir).filter(f => !f.startsWith('.'));
                }
            } catch (_wfErr) {
                // non-fatal
            }
            const generatedBrief = await generateTaskBrief(history, bedrockClient, haiku3575ModelId, briefWorkspaceFiles);
            if (generatedBrief) {
                contextParts.push(`## Task Brief (Generated)\n${generatedBrief}`);
                console.log(`[harness] /turn: injected Haiku-generated task brief (len=${generatedBrief.length}) into CC context`);
            } else {
                // Fallback: original truncated recap (ADO#3089)
                const MAX_RECAP_CHARS = 2000;
                const MAX_MESSAGES = 5;
                const recentMessages = history.slice(-MAX_MESSAGES);
                const recapLines = recentMessages.map(h => {
                    const role = h.role ?? h.Role ?? 'unknown';
                    const content = h.content ?? h.Content ?? '';
                    let text = '';
                    if (typeof content === 'string') {
                        text = content;
                    } else if (Array.isArray(content)) {
                        text = content.map(c => c.text ?? c.Text ?? '').join(' ');
                    }
                    const preview = text.trim().replace(/\n+/g, ' ').substring(0, 200);
                    const roleLabel = role === 'user' ? 'User' : 'Assistant';
                    return `- ${roleLabel}: ${preview}`;
                });
                let recap = `[Session Context — continuing conversation]\nRecent messages:\n${recapLines.join('\n')}`;
                if (recap.length > MAX_RECAP_CHARS) {
                    recap = recap.substring(0, MAX_RECAP_CHARS) + '\n[... recap truncated]';
                }
                contextParts.push(recap);
                console.log(`[harness] /turn: injected session recap fallback (${recap.length} chars, ${recentMessages.length} messages) into CC context`);
            }
        }

        // ADO#3392 — KB retrieval for CC spawn path
        // Must run before briefContent assembly so CC child receives KB context
        const ccKbParts = [];
        {
            const kbFlags = rawBody.KbFlags ?? rawBody.kbFlags ?? null;
            if (kbFlags && (kbFlags.CorpKbEnabled || kbFlags.corpKbEnabled ||
                            kbFlags.PersonalKbEnabled || kbFlags.personalKbEnabled ||
                            kbFlags.TeamKbEnabled || kbFlags.teamKbEnabled)) {

                // Double-retrieval guard: if systemPrompt already contains KB context injected by Blazor, skip
                const alreadyHasCorpKb = systemPrompt && systemPrompt.includes('## Knowledge Base Context');
                const alreadyHasPersonalKb = systemPrompt && systemPrompt.includes('## Personal/Team Knowledge Base Context');
                const alreadyHasTeamKb = systemPrompt && systemPrompt.includes('## Personal/Team Knowledge Base Context (Team ');

                const personalKbUserId = kbFlags.PersonalKbUserId ?? kbFlags.personalKbUserId ?? null;
                const teamIds = kbFlags.TeamIds ?? kbFlags.teamIds ?? null;
                const kbPromises = [];

                if ((kbFlags.CorpKbEnabled || kbFlags.corpKbEnabled) && !alreadyHasCorpKb && process.env.CORP_KB_ID) {
                    kbPromises.push(
                        retrieveFromKbFiltered(process.env.CORP_KB_ID, retrievalQuery, null, null, 5)
                            .then(results => {
                                if (results.length > 0) {
                                    const text = results.map((r, i) => `[${i+1}] ${(r.content?.text || '').substring(0, 2000)}`).join('\n\n');
                                    ccKbParts.push(`## Knowledge Base Context\nThe following information was retrieved from the organization's knowledge base:\n\n${text}`);
                                }
                            }).catch(err => console.error('[harness] CC KB corp retrieval error:', err.message))
                    );
                }
                if ((kbFlags.PersonalKbEnabled || kbFlags.personalKbEnabled) && !alreadyHasPersonalKb && personalKbUserId && process.env.PERSONAL_KB_ID) {
                    kbPromises.push(
                        retrieveFromKbFiltered(process.env.PERSONAL_KB_ID, retrievalQuery, 'ownerId', personalKbUserId, 5)
                            .then(results => {
                                if (results.length > 0) {
                                    const text = results.map((r, i) => `[${i+1}] ${(r.content?.text || '').substring(0, 2000)}`).join('\n\n');
                                    ccKbParts.push(`## Personal/Team Knowledge Base Context\nThe following information was retrieved from the user's knowledge base:\n\n${text}`);
                                }
                            }).catch(err => console.error('[harness] CC KB personal retrieval error:', err.message))
                    );
                } else if ((kbFlags.PersonalKbEnabled || kbFlags.personalKbEnabled) && !alreadyHasPersonalKb && !personalKbUserId) {
                    console.warn('[harness] CC KB: PersonalKbEnabled but no personalKbUserId — skipping for security');
                }
                if ((kbFlags.TeamKbEnabled || kbFlags.teamKbEnabled) && !alreadyHasTeamKb && teamIds && teamIds.length > 0 && process.env.TEAM_KB_ID) {
                    for (const teamId of teamIds) {
                        kbPromises.push(
                            retrieveFromKbFiltered(process.env.TEAM_KB_ID, retrievalQuery, 'teamId', teamId, 5)
                                .then(results => {
                                    if (results.length > 0) {
                                        const text = results.map((r, i) => `[${i+1}] ${(r.content?.text || '').substring(0, 2000)}`).join('\n\n');
                                        ccKbParts.push(`## Personal/Team Knowledge Base Context (Team ${teamId})\nThe following information was retrieved from the team's knowledge base:\n\n${text}`);
                                    }
                                }).catch(err => console.error(`[harness] CC KB team ${teamId} retrieval error:`, err.message))
                        );
                    }
                }
                await Promise.all(kbPromises);
            }
        }
        if (ccKbParts.length > 0) {
            contextParts.push(...ccKbParts);
            console.log(`[harness] CC spawn: injected ${ccKbParts.length} KB section(s) into contextParts for userId=${userId}`);
        }

        // Artifact generation instructions — injected on every CC task turn (ADO#3563)
        const workingPath = folderLocalDir;
        contextParts.push(`## Artifact Generation Rules
When the user asks for a file, generate a real file. Do not return text content and tell the user to paste it into another application.
- Spreadsheet / Excel (.xlsx): use openpyxl. Write the file to the working folder.
- PowerPoint / Presentation (.pptx): use python-pptx. Write the file to the working folder.
- Word document (.docx): use python-docx. Write the file to the working folder.
- PDF: use reportlab. Write the file to the working folder.
- Chart / graph image (.png): use matplotlib or plotly+kaleido. Write the file to the working folder.
- CSV: write as a plain text file with .csv extension. This is the only case where writing text is acceptable for a file request.

After creating a file, confirm its name and location in your response. Do not print the file contents to the user.

## Available Python Libraries
Pre-installed: openpyxl, python-pptx, python-docx, pandas, matplotlib, plotly, kaleido, reportlab, Pillow`);

        // ADO#3559 — folder-scoped pre-task S3 sync (folder already resolved above)
        try {
            if (folder) {
                const { execSync } = require('child_process');
                execSync(
                    `aws s3 sync s3://${S3_BUCKET}/${folder.s3_prefix} ${folderLocalDir}/ --quiet`,
                    { timeout: 30000, stdio: ['ignore', 'pipe', 'pipe'] }
                );
                console.log(`[harness] folder-scoped S3 sync complete: folder=${folder.name} folderId=${folder.id} userId=${userId}`);

                // Record pre-sync snapshot for dirty detection (after S3 sync, so we capture S3 state)
                preSyncSnapshot = buildLocalSnapshot(folderLocalDir);

                // Update last_task_folder_id
                try {
                    const connUpdate = await getDbConnection();
                    await connUpdate.execute('UPDATE users SET last_task_folder_id = ? WHERE id = ?', [folder.id, userId]);
                    connUpdate.end();
                } catch (updateErr) {
                    console.warn(`[harness] failed to update last_task_folder_id (non-fatal): ${updateErr.message}`);
                }
            } else {
                console.warn(`[harness] no folder resolved — skipping folder-scoped S3 pre-sync userId=${userId}`);
            }
        } catch (syncErr) {
            console.warn(`[harness] pre-run folder sync failed (non-fatal): ${syncErr.message}`);
            // Never block — continue with whatever is already local
        }

        // ADO#3561 — pre-task sync for read-only folders (must run before context assembly so brief can reference synced files)
        const resolvedReadOnlyFolders = [];  // { folder, localDir } for later use in brief and post-task exclusion
        if (readOnlyFolderIdsConfirmed.length > 0) {
            const { execSync } = require('child_process');
            for (const roFolderId of readOnlyFolderIdsConfirmed) {
                try {
                    const roFolder = await resolveTaskFolder(userId, roFolderId);
                    const roLocalDir = `${WORKSPACE_DIR}/${userId}/readonly/${roFolder.id}`;
                    mkdirSync(roLocalDir, { recursive: true });
                    execSync(
                        `aws s3 sync s3://${S3_BUCKET}/${roFolder.s3_prefix} ${roLocalDir}/ --delete --quiet`,
                        { timeout: 30000, stdio: ['ignore', 'pipe', 'pipe'] }
                    );
                    console.log(`[harness] ADO#3561 read-only sync complete: folder=${roFolder.name} folderId=${roFolder.id} localDir=${roLocalDir} userId=${userId}`);
                    resolvedReadOnlyFolders.push({ folder: roFolder, localDir: roLocalDir });
                } catch (roSyncErr) {
                    console.warn(`[harness] ADO#3561 read-only folder sync failed for folderId=${roFolderId} (non-fatal): ${roSyncErr.message}`);
                }
            }
        }

        // ADO#3559 — inject working folder context and workspace manifest
        if (folder) {
            contextParts.push(`## Working Folder\nYour working directory is: ${folderLocalDir}\nFolder name: ${folder.name}\nAll files you create must be written here.`);

            // ADO#3561 — inject read-only folder section
            if (resolvedReadOnlyFolders.length > 0) {
                const roLines = [];
                for (const { folder: roFolder, localDir: roDir } of resolvedReadOnlyFolders) {
                    let fileList = '';
                    try {
                        const entries = fs.readdirSync(roDir).filter(f => {
                            try { return fs.statSync(path.join(roDir, f)).isFile(); } catch { return false; }
                        }).slice(0, 10);
                        fileList = entries.length > 0 ? `\n  Files: ${entries.join(', ')}` : '';
                    } catch { /* non-fatal */ }
                    roLines.push(`- ${roFolder.name} available at ./readonly/${roFolder.id}/   (folder name: ${roFolder.name})${fileList}`);
                }
                contextParts.push(`## Read-Only Reference Folders\nThe following folders are available for reading only. Do NOT write files to these paths.\n\n${roLines.join('\n')}\n\nFiles in read-only folders: READ ONLY. Any writes to ./readonly/... will be ignored and not synced back.`);
            }

            // ADO#3561 — updated workspace manifest includes all folders (working + read-only)
            const manifest = await getWorkspaceManifest(userId);
            if (manifest) {
                const roFolderNames = resolvedReadOnlyFolders.map(r => r.folder.name);
                const manifestNote = roFolderNames.length > 0
                    ? `\nRead-only folders available this task: ${roFolderNames.join(', ')}`
                    : '';
                contextParts.push(`## Workspace Manifest\nYour workspace folders:\n${manifest}${manifestNote}`);
            }
        }

        // ADO#4035 cycle 2 — inject CLAUDE.md workspace rules into CC context
        let claudeMdContent = '';
        try {
            claudeMdContent = fs.readFileSync(path.join(__dirname, 'CLAUDE.md'), 'utf8');
        } catch (_) {
            // non-fatal — CLAUDE.md missing just means no workspace rules injected
        }
        if (claudeMdContent) {
            contextParts.push(`## Workspace Rules\n${claudeMdContent}`);
        }

        const fullContext = contextParts.join('\n\n---\n\n');
        const briefContent = fullContext
            ? `${fullContext}\n\n---\n\nUser: ${message}`
            : message;

        // ADO#3289 — log the exact command being spawned
        const ccArgs = [
            '--model', process.env.CC_MODEL || 'sonnet',
            '--print',
            '--output-format', 'stream-json',
            '--verbose',
            '--dangerously-skip-permissions'
        ];
        console.log(`[CC spawn] command=claude ${ccArgs.join(' ')} cwd=${folderLocalDir} userId=${userId} briefLen=${briefContent?.length ?? 0}`);
        const ccProcess = spawn('claude', ccArgs, {
            cwd: folderLocalDir,
            env: {
                ...process.env,
                CLAUDE_CODE_ENTRYPOINT: 'fargate-harness',
                CLAUDE_CODE_USE_BEDROCK: '1',
                CLAUDE_CODE_DISABLE_AUTO_MEMORY: '1',
                CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR: '1',
                HARNESS_KB_WRITE_ALLOWED: String(kbWriteAllowed),
                HARNESS_PLUGIN_AGENT_ID: pluginAgentId || '',
            },
            stdio: ['pipe', 'pipe', 'pipe']
        });
        ccProcess.stdin.write(briefContent);
        ccProcess.stdin.end();
        const TURN_TIMEOUT_MS = parseInt(process.env.CC_TIMEOUT_MS || '300000', 10);
        const timeout = setTimeout(() => {
            ccProcess.kill('SIGTERM');
            endResponse({ type: 'error', errorMessage: 'Turn timed out after 5 minutes' });
        }, TURN_TIMEOUT_MS);
        // NDJSON parser for --output-format stream-json (ADO#3244)
        let ccStdoutBuffer = '';
        let ccTextEmitted = false;
        const toolUseMap = new Map(); // track tool_use id → name for tool_result correlation
        let lastEmittedLabel = '';
        let consecutiveLabelCount = 0;
        ccProcess.stdout.on('data', (chunk) => {
            console.log(`[CC spawn] stdout chunk bytes=${chunk.length} userId=${userId}`);
            ccStdoutBuffer += chunk.toString();
            const lines = ccStdoutBuffer.split('\n');
            ccStdoutBuffer = lines.pop(); // keep incomplete last line
            for (const line of lines) {
                if (!line.trim()) continue;
                let parsed;
                try { parsed = JSON.parse(line); } catch {
                    // Non-JSON line — emit as raw text fallback
                    sendEvent({ type: 'text', content: scrubSecrets(line) });
                    continue;
                }
                const evtType = parsed.type;
                if (evtType === 'assistant' && parsed.message?.content) {
                    // ADO#4048 — filter CC narration: suppress text blocks from messages that also contain tool_use
                    const hasToolUse = parsed.message.content.some(b => b.type === 'tool_use');
                    for (const block of parsed.message.content) {
                        if (block.type === 'text' && block.text) {
                            if (hasToolUse) {
                                // Narration text co-located with tool calls — suppress from UI, log only
                                console.debug(`[CC spawn] narration suppressed (tool_use colocated, ${block.text.length} chars) userId=${userId}`);
                            } else {
                                ccTextEmitted = true;
                                sendEvent({ type: 'text', content: scrubSecrets(block.text) });
                            }
                        } else if (block.type === 'tool_use') {
                            toolUseMap.set(block.id, block.name || 'tool');
                            const inputSummary = block.input ? JSON.stringify(block.input).slice(0, 200) : '';
                            console.log(`[CC spawn] tool_use: ${block.name}(${inputSummary}) userId=${userId}`);
                            const label = resolveProgressLabel(block.name, block.input);
                            if (label === lastEmittedLabel) {
                                consecutiveLabelCount++;
                            } else {
                                lastEmittedLabel = label;
                                consecutiveLabelCount = 1;
                            }
                            if (consecutiveLabelCount <= 3) {
                                sendEvent({ type: 'task_progress', payload: JSON.stringify({ step: 'tool_use', toolName: block.name, status: 'calling', message: label }) });
                            }
                        }
                    }
                } else if (evtType === 'user' && Array.isArray(parsed.message?.content)) {
                    for (const block of parsed.message.content) {
                        if (block.type === 'tool_result') {
                            const toolName = toolUseMap.get(block.tool_use_id) || block.tool_use_id || 'tool';
                            sendEvent({ type: 'task_progress', payload: JSON.stringify({
                                step: 'tool_result', toolName, status: 'done', message: `${toolName} completed`
                            }) });
                        }
                    }
                } else if (evtType === 'result') {
                    if (!ccTextEmitted && parsed.result) {
                        sendEvent({ type: 'text', content: scrubSecrets(parsed.result) });
                        ccTextEmitted = true;
                    }
                    const resultText = parsed.result || '';
                    if (resultText) {
                        console.log(`[CC spawn] result text (first 500 chars): ${resultText.slice(0, 500)} userId=${userId}`);
                    }
                    // result.is_error is handled by the close event exit code
                }
                // system, init, and other types: skip silently
            }
        });
        ccProcess.stderr.on('data', (chunk) => {
            const stderrText = chunk.toString();
            console.log(`[CC spawn] stderr bytes=${chunk.length} userId=${userId} text=${stderrText.trim().slice(0, 500)}`);
            sendEvent({ type: 'log', content: scrubSecrets(stderrText) });
        });
        ccProcess.on('close', async (code) => {
            clearTimeout(timeout);
            toolUseMap.clear();
            // ADO#3289 — log exit code and silent-exit warning
            console.log(`[CC spawn] process exited code=${code} userId=${userId} ccTextEmitted=${ccTextEmitted}`);
            if (code === 0 && !ccTextEmitted) {
                console.warn(`[CC spawn] Process exited 0 but produced no output — possible silent failure userId=${userId}`);
            }
            if (code !== 0) {
                console.warn(`[CC spawn] Process exited with non-zero code=${code} userId=${userId}`);
            }
            // §G7 — clean up scheduled task context
            if (userId) scheduledTaskUsers.delete(userId);
            let artifact = null;
            try {
                artifact = await scanAndUploadArtifacts(userId, folderLocalDir, conversationId);
            } catch (err) {
                console.error('[harness] artifact upload failed:', err.message);
            }

            // ADO#3559 — dirty-only post-task sync (upload only new/changed files)
            // ADO#3562 — adds version tracking + files_updated SSE event
            try {
                if (folder) {
                    const postSyncSnapshot = buildLocalSnapshot(folderLocalDir);
                    const dirtyFiles = findDirtyFiles(preSyncSnapshot, postSyncSnapshot);
                    const uploadedFiles = [];
                    for (const relPath of dirtyFiles) {
                        const localPath = path.join(folderLocalDir, relPath);
                        // ADO#3561 — guard: never upload files from read-only folders
                        if (localPath.startsWith(`${WORKSPACE_DIR}/${userId}/readonly/`)) continue;
                        const s3Key = `${folder.s3_prefix}${relPath}`;
                        const fileSize = (() => { try { return fs.statSync(localPath).size; } catch { return null; } })();
                        await s3Client.send(new PutObjectCommand({
                            Bucket: S3_BUCKET,
                            Key: s3Key,
                            Body: fs.createReadStream(localPath),
                        }));
                        console.log(`[harness] post-sync uploaded: ${relPath} → s3://${S3_BUCKET}/${s3Key} userId=${userId}`);

                        // ADO#3562 — write provenance row
                        const connProv = await getDbConnection();
                        try {
                            const [existRows] = await connProv.execute(
                                'SELECT id, current_version FROM user_workspace_uploads WHERE user_id = ? AND s3_key = ? LIMIT 1',
                                [userId, s3Key]
                            );
                            if (existRows.length === 0) {
                                // New file
                                const fileId = crypto.randomUUID();
                                const filename = path.basename(relPath);
                                await connProv.execute(
                                    'INSERT INTO user_workspace_uploads (id, user_id, folder_id, filename, mime_type, s3_key, size_bytes, created_at, current_version, source, conversation_id) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)',
                                    [fileId, userId, folder.id, filename, 'application/octet-stream', s3Key, fileSize ?? 0, new Date(), 'cc', conversationId ?? null]
                                );
                                await connProv.execute(
                                    'INSERT INTO workspace_file_versions (id, file_id, version_number, s3_key, size_bytes, created_at, created_by, conversation_id, turn_index) VALUES (?, ?, 1, ?, ?, ?, ?, ?, ?)',
                                    [crypto.randomUUID(), fileId, s3Key, fileSize ?? null, new Date(), 'cc', conversationId ?? null, turnIndex ?? null]
                                );
                                uploadedFiles.push({ filename: path.basename(relPath), fileId, version: 1, action: 'created', s3Key });
                            } else {
                                // Updated file
                                const nextVersion = (existRows[0].current_version || 1) + 1;
                                await connProv.execute(
                                    'UPDATE user_workspace_uploads SET current_version = ?, conversation_id = ? WHERE id = ?',
                                    [nextVersion, conversationId ?? null, existRows[0].id]
                                );
                                await connProv.execute(
                                    'INSERT INTO workspace_file_versions (id, file_id, version_number, s3_key, size_bytes, created_at, created_by, conversation_id, turn_index) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)',
                                    [crypto.randomUUID(), existRows[0].id, nextVersion, s3Key, fileSize ?? null, new Date(), 'cc', conversationId ?? null, turnIndex ?? null]
                                );
                                uploadedFiles.push({ filename: path.basename(relPath), fileId: existRows[0].id, version: nextVersion, action: 'updated', s3Key });
                            }
                        } catch (provErr) {
                            console.warn(`[harness] post-sync provenance error (non-fatal): ${provErr.message}`);
                        } finally {
                            connProv.end();
                        }
                    }
                    console.log(`[harness] post-sync complete: ${dirtyFiles.length} file(s) uploaded userId=${userId}`);

                    // ADO#3562 — emit files_updated SSE event with presigned URLs
                    if (uploadedFiles.length > 0) {
                        try {
                            const presignedFiles = await Promise.all(uploadedFiles.map(async (f) => {
                                const url = await getSignedUrl(s3Client, new GetObjectCommand({
                                    Bucket: S3_BUCKET, Key: f.s3Key
                                }), { expiresIn: 1800 });
                                return { ...f, presignedUrl: url };
                            }));
                            sendEvent({
                                type: 'files_updated',
                                payload: JSON.stringify({
                                    folderId: folder.id,
                                    folderName: folder.name,
                                    files: presignedFiles.map(f => ({
                                        filename: f.filename,
                                        action: f.action,
                                        version: f.version,
                                        presignedUrl: f.presignedUrl
                                    }))
                                })
                            });
                            console.log(`[harness] files_updated event emitted: ${uploadedFiles.length} file(s) userId=${userId}`);
                        } catch (presignErr) {
                            console.warn(`[harness] files_updated presign error (non-fatal): ${presignErr.message}`);
                        }
                    }
                } else {
                    // fallback: no folder resolved, skip post-sync
                    console.warn(`[harness] post-sync skipped: no folder resolved for userId=${userId}`);
                }
            } catch (syncErr) {
                console.warn(`[harness] post-run S3 sync failed (non-fatal): ${syncErr.message}`);
            }

            if (artifact) {
                endResponse({
                    type: 'done',
                    exitCode: code,
                    artifactUrl: artifact.artifactUrl,
                    artifactFileName: artifact.fileName,
                    artifactType: artifact.artifactType,
                    artifactS3Key: artifact.s3Key,
                });
            } else {
                endResponse({ type: 'done', exitCode: code });
            }
        });
        ccProcess.on('error', (err) => { clearTimeout(timeout); endResponse({ type: 'error', errorMessage: scrubSecrets(err.message) }); });
    } else {
        // ── Bedrock ConverseStream path ───────────────────────────────────
        console.log(`[harness] /turn: taskMode=false — entering Bedrock ConverseStream path for userId=${userId}`);
        try {
            // Load user memory files from S3
            const prefix = S3_PREFIX || `workspaces/${userId}/`;
            console.log(`[harness] /turn: fetching S3 context files from prefix=${prefix}`);
            const [soulMd, userMd, memoryMd] = await Promise.all([
                fetchS3File(`${prefix}assistants/SOUL.md`),
                fetchS3File(`${prefix}assistants/USER.md`),
                fetchS3File(`${prefix}memory/MEMORY.md`),
            ]);
            console.log(`[harness] /turn: S3 fetch complete — soulMd=${soulMd ? soulMd.length + ' chars' : 'null'}, userMd=${userMd ? userMd.length + ' chars' : 'null'}, memoryMd=${memoryMd ? memoryMd.length + ' chars' : 'null'}`);

            const systemParts = [];
            if (pluginAgentSoul) {
                // §6.1 — plugin agent soul replaces default identity for this turn
                systemParts.push(`## Plugin Agent Identity\n${pluginAgentSoul}`);
            } else if (soulMd) {
                systemParts.push(`## Assistant Identity\n${soulMd}`);
            }
            if (userEmail) systemParts.push(`## User Identity\nEmail: ${userEmail}`);
            if (userMd) systemParts.push(`## About the User\n${userMd}`);
            // ADO#3564 — rewrite user message for retrieval optimization
            const retrievalQuery = await rewriteQueryForRetrieval(message, userId);
            // ADO#3397 — semantic memory injection (replaces MEMORY.md for vectorized users)
            if (pgPool) {
                const semanticChunks = await searchMemoryChunks(userId, retrievalQuery, 5, 0.7);
                if (semanticChunks && semanticChunks.length > 0) {
                    const semanticContext = semanticChunks
                        .map(c => `[${c.source_file}] ${c.content}`)
                        .join('\n\n');
                    systemParts.push(`## Relevant Memory\n${semanticContext}`);
                    console.log(`[pgvector] injected ${semanticChunks.length} semantic chunks for userId=${userId} (Bedrock path)`);
                } else {
                    if (memoryMd) systemParts.push(`## Long-Term Memory\n${memoryMd}`);
                }
            } else {
                if (memoryMd) systemParts.push(`## Long-Term Memory\n${memoryMd}`);
            }
            // ADO#3398 7.7-A — structured system prompt: memory guidance + tool catalog + context awareness
            systemParts.push(`## Memory & Tool Guidance

Before any substantive response about prior work, decisions, people, or preferences: call \`read_memory\` with the relevant slug.
When the user states a preference, makes a decision, or shares a new fact worth keeping: call \`write_memory\`.

## MANDATORY: Workspace File Saving
CRITICAL: When the user asks you to save, create, write, or generate a file for their workspace — you MUST call the \`write_file\` tool. Do NOT describe what you would write. Do NOT provide the content as a text response. ALWAYS call \`write_file\` with the actual content. This is non-negotiable. Saying "I've saved..." without calling write_file is incorrect behavior.

Triggers that REQUIRE a write_file tool call (not an exhaustive list):
- "save this to my workspace"
- "create a file called..."
- "write a [document/report/summary] to workspace"
- "save [content] as [filename]"
- Any request to persist text as a named file

When referencing prior artifacts: call \`list_workspace_files(type=generated)\` first to see what exists.`);
            systemParts.push(buildToolManifestSection(enabledMcpSlugs));
            systemParts.push(`## Context Awareness

You have access to the user's workspace files and memory topics. When the user asks about something you may have worked on before, check workspace artifacts and memory before responding.`);
            systemParts.push(`## Tool Call Policy
- ALWAYS call tools live. Never use prior tool results from conversation history as a substitute for a fresh call.
- If a tool errored in a previous turn, treat it as if it was never called. Retry on this turn.
- Exception: if the user explicitly says "you already checked that" or similar, you may use context.
- Memory reads (read_memory), file reads, email/calendar lookups — always live, every time.
- Do not say you are going to call a tool and then not call it. If you say you will check something, check it.`);
            systemParts.push(`## Task Mode Self-Escalation

When the user wants you to execute a coding task and you have all information needed to proceed (clear objective, specific files or functionality to create/modify, no ambiguous requirements), you MUST emit [TASK_READY] at the end of your response on its own line.

Do not ask for confirmation before emitting [TASK_READY]. Do not wait for the user to say "go ahead" or "start" or "do it". Do not ask if they are ready. When you have what you need, escalate immediately.

The harness will detect [TASK_READY], strip it from the displayed response, and automatically spawn Claude Code CLI on your behalf to execute the task.

Only ask clarifying questions when requirements are genuinely incomplete — you are missing specific information needed to write correct code (e.g., unknown file path, ambiguous behavior, missing data structure). Do not ask clarifying questions out of caution when the intent is already clear.

Do NOT emit [TASK_READY] if:
- Task mode is already active (you are already in a CC spawn context)
- The user is asking a question, not requesting a task
- You genuinely need more information to proceed (state what you need and wait)`);
            if (systemPrompt) systemParts.push(systemPrompt);

            // ADO#3398 7.7-B — per-turn workspace brief injection
            try {
                const briefHeaders = { 'Content-Type': 'application/json' };
                if (INTERNAL_API_TOKEN) briefHeaders['X-Internal-Token'] = INTERNAL_API_TOKEN;
                const briefResp = await fetch(`${FAIT_BASE_URL}/api/workspace/files?type=generated&limit=3&userId=${encodeURIComponent(userId)}`, { headers: briefHeaders });
                if (briefResp.ok) {
                    const briefData = await briefResp.json();
                    const files = briefData?.files ?? briefData ?? [];
                    if (Array.isArray(files) && files.length > 0) {
                        const fileList = files.map(f => `${f.filename || f.Filename} (${new Date(f.createdAt || f.CreatedAt).toLocaleDateString()})`).join(', ');
                        systemParts.push(`## Recent Workspace Artifacts\nRecent assistant-created files: ${fileList}`);
                        console.log(`[harness] workspace brief injected: ${files.length} files for userId=${userId}`);
                    }
                }
            } catch (briefErr) {
                console.warn(`[harness] workspace brief injection failed (non-fatal): ${briefErr.message}`);
            }

            // ADO#3398 7.7-B — memory topic keyword pre-fetch stub
            {
                const KNOWN_MEMORY_SLUGS = ['preferences', 'projects', 'role', 'context', 'goals', 'history'];
                const messageLower = (message || '').toLowerCase();
                const matchedSlug = KNOWN_MEMORY_SLUGS.find(slug => messageLower.includes(slug));
                if (matchedSlug) {
                    try {
                        const prefix = S3_PREFIX || `workspaces/${userId}/`;
                        const topicContent = await fetchS3File(`${prefix}memory/${matchedSlug}.md`);
                        if (topicContent) {
                            systemParts.push(`## Pre-fetched Memory: ${matchedSlug}\n${topicContent}`);
                            console.log(`[harness] pre-fetched memory topic '${matchedSlug}' for userId=${userId}`);
                        }
                    } catch (prefetchErr) {
                        console.warn(`[harness] memory pre-fetch failed for slug '${matchedSlug}': ${prefetchErr.message}`);
                    }
                }
            }

            if (systemParts.length === 0) {
                systemParts.push('You are a helpful AI assistant.');
            }
            let fullSystemPrompt = systemParts.join('\n\n---\n\n');
            console.log(`[harness] /turn: system prompt built, totalLen=${fullSystemPrompt.length}`);

            // ADO#3241 — Harness-side KB retrieval
            const kbFlags = rawBody.KbFlags ?? rawBody.kbFlags ?? null;
            console.log(`[harness] /turn: kbFlags extracted — value=${JSON.stringify(kbFlags)} userId=${rawBody.UserId ?? rawBody.userId}`);
            if (kbFlags) {
                const kbEnabled = kbFlags.CorpKbEnabled || kbFlags.corpKbEnabled ||
                                  kbFlags.PersonalKbEnabled || kbFlags.personalKbEnabled ||
                                  kbFlags.TeamKbEnabled || kbFlags.teamKbEnabled;

                if (kbEnabled) {
                    console.log(`[harness] /turn: KB retrieval — flags=${JSON.stringify(kbFlags)}`);
                    const kbSources = [];

                    // ADO#3278 — KB retrieval with data isolation filters
                    const personalKbUserId = kbFlags.PersonalKbUserId ?? kbFlags.personalKbUserId ?? null;
                    const teamIds = kbFlags.TeamIds ?? kbFlags.teamIds ?? null;

                    async function doKbRetrieval(kbId, kbName, query, filterKey, filterValue) {
                        if (!kbId) {
                            console.warn(`[harness] KB retrieval: no KB ID for ${kbName}`);
                            return;
                        }
                        try {
                            const results = await retrieveFromKbFiltered(kbId, query, filterKey, filterValue, 5);
                            if (results.length > 0) {
                                const chunks = results.map(r => ({
                                    title: (r.location?.s3Location?.uri || r.location?.confluenceLocation?.url || '').split('/').pop() || 'Document',
                                    excerpt: (r.content?.text || '').substring(0, 200)
                                }));
                                kbSources.push({
                                    kbId,
                                    kbName,
                                    sourceCount: results.length,
                                    chunks
                                });
                                const contextText = results.map((r, i) => `[${i+1}] ${(r.content?.text || '').substring(0, 2000)}`).join('\n\n');
                                const section = kbName === 'Corp KB'
                                    ? `## Knowledge Base Context\nThe following information was retrieved from the organization's knowledge base:\n\n${contextText}`
                                    : `## Personal/Team Knowledge Base Context\nThe following information was retrieved from the user's knowledge base:\n\n${contextText}`;
                                systemParts.push(section);
                            }
                        } catch (err) {
                            console.error(`[harness] KB retrieval error for ${kbName}:`, err.message);
                        }
                    }

                    const kbPromises = [];
                    if (kbFlags.CorpKbEnabled || kbFlags.corpKbEnabled) {
                        // Corp KB: no per-user filter — entire KB is team-scoped structurally
                        kbPromises.push(doKbRetrieval(process.env.CORP_KB_ID, 'Corp KB', retrievalQuery, null, null));
                    }
                    if (kbFlags.PersonalKbEnabled || kbFlags.personalKbEnabled) {
                        // Personal KB: filter by ownerId = user's GUID
                        if (!personalKbUserId) {
                            console.warn(`[harness] /turn: Personal KB requested but no PersonalKbUserId in kbFlags — skipping for security`);
                        } else {
                            kbPromises.push(doKbRetrieval(process.env.PERSONAL_KB_ID, 'Personal KB', retrievalQuery, 'ownerId', personalKbUserId));
                        }
                    }
                    if (kbFlags.TeamKbEnabled || kbFlags.teamKbEnabled) {
                        // Team KB: one retrieval per team ID, each filtered by teamId
                        const effectiveTeamIds = teamIds && teamIds.length > 0 ? teamIds : null;
                        if (!effectiveTeamIds) {
                            console.warn(`[harness] /turn: Team KB requested but no TeamIds in kbFlags — skipping for security`);
                        } else {
                            for (const teamId of effectiveTeamIds) {
                                kbPromises.push(doKbRetrieval(process.env.TEAM_KB_ID, `Team KB (${teamId})`, retrievalQuery, 'teamId', teamId));
                            }
                        }
                    }
                    await Promise.all(kbPromises);

                    // Emit kb_sources event (only when results exist — ADO#3278)
                    if (kbSources.length > 0) {
                        res.write(`event: kb_sources\ndata: ${JSON.stringify({ sources: kbSources })}\n\n`);
                        console.log(`[harness] /turn: emitted kb_sources — ${kbSources.length} KB(s) with results`);
                    } else {
                        console.log(`[harness] /turn: KB searched — no results found, skipping kb_sources event`);
                    }

                    // Rebuild system prompt after KB context injection
                    fullSystemPrompt = systemParts.join('\n\n---\n\n');
                    console.log(`[harness] /turn: system prompt rebuilt after KB retrieval, totalLen=${fullSystemPrompt.length}`);
                }
            }

            // Build message history
            const messages = [];
            if (Array.isArray(history)) {
                for (const h of history) {
                    if (h.role && h.content) {
                        messages.push({
                            role: h.role === 'assistant' ? 'assistant' : 'user',
                            content: [{ text: h.content }]
                        });
                    }
                }
            }
            messages.push({ role: 'user', content: [{ text: message }] });
            console.log(`[harness] /turn: message array built, count=${messages.length} (including current user message)`);

            // ADO#3218 — Dynamic toolConfig: built-ins always present + MCP tools for enabled slugs
            const BUILTIN_TOOL_SPECS = [
                    {
                        toolSpec: {
                            name: 'search_knowledge_base',
                            description: 'Search the user knowledge base for relevant context, facts, and information. Use this when the user asks questions that may be answered by their stored knowledge.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        query: {
                                            type: 'string',
                                            description: 'The search query'
                                        },
                                        kb_type: {
                                            type: 'string',
                                            enum: ['corp', 'personal', 'team'],
                                            description: 'Knowledge base type to search. Default: personal'
                                        }
                                    },
                                    required: ['query']
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'list_workspace_files',
                            description: 'List files in the user workspace. Use type=files for uploaded files, type=generated for AI-created artifacts, type=all for everything (default).',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        type: {
                                            type: 'string',
                                            enum: ['files', 'generated', 'all'],
                                            description: 'Which files to list: "files" = user uploads, "generated" = assistant-created artifacts, "all" = both (default)'
                                        },
                                        folder: {
                                            type: 'string',
                                            description: 'Optional subfolder path filter (for user uploads only)'
                                        }
                                    }
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'read_workspace_file',
                            description: 'Read the content of a workspace file by ID or path. Returns text content for text/markdown/html/json files. Returns metadata only for binary files.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        fileId: {
                                            type: 'string',
                                            description: 'The UUID of the file (from list_workspace_files results)'
                                        },
                                        path: {
                                            type: 'string',
                                            description: 'Filename or path of the file (alternative to fileId)'
                                        }
                                    }
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'read_memory',
                            description: 'Read a memory topic by slug. Returns the stored content for that topic.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        slug: { type: 'string', description: 'The memory topic slug to read' }
                                    },
                                    required: ['slug']
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'write_memory',
                            description: 'Write or update a memory topic. Persists content under the given slug.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        slug: { type: 'string', description: 'Topic slug (identifier)' },
                                        title: { type: 'string', description: 'Human-readable topic title (optional — defaults to slug)' },
                                        content: { type: 'string', description: 'Full markdown content to persist' }
                                    },
                                    required: ['slug', 'content']
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'create_document',
                            description: 'Use this tool to create a real Word document (.docx) artifact that will be saved and made available for download in the chat. When the user asks for a Word doc, report, proposal, or any other document file, ALWAYS use this tool — do not produce markdown as a substitute. This is the ONLY way to produce a downloadable file artifact. The document will appear as a clickable artifact card in the chat after generation.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        type: { type: 'string', description: 'Document format — must be "word" for .docx output' },
                                        title: { type: 'string', description: 'Document title, used as the filename base (e.g. "Q1 Report" → Q1_Report.docx)' },
                                        sections: {
                                            type: 'array',
                                            items: {
                                                type: 'object',
                                                properties: {
                                                    heading: { type: 'string' },
                                                    content: { type: 'string' }
                                                },
                                                required: ['heading', 'content']
                                            },
                                            description: 'Array of document sections. Each section has a heading (string) and content (string body text for that section).'
                                        }
                                    },
                                    required: ['type', 'title', 'sections']
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'list_files',
                            description: 'List folders and files in the user\'s workspace at a given folder path.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        folder_path: {
                                            type: 'string',
                                            description: 'Folder path (e.g. "reports/q1"). Empty or omit for root.'
                                        }
                                    }
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'read_file',
                            description: 'Read the text content of a file from the user\'s workspace.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        file_path: {
                                            type: 'string',
                                            description: 'Full file path (e.g. "reports/q1/summary.txt")'
                                        }
                                    },
                                    required: ['file_path']
                                }
                            }
                        }
                    },
                    {
                        toolSpec: {
                            name: 'write_file',
                            description: 'Save text content as a file in the user\'s workspace. Supports folder paths (e.g. "reports/q1/summary.md") — missing folders are created automatically. If a file already exists at the path, a new version is created automatically. Files appear immediately in the workspace FILES tab.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        path: {
                                            type: 'string',
                                            description: 'File path relative to user workspace root (e.g. "summary.md", "reports/q1.txt"). No leading slashes or .. traversal.'
                                        },
                                        content: {
                                            type: 'string',
                                            description: 'Text content to write to the file (max 1MB)'
                                        }
                                    },
                                    required: ['path', 'content']
                                }
                            }
                        }
                    }
            ];

            const allTools = [...BUILTIN_TOOL_SPECS];
            for (const slug of enabledMcpSlugs) {
                if (MCP_TOOL_SPECS[slug]) {
                    allTools.push(...MCP_TOOL_SPECS[slug]);
                    console.log(`[harness] /turn: toolConfig — added ${MCP_TOOL_SPECS[slug].length} tools for slug=${slug}`);
                } else {
                    // ADO#3249 — log unknown slugs so mismatches are caught early
                    console.warn(`[harness] /turn: toolConfig — no MCP_TOOL_SPECS entry for slug=${slug} (known: ${Object.keys(MCP_TOOL_SPECS).join(',')})`);
                }
            }
            const toolConfig = { tools: allTools, toolChoice: { auto: {} } };
            console.log(`[harness] /turn: toolConfig built — totalTools=${allTools.length}, toolNames=[${allTools.map(t => t.toolSpec?.name).join(',')}]`);

            console.log(`[harness] /turn: calling bedrockClient.send for userId=${userId}, modelId=${modelId}`);
            let tokenCount = 0;
            let inputTokens = 0;
            let outputTokens = 0;
            const MAX_TOOL_ITERATIONS = 10;
            let toolIterations = 0;
            let continueLoop = true;

            // ADO#3309 — Fetch KB access once per turn (before tool call loop)
            let kbAccessForTurn = null;
            if (userId) {
                kbAccessForTurn = await fetchKbAccess(userId);
                if (!kbAccessForTurn) {
                    console.warn(`[harness] /turn: fetchKbAccess returned null for userId=${userId} — personal/team KB will be denied`);
                }
            }

            while (continueLoop && toolIterations < MAX_TOOL_ITERATIONS) {
                toolIterations++;
                // ADO#3531 — prune stale tool result content beyond last 10 turns
                const prunedMessages = pruneToolResults(messages);
                let assistantTextAccumulator = '';
                let assistantContent = [];
                let toolUseAccumulator = null;
                let messageStopSeen = false;
                let stopReason = 'end_turn';
                // pendingToolResults replaces the scalar — handles multiple toolUse blocks per turn
                const pendingToolResults = [];

                const cmd = new ConverseStreamCommand({
                    modelId: resolvedModelId,
                    messages: prunedMessages,
                    system: [{ text: fullSystemPrompt }],
                    inferenceConfig: { maxTokens: 4096, temperature: 0.7 },
                    toolConfig
                });

                console.log(`[harness] /turn: agentic loop iteration ${toolIterations}, messages.length=${messages.length}`);
                const response = await bedrockClient.send(cmd);
                console.log(`[harness] /turn: Bedrock stream opened, beginning event iteration`);

                for await (const event of response.stream) {
                    if (event.contentBlockStart?.start?.toolUse) {
                        toolUseAccumulator = {
                            toolUseId: event.contentBlockStart.start.toolUse.toolUseId,
                            name: event.contentBlockStart.start.toolUse.name,
                            inputJson: ''
                        };
                        console.log(`[harness] /turn: toolUse start: name=${toolUseAccumulator.name}, id=${toolUseAccumulator.toolUseId}`);
                    } else if (event.contentBlockDelta?.delta?.toolUse) {
                        if (toolUseAccumulator) {
                            toolUseAccumulator.inputJson += event.contentBlockDelta.delta.toolUse.input || '';
                        }
                    } else if (event.contentBlockStop && toolUseAccumulator) {
                        // Tool call complete — execute it
                        console.log(`[harness] /turn: toolUse complete: name=${toolUseAccumulator.name}, input=${toolUseAccumulator.inputJson}`);
                        const toolInput = JSON.parse(toolUseAccumulator.inputJson || '{}');
                        let toolResultText = '';
                        let isError = false;

                        if (toolUseAccumulator.name === 'list_workspace_files') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const wsRes = await fetch(`http://localhost:${PORT}/tools/list_workspace_files`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, folder: toolInput.folder || '', type: toolInput.type || 'all' })
                                });
                                const wsData = await wsRes.json();
                                toolResultText = `\n\n[Workspace Files]\n${JSON.stringify(wsData, null, 2)}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (wsErr) {
                                toolResultText = `\n\n[Workspace Files Error]\n${wsErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${wsErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'read_workspace_file') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const rwfRes = await fetch(`http://localhost:${PORT}/tools/read_workspace_file`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ...toolInput })
                                });
                                toolResult = await rwfRes.json();
                                toolResultText = toolResult.content ?? (toolResult.note || JSON.stringify(toolResult));
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (rwfErr) {
                                toolResultText = `\n\n[Read Workspace File Error]\n${rwfErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${rwfErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'read_memory') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const rmRes = await fetch(`http://localhost:${PORT}/tools/read_memory`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, slug: toolInput.slug })
                                });
                                const rmData = await rmRes.json();
                                toolResultText = `\n\n[Memory Read]\n${JSON.stringify(rmData, null, 2)}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (rmErr) {
                                toolResultText = `\n\n[Memory Read Error]\n${rmErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${rmErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'search_memory') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const smRes = await fetch(`http://localhost:${PORT}/tools/search_memory`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, query: toolInput.query })
                                });
                                const smData = await smRes.json();
                                toolResultText = `\n\n[Memory Search]\n${JSON.stringify(smData, null, 2)}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (smErr) {
                                toolResultText = `\n\n[Memory Search Error]\n${smErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${smErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'write_memory') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const wmRes = await fetch(`http://localhost:${PORT}/tools/write_memory`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, slug: toolInput.slug, title: toolInput.title, content: toolInput.content })
                                });
                                const wmData = await wmRes.json();
                                toolResultText = `\n\n[Memory Write]\n${JSON.stringify(wmData, null, 2)}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (wmErr) {
                                toolResultText = `\n\n[Memory Write Error]\n${wmErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${wmErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'create_document') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const cdRes = await fetch(`http://localhost:${PORT}/tools/create_document`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({
                                        userId,
                                        conversationId,
                                        type: toolInput.type,
                                        title: toolInput.title,
                                        sections: toolInput.sections
                                    })
                                });
                                const cdData = await cdRes.json();
                                if (cdData.error) {
                                    emitToolCall(res, 'builtin', 'create_document', 'error', `Document creation failed: ${cdData.error}`);
                                    toolResultText = `\n\n[Document Error]\n${cdData.error}\n\n`;
                                } else {
                                    // Emit artifact SSE event BEFORE the tool result text
                                    sendEvent({
                                        type: 'artifact',
                                        payload: JSON.stringify({
                                            filename: cdData.filename,
                                            s3Key: cdData.s3Key,
                                            mimeType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
                                            sizeBytes: cdData.sizeBytes
                                        })
                                    });
                                    emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                                    toolResultText = `\n\nDocument created: ${cdData.filename}\n\n`;
                                }
                            } catch (cdErr) {
                                toolResultText = `\n\n[Document Error]\n${cdErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${cdErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'list_files') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const lfRes = await fetch(`http://localhost:${PORT}/tools/list_files`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ...toolInput })
                                });
                                const lfData = await lfRes.json();
                                toolResultText = JSON.stringify(lfData.items || []);
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (lfErr) {
                                toolResultText = `Error listing files: ${lfErr.message}`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${lfErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'read_file') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const rfRes = await fetch(`http://localhost:${PORT}/tools/read_file`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ...toolInput })
                                });
                                const rfData = await rfRes.json();
                                toolResultText = rfData.content || rfData.error || 'No content returned.';
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (rfErr) {
                                toolResultText = `Error reading file: ${rfErr.message}`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${rfErr.message.substring(0,100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'write_file') {
                            emitToolCall(res, 'builtin', toolUseAccumulator.name, 'calling', getBuiltinSummary(toolUseAccumulator.name, toolInput));
                            try {
                                const wfRes = await fetch(`http://localhost:${PORT}/tools/write_file`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({
                                        userId,
                                        conversationId,
                                        path: toolInput.path,
                                        content: toolInput.content,
                                    })
                                });
                                const wfData = await wfRes.json();
                                if (wfData.error) {
                                    toolResultText = `\n\n[Write File Error]\n${wfData.error}\n\n`;
                                    emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${wfData.error.substring(0, 100)}`);
                                } else {
                                    emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `File saved: ${wfData.filename}`);
                                    toolResultText = `\n\nFile saved to workspace: ${wfData.filename} (${wfData.sizeBytes} bytes). It will appear in the FILES tab.\n\n`;
                                }
                            } catch (wfErr) {
                                toolResultText = `\n\n[Write File Error]\n${wfErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${wfErr.message.substring(0, 100)}`);
                            }
                        } else if (toolUseAccumulator.name.startsWith('graph_')) {
                            // ADO#3241 — tool_call SSE events
                            const graphSummaries = {
                                graph_list_emails: 'Reading your inbox...',
                                graph_get_email: 'Reading email...',
                                graph_send_email: 'Sending email...',
                                graph_list_calendar_events: 'Checking your calendar...'
                            };
                            emitToolCall(res, 'graph', toolUseAccumulator.name, 'calling', graphSummaries[toolUseAccumulator.name] || `Calling ${toolUseAccumulator.name}...`);
                            try {
                                const mcpRes = await fetch(`http://localhost:${PORT}/tools/${toolUseAccumulator.name}`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ms365Token: userTokens.ms365, ...toolInput })
                                });
                                const mcpData = await mcpRes.json();
                                toolResultText = JSON.stringify(mcpData, null, 2);
                                emitToolCall(res, 'graph', toolUseAccumulator.name, 'done', 'Done.');
                            } catch (mcpErr) {
                                toolResultText = `MCP tool error (${toolUseAccumulator.name}): ${mcpErr.message}`;
                                isError = true;
                                emitToolCall(res, 'graph', toolUseAccumulator.name, 'error', `Error: ${mcpErr.message.substring(0, 100)}`);
                            }
                        } else if (toolUseAccumulator.name.startsWith('ado_')) {
                            // ADO#3241 — tool_call SSE events
                            const adoSummaries = {
                                ado_list_work_items: 'Querying ADO work items...',
                                ado_get_work_item: `Looking up work item ${toolInput.id ?? ''}...`,
                                ado_create_work_item: `Creating work item: ${toolInput.title ?? ''}...`,
                                ado_update_work_item: `Updating work item ${toolInput.id ?? ''}...`,
                                ado_wiql_query: 'Running ADO query...'
                            };
                            emitToolCall(res, 'ado', toolUseAccumulator.name, 'calling', adoSummaries[toolUseAccumulator.name] || `Calling ${toolUseAccumulator.name}...`);
                            try {
                                const mcpRes = await fetch(`http://localhost:${PORT}/tools/${toolUseAccumulator.name}`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, adoToken: userTokens.ado, ...toolInput })
                                });
                                const mcpData = await mcpRes.json();
                                toolResultText = JSON.stringify(mcpData, null, 2);
                                emitToolCall(res, 'ado', toolUseAccumulator.name, 'done', 'Done.');
                            } catch (mcpErr) {
                                toolResultText = `MCP tool error (${toolUseAccumulator.name}): ${mcpErr.message}`;
                                isError = true;
                                emitToolCall(res, 'ado', toolUseAccumulator.name, 'error', `Error: ${mcpErr.message.substring(0, 100)}`);
                            }
                        } else if (toolUseAccumulator.name === 'web_search') {
                            emitToolCall(res, 'brave', 'web_search', 'calling', `Searching the web for: ${toolInput.query ?? ''}`);
                            try {
                                const mcpRes = await fetch(`http://localhost:${PORT}/tools/web_search`, {
                                    method: 'POST',
                                    headers: { 'Content-Type': 'application/json' },
                                    body: JSON.stringify({ userId, ...toolInput })
                                });
                                const mcpData = await mcpRes.json();
                                toolResultText = JSON.stringify(mcpData, null, 2);
                                emitToolCall(res, 'brave', 'web_search', 'done', 'Web search complete.');
                            } catch (mcpErr) {
                                toolResultText = `Web search error: ${mcpErr.message}`;
                                isError = true;
                                emitToolCall(res, 'brave', 'web_search', 'error', `Error: ${mcpErr.message.substring(0, 100)}`);
                            }
                        } else {
                            // default: search_knowledge_base
                            emitToolCall(res, 'builtin', 'search_knowledge_base', 'calling', getBuiltinSummary('search_knowledge_base', toolInput));
                            try {
                                // ADO#3309 — kbAccessForTurn fetched once before the loop, reused across iterations
                                const kbSearchResult = await executeKbSearch(toolInput.query, toolInput.kb_type || 'personal', userId, kbAccessForTurn, kbFlags);
                                toolResultText = `\n\n[KB Search Results]\n${kbSearchResult.text}\n\n`;
                                // ADO#3309 — Emit kb_sources SSE event (matching Path A behavior)
                                if (kbSearchResult.sources && kbSearchResult.sources.length > 0) {
                                    const kbSourcesEvent = {
                                        sources: [{
                                            kbId: toolInput.kb_type || 'personal',
                                            kbName: (toolInput.kb_type || 'personal') === 'corp' ? 'Corp KB' : (toolInput.kb_type === 'team' ? 'Team KB' : 'Personal KB'),
                                            sourceCount: kbSearchResult.sources.length,
                                            chunks: kbSearchResult.sources
                                        }]
                                    };
                                    res.write(`event: kb_sources\ndata: ${JSON.stringify(kbSourcesEvent)}\n\n`);
                                }
                                emitToolCall(res, 'builtin', 'search_knowledge_base', 'done', 'Knowledge base search complete');
                            } catch (kbErr) {
                                toolResultText = `\n\n[KB Search Error]\n${kbErr.message}\n\n`;
                                isError = true;
                                emitToolCall(res, 'builtin', 'search_knowledge_base', 'error', `Error: ${kbErr.message.substring(0,100)}`);
                            }
                        }

                        // ADO#3215: accumulate assistant content (text so far + toolUse block)
                        if (assistantTextAccumulator) {
                            assistantContent.push({ text: assistantTextAccumulator });
                            assistantTextAccumulator = '';
                        }
                        assistantContent.push({
                            toolUse: {
                                toolUseId: toolUseAccumulator.toolUseId,
                                name: toolUseAccumulator.name,
                                input: toolInput
                            }
                        });

                        // Store pending tool result to append to messages after messageStop
                        pendingToolResults.push({
                            toolUseId: toolUseAccumulator.toolUseId,
                            toolResultText,
                            isError
                        });
                        toolUseAccumulator = null;
                    } else if (event.contentBlockDelta?.delta?.text) {
                        tokenCount++;
                        assistantTextAccumulator += event.contentBlockDelta.delta.text;
                        sendEvent({ type: 'text', content: event.contentBlockDelta.delta.text });
                    } else if (event.metadata?.usage) {
                        // ADO#3151: metadata arrives after messageStop — must NOT break before capturing this
                        inputTokens += event.metadata.usage.inputTokens || 0;
                        outputTokens += event.metadata.usage.outputTokens || 0;
                        console.log(`[harness] /turn: metadata captured — inputTokens=${inputTokens}, outputTokens=${outputTokens}`);
                        if (messageStopSeen) break;
                    } else if (event.messageStop) {
                        stopReason = event.messageStop.stopReason;
                        console.log(`[harness] /turn: messageStop received after ${tokenCount} text events, stopReason=${stopReason}`);
                        messageStopSeen = true;
                        // Do NOT break here — metadata event with usage arrives after messageStop
                    } else {
                        console.log(`[harness] /turn: stream event (non-text): ${JSON.stringify(Object.keys(event))}`);
                    }
                }

                // Flush any remaining text accumulator
                if (assistantTextAccumulator) {
                    assistantContent.push({ text: assistantTextAccumulator });
                    assistantTextAccumulator = '';
                }

                // ADO#3923: TASK_RESUME detection — if model appends [TASK_RESUME], strip it and emit task_resume SSE
                const fullAssistantText = assistantContent
                    .filter(b => b.text)
                    .map(b => b.text)
                    .join('');
                if (fullAssistantText.trimEnd().endsWith('[TASK_RESUME]')) {
                    // Strip [TASK_RESUME] from the last text block so it doesn't appear in saved content
                    const lastTextIdx = assistantContent.map(b => !!b.text).lastIndexOf(true);
                    if (lastTextIdx >= 0) {
                        assistantContent[lastTextIdx].text = assistantContent[lastTextIdx].text
                            .replace(/\[TASK_RESUME\]\s*$/, '').trimEnd();
                    }
                    console.log(`[harness] ADO#3923: TASK_RESUME detected, emitting task_resume SSE for userId=${userId}`);
                    sendEvent({ type: 'task_resume' });
                }

                // ADO#4004: TASK_READY self-escalation detection
                // When Vision emits [TASK_READY] in non-task mode, strip it and emit task_ready SSE
                // Blazor handles task_ready by setting _taskMode = true and re-sending the last user message
                if (fullAssistantText.trimEnd().endsWith('[TASK_READY]')) {
                    // Strip [TASK_READY] from the last text block
                    const lastTextIdx = assistantContent.map(b => !!b.text).lastIndexOf(true);
                    if (lastTextIdx >= 0) {
                        assistantContent[lastTextIdx].text = assistantContent[lastTextIdx].text
                            .replace(/\[TASK_READY\]\s*$/, '').trimEnd();
                    }
                    console.log(`[harness] ADO#4004: TASK_READY detected in non-task-mode response for userId=${userId} — escalating to task mode`);
                    sendEvent({ type: 'task_ready' });
                }

                // ADO#3215: if a tool was called, feed the result back to Bedrock and loop
                if (pendingToolResults.length > 0) {
                    messages.push({ role: 'assistant', content: assistantContent });
                    messages.push({
                        role: 'user',
                        content: pendingToolResults.map(r => ({
                            toolResult: {
                                toolUseId: r.toolUseId,
                                content: [{ text: r.toolResultText }],
                                status: r.isError ? 'error' : 'success'
                            }
                        }))
                    });
                    pendingToolResults.length = 0;
                    continueLoop = true;
                    console.log(`[harness] /turn: tool result(s) fed back to Bedrock, looping (iteration ${toolIterations}/${MAX_TOOL_ITERATIONS})`);
                } else {
                    // end_turn with no tool call — done
                    continueLoop = false;
                    console.log(`[harness] /turn: end_turn with no tool call, exiting agentic loop after ${toolIterations} iteration(s)`);
                }
            }
            if (toolIterations >= MAX_TOOL_ITERATIONS) {
                console.warn(`[harness] /turn: MAX_TOOL_ITERATIONS (${MAX_TOOL_ITERATIONS}) reached — agentic loop capped`);
            }
            console.log(`[harness] /turn: stream complete, sending done event for userId=${userId}`);
            // ADO#3093 — fire-and-forget preference detection write
            if (hasPreferenceSignal(message)) {
                firePreferenceWrite(userId, message);
            }
            console.log(`[harness] /turn: done event — inputTokens=${inputTokens}, outputTokens=${outputTokens}`);
            sendEvent({ type: 'done', inputTokens, outputTokens });
            // §G7 — clean up scheduled task context
            if (userId) scheduledTaskUsers.delete(userId);
            res.end();
        } catch (err) {
            console.error(`[harness] /turn: Bedrock ConverseStream error for userId=${userId}: ${err.message}`, err.stack);
            sendEvent({ type: 'error', errorMessage: scrubSecrets(err.message) });
            // §G7 — clean up scheduled task context
            if (userId) scheduledTaskUsers.delete(userId);
            res.end();
        }
    }
});

// Blazor delivers user's approval/denial back to harness
app.post('/intervention/respond', (req, res) => {
    const { interventionId, approved } = req.body || {};
    if (!interventionId) return res.status(400).json({ error: 'interventionId required' });
    const pending = pendingInterventions.get(interventionId);
    if (pending) {
        pendingInterventions.delete(interventionId);
        pending.resolve(approved === true);
    } else {
        console.warn('[harness] /intervention/respond: no pending intervention for id', interventionId);
    }
    res.json({ ok: true });
});

// Session info
app.get('/session', (req, res) => {
    res.json({
        userId: process.env.FAIT_USER_ID || null,
        sessionId: process.env.FAIT_SESSION_ID || null,
        workspaceDir: WORKSPACE_DIR,
        ccModel: process.env.CC_MODEL || 'sonnet'
    });
});

// Bootstrap GCP credentials then start server
(async () => {
    if (!INTERNAL_API_TOKEN) {
        console.warn('[harness] WARNING: INTERNAL_API_TOKEN not set — preference writes will fail with 401');
    }
    // ADO#3289 — startup check: verify claude CLI is available
    try {
        const { execSync } = require('child_process');
        const claudeVersion = execSync('claude --version', { timeout: 10000, encoding: 'utf8' }).trim();
        console.log(`[harness] startup: claude CLI found — ${claudeVersion}`);
    } catch (claudeErr) {
        console.error(`[harness] startup: claude CLI NOT found or failed — task mode will fail. Error: ${claudeErr.message}`);
    }
    await initPgVector();
    await bootstrapGcpCredentials();
    app.listen(PORT, '0.0.0.0', () => {
        console.log(`FAIT v2 agent harness listening on port ${PORT}`);
    });
})();
