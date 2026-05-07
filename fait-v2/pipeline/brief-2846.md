# CC Brief — ADO #2846: Fargate Agent Harness Image

## Task
Create the `agent-harness/` directory in `~/projects/fip/fait-v2/` with all files for the FAIT v2 per-user Fargate agent runtime container.

## Working directory
`/home/fredw/projects/fip/fait-v2`

## Files to Create

### 1. `agent-harness/Dockerfile`

```dockerfile
FROM node:20-slim

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

# Install Claude Code CLI
RUN npm install -g @anthropic-ai/claude-code

# Set up app directory
WORKDIR /app
COPY package.json ./
RUN npm install --production
COPY harness-server.js ./

# Create workspace mount point (EFS will mount here)
RUN mkdir -p /workspace

# Expose HTTP shim port
EXPOSE 3000

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:3000/health || exit 1

CMD ["node", "harness-server.js"]
```

### 2. `agent-harness/harness-server.js`

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

### 3. `agent-harness/package.json`

```json
{
  "name": "fait-v2-agent-harness",
  "version": "1.0.0",
  "description": "FAIT v2 per-user Fargate agent harness — OpenClaw runtime HTTP shim",
  "main": "harness-server.js",
  "scripts": {
    "start": "node harness-server.js"
  },
  "dependencies": {
    "express": "^4.18.0"
  },
  "engines": {
    "node": ">=20"
  }
}
```

### 4. `agent-harness/.dockerignore`

```
node_modules
*.log
.git
README.md
```

### 5. `agent-harness/README.md`

Write a README with these sections:

## FAIT v2 Agent Harness

### Purpose
Per-user Fargate task — one instance per active user session. Provides the OpenClaw agent runtime, Claude Code CLI access, and an HTTP shim that the FAIT v2 Blazor app calls to dispatch conversation turns.

### Ports
- `3000` — HTTP shim (configurable via `PORT` env var)

### Environment Variables
| Variable | Default | Description |
|---|---|---|
| `PORT` | `3000` | HTTP shim listen port |
| `WORKSPACE_DIR` | `/workspace` | EFS mount base path |
| `CC_MODEL` | `sonnet` | Claude Code model to use |
| `FAIT_USER_ID` | — | User ID (injected by ECS task definition) |
| `FAIT_SESSION_ID` | — | Session ID (injected by ECS task definition) |

### EFS Mount
User workspace files mount at `/workspace/{userId}/` — includes SOUL.md, USER.md, AGENTS.md, MEMORY.md, and memory topic files.

### API Endpoints

#### `GET /health`
Health check. Returns `{ status: "healthy", timestamp: "..." }`.

#### `POST /turn`
Dispatch a conversation turn to Claude Code CLI.

**Request body:**
```json
{
  "userId": "user-guid",
  "message": "User's message text",
  "systemPrompt": "Optional system prompt / SOUL.md content",
  "sessionId": "optional-session-id"
}
```

**Response:** SSE stream of events:
```
data: {"type":"text","content":"..."}
data: {"type":"log","content":"..."}
data: {"type":"done","exitCode":0}
data: {"type":"error","message":"..."}
```

#### `GET /session`
Returns current session metadata: userId, sessionId, workspaceDir, ccModel.

### Build
```bash
docker build -t fait-v2-harness .
```
> Note: ECR push is handled by Rhodey (pipeline) at deploy time — do not push manually.

### Architecture Note
Each user gets their own Fargate task instance. The Blazor app routes turns to the user's task via the `/turn` endpoint. Responses stream back via SSE, which the Blazor app proxies to SignalR for real-time browser updates.

## Instructions for CC

1. Create the directory `agent-harness/` at the root of the working directory (`/home/fredw/projects/fip/fait-v2/`)
2. Create all 5 files exactly as specified above
3. Do NOT modify any existing files (no .cs, no .csproj, no existing Dockerfiles)
4. Do NOT run `npm install`, `docker build`, or any build commands
5. After creating files, output a summary listing all 5 files created with their byte counts
