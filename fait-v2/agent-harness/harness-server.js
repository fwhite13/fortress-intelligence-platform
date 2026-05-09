const express = require('express');
const { spawn } = require('child_process');
const { mkdirSync } = require('fs');
const { SecretsManagerClient, GetSecretValueCommand } = require('@aws-sdk/client-secrets-manager');
const { BedrockRuntimeClient, ConverseStreamCommand } = require('@aws-sdk/client-bedrock-runtime');
const { S3Client, GetObjectCommand } = require('@aws-sdk/client-s3');

const bedrockClient = new BedrockRuntimeClient({ region: process.env.AWS_REGION || 'us-east-1' });
const s3Client = new S3Client({ region: process.env.AWS_REGION || 'us-east-1' });
const MODEL_ID = 'us.anthropic.claude-sonnet-4-6';
const S3_BUCKET = process.env.WORKSPACE_S3_BUCKET || 'fortress-user-workspaces';
const S3_PREFIX = process.env.WORKSPACE_S3_PREFIX || '';
const { existsSync, writeFileSync } = require('fs');
const app = express();
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

// Stitch MCP health check
app.get('/tools/stitch/health', (req, res) => {
    const credPath = process.env.GOOGLE_APPLICATION_CREDENTIALS;
    const available = !!(credPath && existsSync(credPath));
    res.json({ available, reason: available ? 'ok' : 'GCP credentials not configured' });
});

// Tool dispatch — Stitch MCP tools
app.post('/tools/:toolName', async (req, res) => {
    const { toolName } = req.params;
    const args = req.body || {};

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

app.post('/turn', async (req, res) => {
    console.log('[harness] /turn received: userId=%s, hasMessage=%s, taskMode=%s',
        req.body?.UserId ?? '(none)', !!req.body?.Message, req.body?.TaskMode ?? false);
    console.log(`[harness] /turn: request received. body keys=${Object.keys(req.body || {}).join(',')}, contentType=${req.headers['content-type']}`);
    const rawBody = req.body || {};
    console.log(`[harness] /turn: raw body dump: ${JSON.stringify(rawBody).substring(0, 500)}`);

    const { SessionId: sessionId, UserId: userId, Message: message, SystemPrompt: systemPrompt, TaskMode: taskMode, History: history } = rawBody;
    console.log(`[harness] /turn: destructured: userId=${userId}, messageLen=${message?.length}, taskMode=${taskMode}, historyLen=${Array.isArray(history) ? history.length : 'n/a'}, sessionId=${sessionId}`);

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

    console.log(`[harness] /turn: validation passed for userId=${userId}, starting SSE response`);
    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');

    const sendEvent = (data) => {
        console.log(`[harness] /turn: sendEvent type=${data.type}, contentLen=${data.content?.length ?? 0}, errorMessage=${data.errorMessage ?? ''}`);
        res.write(`data: ${JSON.stringify(data)}\n\n`);
    };

    if (taskMode) {
        console.log(`[harness] /turn: taskMode=true — entering CC spawn path for userId=${userId}`);
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
        if (soulMd) contextParts.push(`## Assistant Identity\n${soulMd}`);
        if (userMd) contextParts.push(`## About the User\n${userMd}`);
        if (memoryMd) contextParts.push(`## Long-Term Memory\n${memoryMd}`);
        if (systemPrompt) contextParts.push(systemPrompt);

        const fullContext = contextParts.join('\n\n---\n\n');
        const briefContent = fullContext
            ? `${fullContext}\n\n---\n\nUser: ${message}`
            : message;
        const ccProcess = spawn('claude', [
            '--model', process.env.CC_MODEL || 'sonnet',
            '--print',
            '--dangerously-skip-permissions'
        ], {
            cwd: userWorkspaceDir,
            env: {
                ...process.env,
                CLAUDE_CODE_ENTRYPOINT: 'fargate-harness',
                CLAUDE_CODE_DISABLE_AUTO_MEMORY: '1',
                CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR: '1',
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
        ccProcess.stdout.on('data', (chunk) => sendEvent({ type: 'text', content: chunk.toString() }));
        ccProcess.stderr.on('data', (chunk) => sendEvent({ type: 'log', content: chunk.toString() }));
        ccProcess.on('close', (code) => { clearTimeout(timeout); endResponse({ type: 'done', exitCode: code }); });
        ccProcess.on('error', (err) => { clearTimeout(timeout); endResponse({ type: 'error', errorMessage: err.message }); });
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
            if (soulMd) systemParts.push(`## Assistant Identity\n${soulMd}`);
            if (userMd) systemParts.push(`## About the User\n${userMd}`);
            if (memoryMd) systemParts.push(`## Long-Term Memory\n${memoryMd}`);
            if (systemPrompt) systemParts.push(systemPrompt);
            if (systemParts.length === 0) {
                systemParts.push('You are a helpful AI assistant.');
            }
            const fullSystemPrompt = systemParts.join('\n\n---\n\n');
            console.log(`[harness] /turn: system prompt built, totalLen=${fullSystemPrompt.length}`);

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

            const cmd = new ConverseStreamCommand({
                modelId: MODEL_ID,
                messages,
                system: [{ text: fullSystemPrompt }],
                inferenceConfig: { maxTokens: 4096, temperature: 0.7 }
            });

            console.log(`[harness] /turn: calling bedrockClient.send for userId=${userId}, modelId=${MODEL_ID}`);
            const response = await bedrockClient.send(cmd);
            console.log(`[harness] /turn: Bedrock stream opened, beginning event iteration`);
            let tokenCount = 0;
            for await (const event of response.stream) {
                if (event.contentBlockDelta?.delta?.text) {
                    tokenCount++;
                    sendEvent({ type: 'text', content: event.contentBlockDelta.delta.text });
                } else if (event.messageStop) {
                    console.log(`[harness] /turn: messageStop received after ${tokenCount} text events`);
                    break;
                } else {
                    console.log(`[harness] /turn: stream event (non-text): ${JSON.stringify(Object.keys(event))}`);
                }
            }
            console.log(`[harness] /turn: stream complete, sending done event for userId=${userId}`);
            sendEvent({ type: 'done' });
            res.end();
        } catch (err) {
            console.error(`[harness] /turn: Bedrock ConverseStream error for userId=${userId}: ${err.message}`, err.stack);
            sendEvent({ type: 'error', errorMessage: err.message });
            res.end();
        }
    }
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
    await bootstrapGcpCredentials();
    app.listen(PORT, '0.0.0.0', () => {
        console.log(`FAIT v2 agent harness listening on port ${PORT}`);
    });
})();
