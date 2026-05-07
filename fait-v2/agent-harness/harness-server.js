const express = require('express');
const { spawn } = require('child_process');
const { mkdirSync } = require('fs');
const app = express();
app.use(express.json({ limit: '10mb' }));

const PORT = process.env.PORT || 3000;
const WORKSPACE_DIR = process.env.WORKSPACE_DIR || '/workspace';

// Health check
app.get('/health', (req, res) => {
    res.json({ status: 'healthy', timestamp: new Date().toISOString() });
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

app.listen(PORT, '0.0.0.0', () => {
    console.log(`FAIT v2 agent harness listening on port ${PORT}`);
});
