# CC Brief — ADO#2846 BUILD cycle 2

Apply exactly 5 fixes to two files. Do not change anything else.

---

## File 1: `agent-harness/Dockerfile`

### Fix C1 — Merge `unzip` into first RUN layer

**Current state (lines 3–15):**
```dockerfile
# Install system dependencies
RUN apt-get update && apt-get install -y \
    curl \
    git \
    ca-certificates \
    && rm -rf /var/lib/apt/lists/*

# Install AWS CLI v2 (for S3 workspace access)
RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o /tmp/awscli.zip \
    && apt-get install -y unzip \
    && unzip /tmp/awscli.zip -d /tmp \
    && /tmp/aws/install \
    && rm -rf /tmp/awscli.zip /tmp/aws
```

**Replace with:**
```dockerfile
# Install system dependencies
RUN apt-get update && apt-get install -y \
    curl \
    git \
    ca-certificates \
    unzip \
    && rm -rf /var/lib/apt/lists/*

# Install AWS CLI v2 (for S3 workspace access)
RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o /tmp/awscli.zip \
    && unzip /tmp/awscli.zip -d /tmp \
    && /tmp/aws/install \
    && rm -rf /tmp/awscli.zip /tmp/aws
```

---

## File 2: `agent-harness/harness-server.js`

Apply fixes C2, I3, I4, I5. The current file content is:

```javascript
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
```

**Rewrite `harness-server.js` with ALL of the following changes applied:**

### Fix C2 — userId path traversal validation
After the existing `if (!userId || !message)` check (and before constructing `userWorkspaceDir`), add:
```javascript
// Validate userId — no path traversal
if (typeof userId !== 'string' ||
    userId.includes('..') || userId.includes('/') || userId.includes('\\') ||
    !/^[a-zA-Z0-9_-]{1,64}$/.test(userId)) {
    return res.status(400).json({ error: 'Invalid userId' });
}
```

### Fix I5 — Guard workspace dir existence before CC spawn
Add `const { mkdirSync } = require('fs');` at the top (alongside the existing requires). Then, inside the `/turn` handler in the `try` block, BEFORE the `const briefContent = ...` line, add:
```javascript
// I5: Ensure workspace dir exists before spawning CC
try {
    mkdirSync(userWorkspaceDir, { recursive: true });
} catch (mkErr) {
    return endResponse({ type: 'error', message: `Cannot create workspace: ${mkErr.message}` });
}
```

### Fix I3 — ended flag to prevent double res.end()
Replace the `try` block structure so that a shared `ended` flag and `endResponse` helper are used. The `endResponse` function should be defined at the top of the `try` block (before spawning):
```javascript
let ended = false;
const endResponse = (data) => {
    if (ended) return;
    ended = true;
    res.write(`data: ${JSON.stringify(data)}\n\n`);
    res.end();
};
```

### Fix I4 — 5-minute timeout on /turn
After spawning the CC process (after `ccProcess.stdin.end()`), add:
```javascript
const TURN_TIMEOUT_MS = parseInt(process.env.CC_TIMEOUT_MS || '300000', 10);
const timeout = setTimeout(() => {
    ccProcess.kill('SIGTERM');
    endResponse({ type: 'error', message: 'Turn timed out after 5 minutes' });
}, TURN_TIMEOUT_MS);
```

Then replace the `ccProcess.on('close', ...)` handler with:
```javascript
ccProcess.on('close', (code) => {
    clearTimeout(timeout);
    endResponse({ type: 'done', exitCode: code });
});
```

And replace the `ccProcess.on('error', ...)` handler with:
```javascript
ccProcess.on('error', (err) => {
    clearTimeout(timeout);
    endResponse({ type: 'error', message: err.message });
});
```

And replace the outer `catch` block with:
```javascript
    } catch (err) {
        endResponse({ type: 'error', message: err.message });
    }
```

---

## Important Notes
- The `endResponse` helper must be defined BEFORE the `mkdirSync` block (I5 uses `endResponse` in its catch).
- All 5 fixes must be in the final output of both files.
- Do not add comments beyond what is specified.
- Do not change anything else in either file.

## Output
Overwrite `agent-harness/Dockerfile` and `agent-harness/harness-server.js` with the fixed versions.
