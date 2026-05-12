const express = require('express');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const { mkdirSync } = require('fs');
const { SecretsManagerClient, GetSecretValueCommand } = require('@aws-sdk/client-secrets-manager');
const { BedrockRuntimeClient, ConverseStreamCommand } = require('@aws-sdk/client-bedrock-runtime');
const { S3Client, GetObjectCommand, PutObjectCommand, HeadObjectCommand } = require('@aws-sdk/client-s3');
const { BedrockAgentRuntimeClient, RetrieveCommand } = require('@aws-sdk/client-bedrock-agent-runtime');
const mysql = require('mysql2/promise');

const bedrockClient = new BedrockRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });
const s3Client = new S3Client({ region: process.env.AWS_REGION || 'us-east-1' });
const bedrockAgentClient = new BedrockAgentRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });

// ─── DB helpers ───────────────────────────────────────────────────────────
async function getDbConnection() {
    return mysql.createConnection({
        host: process.env.DB_HOST || 'localhost',
        database: process.env.DB_NAME || 'fait',
        user: process.env.DB_USER || 'fait',
        password: process.env.DB_PASSWORD || '',
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
    'list_workspace_files', 'search_memory', 'read_memory', 'write_memory', 'create_document',
    'list_files', 'read_file'
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
        if (!result.found) {
            return res.json({ content: `Topic '${slug}' not found in memory.` });
        }
        res.json({ content: result.content });
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
        const s3Key = `workspaces/${userId}/artifacts/${conversationId}/${filename}`;

        // 3. Upload to S3
        await s3Client.send(new PutObjectCommand({
            Bucket: S3_BUCKET,
            Key: s3Key,
            Body: docBytes,
            ContentType: 'application/vnd.openxmlformats-officedocument.wordprocessingml.document'
        }));

        res.json({ success: true, filename, s3Key, sizeBytes });
    } catch (err) {
        console.error('[harness] create_document error:', err.message);
        res.status(500).json({ error: err.message });
    }
});

// ─── list_files tool handler (ADO#3206) ──────────────────────────────────
app.post('/tools/list_files', async (req, res) => {
    const { userId, folder_path = '' } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });

    // ADO#3301 — replaced direct DB connection with Blazor internal API
    try {
        const blazorBase = FAIT_BASE_URL;
        const headers = { 'Content-Type': 'application/json' };
        if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;

        // NOTE: folder_path-to-folderId resolution is not implemented in the internal API.
        // For now, always list root-level items. folder_path is ignored.
        // Future: resolve folder_path to a folderId via the folders endpoint.
        if (folder_path) {
            console.warn(`[list_files] folder_path="${folder_path}" not yet supported via Blazor API — listing root`);
        }

        const apiRes = await fetch(`${blazorBase}/api/workspace/internal/list-files`, {
            method: 'POST',
            headers,
            body: JSON.stringify({ UserId: userId }),
        });

        if (!apiRes.ok) {
            const errBody = await apiRes.text();
            console.error(`[list_files] Blazor API error: status=${apiRes.status} body=${errBody}`);
            return res.status(500).json({ error: `Blazor API error: ${apiRes.status}` });
        }

        const data = await apiRes.json();
        res.json({ items: data.items || [] });
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

        // Resolve folder
        let folderId = null;
        if (folderPath.length > 0) {
            let parentId = null;
            for (const segment of folderPath) {
                const paramArr = parentId === null ? [userId, segment] : [userId, segment, parentId];
                const sql = parentId === null
                    ? 'SELECT id FROM user_workspace_folders WHERE user_id = ? AND name = ? AND parent_id IS NULL'
                    : 'SELECT id FROM user_workspace_folders WHERE user_id = ? AND name = ? AND parent_id = ?';
                const [rows] = await conn.execute(sql, paramArr);
                if (rows.length === 0) {
                    return res.json({ content: `File not found: ${file_path}` });
                }
                parentId = rows[0].id;
            }
            folderId = parentId;
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
    const { userId, folder = '' } = req.body || {};
    if (!userId) return res.status(400).json({ error: 'userId required' });

    const prefix = `workspaces/${userId}/${folder ? folder.replace(/^\/+|\/+$/g, '') + '/' : ''}`;

    try {
        const { S3Client: S3ClientLocal, ListObjectsV2Command } = require('@aws-sdk/client-s3');
        const s3 = new S3ClientLocal({ region: process.env.AWS_REGION || 'us-east-1' });

        const cmd = new ListObjectsV2Command({
            Bucket: S3_BUCKET,
            Prefix: prefix,
            MaxKeys: 200,
        });
        const resp = await s3.send(cmd);

        const files = (resp.Contents || []).map(obj => ({
            name: obj.Key.replace(prefix, ''),
            size: obj.Size,
            modified: obj.LastModified,
        })).filter(f => f.name && !f.name.endsWith('/'));

        res.json({ files, prefix, truncated: resp.IsTruncated ?? false });
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

async function executeKbSearch(query, kbType, userId, kbAccess) {
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
        // kbAccess unavailable (internal API unreachable) — fail open, log warning
        console.warn(`[harness] executeKbSearch: kbAccess unavailable for userId=${userId} kbType=${kbType} — proceeding without enforcement (fail-open)`);
    }

    // For team KB: use authorizedTeamIds from kbAccess (not model input)
    // For personal KB: use personalUserId from kbAccess (not model input)
    if (kbType === 'team' && kbAccess?.authorizedTeamIds?.length > 0) {
        // Retrieve from each authorized team and merge results
        const kbId = process.env.TEAM_KB_ID;
        if (!kbId) {
            console.warn(`[harness] KB search: no TEAM_KB_ID configured`);
            return { text: 'No knowledge base configured for type: team', sources: [] };
        }
        try {
            const allResults = [];
            for (const teamId of kbAccess.authorizedTeamIds) {
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

async function scanAndUploadArtifacts(userId, workspaceDir) {
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
    const s3Key = `${S3_PREFIX}artifacts/${userId}/${latestFile.name}`;
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

    await s3Client.send(new PutObjectCommand({
        Bucket: S3_BUCKET,
        Key: s3Key,
        Body: fileBuffer,
        ContentType: contentTypes[ext] || 'application/octet-stream',
    }));

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
function classifyRequest(message, history) {
    const msg = (message || '').toLowerCase();

    // File extension signals
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
    const conversationId  = rawBody.ConversationId  ?? rawBody.conversationId  ?? '';
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
                        modelId: MODEL_ID,
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
        // ── CC spawn path (unchanged) ─────────────────────────────────────
        const userWorkspaceDir = `${WORKSPACE_DIR}/${userId}`;
        let ended = false;
        const endResponse = (data) => {
            if (ended) return;
            ended = true;
            sendEvent(data);
            res.end();
        };
        try {
            mkdirSync(userWorkspaceDir, { recursive: true });
        } catch (mkErr) {
            return endResponse({ type: 'error', errorMessage: `Cannot create workspace: ${mkErr.message}` });
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
        if (memoryMd) contextParts.push(`## Long-Term Memory\n${memoryMd}`);
        // Memory tool guidance (ADO#3188)
        contextParts.push(`You have access to read_memory(slug) and write_memory(slug, title, content) tools.
- On cold start, MEMORY.md lists available topic slugs. Call read_memory to fetch any topic relevant to the current conversation.
- When the user states a preference, personal detail, or decision worth remembering, call write_memory to persist it. Use judgment — not every message warrants a memory write.
- For new information that does not fit an existing topic, create a new topic with an appropriate slug and title.
\nYou have access to create_document(type, title, sections[]) to produce file output.
- Use type="word" for Word documents.
- Call this when the user asks you to create a document, report, proposal, or similar deliverable.
- You may also call this proactively when a document is clearly the best way to present your output (e.g. a structured report, a multi-section analysis).
- sections is an array of { heading, content } objects.
- After calling create_document, briefly confirm to the user that the document was created and is available in the chat.
\nYou have access to list_files(folder_path?) and read_file(file_path) to access files the user has uploaded to their workspace.
- Use list_files() to see what is available at the root, or list_files("folder/path") for a subfolder.
- Use read_file("path/to/file") to read file content directly.
- Paths use forward slashes. Folder and file names are case-sensitive.
- Prefer reading workspace files directly when the user references "my files", "the document I uploaded", or similar.`);
        if (systemPrompt) contextParts.push(systemPrompt);

        // ADO#3089 — inject session context recap on cold-start CC turns with existing history
        const hasHistory = Array.isArray(history) && history.length > 0;
        if (hasHistory) {
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
            console.log(`[harness] /turn: injected session recap (${recap.length} chars, ${recentMessages.length} messages) into CC context`);
        }

        const fullContext = contextParts.join('\n\n---\n\n');
        const briefContent = fullContext
            ? `${fullContext}\n\n---\n\nUser: ${message}`
            : message;
        // Pre-stage: sync user workspace from S3 → local
        try {
            const { execSync } = require('child_process');
            execSync(
                `aws s3 sync s3://${S3_BUCKET}/workspaces/${userId}/ ${userWorkspaceDir}/ --quiet`,
                { timeout: 30000, stdio: ['ignore', 'pipe', 'pipe'] }
            );
            console.log(`[harness] workspace synced from S3 for userId=${userId}`);
        } catch (syncErr) {
            console.warn(`[harness] pre-run S3 sync failed (non-fatal): ${syncErr.message}`);
            // Never block — continue with whatever is already local
        }

        // ADO#3289 — log the exact command being spawned
        const ccArgs = [
            '--model', process.env.CC_MODEL || 'sonnet',
            '--print',
            '--output-format', 'stream-json',
            '--dangerously-skip-permissions'
        ];
        console.log(`[CC spawn] command=claude ${ccArgs.join(' ')} cwd=${userWorkspaceDir} userId=${userId} briefLen=${briefContent?.length ?? 0}`);
        const ccProcess = spawn('claude', ccArgs, {
            cwd: userWorkspaceDir,
            env: {
                ...process.env,
                CLAUDE_CODE_ENTRYPOINT: 'fargate-harness',
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
                    for (const block of parsed.message.content) {
                        if (block.type === 'text' && block.text) {
                            ccTextEmitted = true;
                            sendEvent({ type: 'text', content: scrubSecrets(block.text) });
                        } else if (block.type === 'tool_use') {
                            toolUseMap.set(block.id, block.name || 'tool');
                            sendEvent({ type: 'task_progress', payload: JSON.stringify({ step: 'tool_use', toolName: block.name, status: 'calling', message: `Calling ${block.name}...` }) });
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
                artifact = await scanAndUploadArtifacts(userId, userWorkspaceDir);
            } catch (err) {
                console.error('[harness] artifact upload failed:', err.message);
            }

            // Post-run: sync local workspace back to S3
            try {
                const { execSync } = require('child_process');
                execSync(
                    `aws s3 sync ${userWorkspaceDir}/ s3://${S3_BUCKET}/workspaces/${userId}/ --quiet`,
                    { timeout: 30000, stdio: ['ignore', 'pipe', 'pipe'] }
                );
                console.log(`[harness] workspace synced to S3 for userId=${userId}`);
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
            if (memoryMd) systemParts.push(`## Long-Term Memory\n${memoryMd}`);
            // Memory tool guidance (ADO#3188)
            systemParts.push(`You have access to read_memory(slug) and write_memory(slug, title, content) tools.
- On cold start, MEMORY.md lists available topic slugs. Call read_memory to fetch any topic relevant to the current conversation.
- When the user states a preference, personal detail, or decision worth remembering, call write_memory to persist it. Use judgment — not every message warrants a memory write.
- For new information that does not fit an existing topic, create a new topic with an appropriate slug and title.
\nYou have access to create_document(type, title, sections[]) to produce file output.
- Use type="word" for Word documents.
- Call this when the user asks you to create a document, report, proposal, or similar deliverable.
- You may also call this proactively when a document is clearly the best way to present your output (e.g. a structured report, a multi-section analysis).
- sections is an array of { heading, content } objects.
- After calling create_document, briefly confirm to the user that the document was created and is available in the chat.
\nYou have access to list_files(folder_path?) and read_file(file_path) to access files the user has uploaded to their workspace.
- Use list_files() to see what is available at the root, or list_files("folder/path") for a subfolder.
- Use read_file("path/to/file") to read file content directly.
- Paths use forward slashes. Folder and file names are case-sensitive.
- Prefer reading workspace files directly when the user references "my files", "the document I uploaded", or similar.`);
            if (systemPrompt) systemParts.push(systemPrompt);
            if (systemParts.length === 0) {
                systemParts.push('You are a helpful AI assistant.');
            }
            let fullSystemPrompt = systemParts.join('\n\n---\n\n');
            console.log(`[harness] /turn: system prompt built, totalLen=${fullSystemPrompt.length}`);

            // ADO#3241 — Harness-side KB retrieval
            const kbFlags = rawBody.KbFlags ?? rawBody.kbFlags ?? null;
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
                        kbPromises.push(doKbRetrieval(process.env.CORP_KB_ID, 'Corp KB', message, null, null));
                    }
                    if (kbFlags.PersonalKbEnabled || kbFlags.personalKbEnabled) {
                        // Personal KB: filter by ownerId = user's GUID
                        if (!personalKbUserId) {
                            console.warn(`[harness] /turn: Personal KB requested but no PersonalKbUserId in kbFlags — skipping for security`);
                        } else {
                            kbPromises.push(doKbRetrieval(process.env.PERSONAL_KB_ID, 'Personal KB', message, 'ownerId', personalKbUserId));
                        }
                    }
                    if (kbFlags.TeamKbEnabled || kbFlags.teamKbEnabled) {
                        // Team KB: one retrieval per team ID, each filtered by teamId
                        const effectiveTeamIds = teamIds && teamIds.length > 0 ? teamIds : null;
                        if (!effectiveTeamIds) {
                            console.warn(`[harness] /turn: Team KB requested but no TeamIds in kbFlags — skipping for security`);
                        } else {
                            for (const teamId of effectiveTeamIds) {
                                kbPromises.push(doKbRetrieval(process.env.TEAM_KB_ID, `Team KB (${teamId})`, message, 'teamId', teamId));
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
                            description: 'List files in the user workspace. Returns filename, size, and last modified date.',
                            inputSchema: {
                                json: {
                                    type: 'object',
                                    properties: {
                                        folder: {
                                            type: 'string',
                                            description: 'Optional subfolder path within the workspace (e.g. "memory" or "artifacts"). Omit for root workspace listing.'
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

            console.log(`[harness] /turn: calling bedrockClient.send for userId=${userId}, modelId=${MODEL_ID}`);
            let tokenCount = 0;
            let inputTokens = 0;
            let outputTokens = 0;
            const MAX_TOOL_ITERATIONS = 10;
            let toolIterations = 0;
            let continueLoop = true;

            while (continueLoop && toolIterations < MAX_TOOL_ITERATIONS) {
                toolIterations++;
                let assistantTextAccumulator = '';
                let assistantContent = [];
                let toolUseAccumulator = null;
                let messageStopSeen = false;
                let stopReason = 'end_turn';
                // pendingToolResults replaces the scalar — handles multiple toolUse blocks per turn
                const pendingToolResults = [];
                let kbAccessForTurn = null; // ADO#3309 — cached per-turn KB access entitlements for Path B enforcement

                const cmd = new ConverseStreamCommand({
                    modelId: MODEL_ID,
                    messages,
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
                                    body: JSON.stringify({ userId, folder: toolInput.folder || '' })
                                });
                                const wsData = await wsRes.json();
                                toolResultText = `\n\n[Workspace Files]\n${JSON.stringify(wsData, null, 2)}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'done', `${toolUseAccumulator.name} complete`);
                            } catch (wsErr) {
                                toolResultText = `\n\n[Workspace Files Error]\n${wsErr.message}\n\n`;
                                emitToolCall(res, 'builtin', toolUseAccumulator.name, 'error', `Error: ${wsErr.message.substring(0,100)}`);
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
                                // ADO#3309 — Fetch authoritative KB access (fetch once per turn, reuse on retry)
                                if (!kbAccessForTurn) {
                                    kbAccessForTurn = await fetchKbAccess(userId);
                                }
                                const kbSearchResult = await executeKbSearch(toolInput.query, toolInput.kb_type || 'personal', userId, kbAccessForTurn);
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
    await bootstrapGcpCredentials();
    app.listen(PORT, '0.0.0.0', () => {
        console.log(`FAIT v2 agent harness listening on port ${PORT}`);
    });
})();
