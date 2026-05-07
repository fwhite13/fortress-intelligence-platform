# FAIT v2 Agent Harness

## Purpose
Per-user Fargate task — one instance per active user session. Provides the OpenClaw agent runtime, Claude Code CLI access, and an HTTP shim that the FAIT v2 Blazor app calls to dispatch conversation turns.

## Ports
- `3000` — HTTP shim (configurable via `PORT` env var)

## Environment Variables
| Variable | Default | Description |
|---|---|---|
| `PORT` | `3000` | HTTP shim listen port |
| `WORKSPACE_DIR` | `/workspace` | EFS mount base path |
| `CC_MODEL` | `sonnet` | Claude Code model to use |
| `FAIT_USER_ID` | — | User ID (injected by ECS task definition) |
| `FAIT_SESSION_ID` | — | Session ID (injected by ECS task definition) |

## EFS Mount
User workspace files mount at `/workspace/{userId}/` — includes SOUL.md, USER.md, AGENTS.md, MEMORY.md, and memory topic files.

## API Endpoints

### `GET /health`
Health check. Returns `{ status: "healthy", timestamp: "..." }`.

### `POST /turn`
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

### `GET /session`
Returns current session metadata: userId, sessionId, workspaceDir, ccModel.

## Build
```bash
docker build -t fait-v2-harness .
```
> Note: ECR push is handled by Rhodey (pipeline) at deploy time — do not push manually.

## Architecture Note
Each user gets their own Fargate task instance. The Blazor app routes turns to the user's task via the `/turn` endpoint. Responses stream back via SSE, which the Blazor app proxies to SignalR for real-time browser updates.
