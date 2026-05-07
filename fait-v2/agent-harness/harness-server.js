const express = require('express');
const { spawn } = require('child_process');
const { mkdirSync } = require('fs');
const { SecretsManagerClient, GetSecretValueCommand } = require('@aws-sdk/client-secrets-manager');
const { existsSync, writeFileSync } = require('fs');
const app = express();
app.use(express.json({ limit: '10mb' }));

const PORT = process.env.PORT || 3000;
const WORKSPACE_DIR = process.env.WORKSPACE_DIR || '/workspace';

// Health check
app.get('/health', (req, res) => {
    res.json({ status: 'healthy', timestamp: new Date().toISOString() });
});

// ─── GCP credential bootstrap ─────────────────────────────────────────────
async function bootstrapGcpCredentials() {
    const secretName = process.env.GCP_STITCH_SECRET_NAME || 'fait-v2/gcp-stitch-service-account';
    try {
        const client = new SecretsManagerClient({ region: process.env.AWS_REGION || 'us-east-1' });
        const response = await client.send(new GetSecretValueCommand({ SecretId: secretName }));
        const credPath = '/tmp/gcp-service-account.json';
        writeFileSync(credPath, response.SecretString, { mode: 0o600 });
        process.env.GOOGLE_APPLICATION_CREDENTIALS = credPath;
        console.log('[harness] GCP credentials bootstrapped from Secrets Manager');
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

function invokeStitchTool(toolName, args) {
    return new Promise((resolve, reject) => {
        const proc = spawn('stitch-mcp', [], {
            env: { ...process.env },
            stdio: ['pipe', 'pipe', 'pipe']
        });

        // Send MCP initialize then tools/call via JSON-RPC over stdio
        const initMsg = JSON.stringify({
            jsonrpc: '2.0', id: 1,
            method: 'initialize',
            params: {
                protocolVersion: '2024-11-05',
                capabilities: {},
                clientInfo: { name: 'fait-v2-harness', version: '1.0' }
            }
        }) + '\n';

        const callMsg = JSON.stringify({
            jsonrpc: '2.0', id: 2,
            method: 'tools/call',
            params: { name: toolName, arguments: args || {} }
        }) + '\n';

        let stdout = '';
        let stderr = '';

        proc.stdout.on('data', (chunk) => { stdout += chunk.toString(); });
        proc.stderr.on('data', (chunk) => { stderr += chunk.toString(); });

        proc.on('close', (code) => {
            // Parse all JSON-RPC messages from stdout, find id=2 response
            const lines = stdout.split('\n').filter(l => l.trim());
            for (const line of lines) {
                try {
                    const msg = JSON.parse(line);
                    if (msg.id === 2) {
                        if (msg.error) {
                            return reject(new Error(msg.error.message || JSON.stringify(msg.error)));
                        }
                        return resolve(msg.result);
                    }
                } catch (_) { /* skip non-JSON lines */ }
            }
            reject(new Error(`stitch-mcp exited ${code} with no result. stderr: ${stderr.slice(0, 200)}`));
        });

        proc.on('error', (err) => reject(err));

        // Write both messages then close stdin
        proc.stdin.write(initMsg);
        proc.stdin.write(callMsg);
        proc.stdin.end();
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

// Dispatch a turn to CC CLI
// Body: { sessionId, userId, message, systemPrompt, tools }
// Returns streaming SSE
app.post('/turn', async (req, res) => {
    const { sessionId, userId, message, systemPrompt } = req.body;

    if (!userId || !message) {
        return res.status(400).json({ error: 'userId and message required' });
    }

    // Validate userId — no path traversal
    if (typeof userId !== 'string' ||
        userId.includes('..') || userId.includes('/') || userId.includes('\\') ||
        !/^[a-zA-Z0-9_-]{1,64}$/.test(userId)) {
        return res.status(400).json({ error: 'Invalid userId' });
    }

    const userWorkspaceDir = `${WORKSPACE_DIR}/${userId}`;

    // Set up SSE for streaming
    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');

    try {
        let ended = false;
        const endResponse = (data) => {
            if (ended) return;
            ended = true;
            res.write(`data: ${JSON.stringify(data)}\n\n`);
            res.end();
        };

        // I5: Ensure workspace dir exists before spawning CC
        try {
            mkdirSync(userWorkspaceDir, { recursive: true });
        } catch (mkErr) {
            return endResponse({ type: 'error', message: `Cannot create workspace: ${mkErr.message}` });
        }

        // Build CC CLI invocation
        // System prompt passed via stdin as a brief file
        const briefContent = systemPrompt
            ? `${systemPrompt}\n\n---\n\nUser: ${message}`
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
            endResponse({ type: 'error', message: 'Turn timed out after 5 minutes' });
        }, TURN_TIMEOUT_MS);

        ccProcess.stdout.on('data', (chunk) => {
            res.write(`data: ${JSON.stringify({ type: 'text', content: chunk.toString() })}\n\n`);
        });

        ccProcess.stderr.on('data', (chunk) => {
            res.write(`data: ${JSON.stringify({ type: 'log', content: chunk.toString() })}\n\n`);
        });

        ccProcess.on('close', (code) => {
            clearTimeout(timeout);
            endResponse({ type: 'done', exitCode: code });
        });

        ccProcess.on('error', (err) => {
            clearTimeout(timeout);
            endResponse({ type: 'error', message: err.message });
        });

    } catch (err) {
        endResponse({ type: 'error', message: err.message });
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
