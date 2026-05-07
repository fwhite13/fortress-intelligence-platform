const express = require('express');
const { spawn } = require('child_process');
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

    const userWorkspaceDir = `${WORKSPACE_DIR}/${userId}`;

    // Set up SSE for streaming
    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');

    try {
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

        ccProcess.stdout.on('data', (chunk) => {
            res.write(`data: ${JSON.stringify({ type: 'text', content: chunk.toString() })}\n\n`);
        });

        ccProcess.stderr.on('data', (chunk) => {
            res.write(`data: ${JSON.stringify({ type: 'log', content: chunk.toString() })}\n\n`);
        });

        ccProcess.on('close', (code) => {
            res.write(`data: ${JSON.stringify({ type: 'done', exitCode: code })}\n\n`);
            res.end();
        });

        ccProcess.on('error', (err) => {
            res.write(`data: ${JSON.stringify({ type: 'error', message: err.message })}\n\n`);
            res.end();
        });

    } catch (err) {
        res.write(`data: ${JSON.stringify({ type: 'error', message: err.message })}\n\n`);
        res.end();
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
