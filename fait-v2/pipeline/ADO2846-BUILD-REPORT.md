# Build Report — ADO #2846

**WI:** FAIT v2: Fargate agent harness image - OpenClaw runtime, CC CLI, MCP clients, HTTP shim
**Commit:** `0ff8b4f`
**Branch:** `main`
**Date:** 2026-05-06

---

## What was built

Created `agent-harness/` — the FAIT v2 per-user Fargate container. Provides a Node.js Express HTTP shim that the Blazor app calls to dispatch conversation turns to Claude Code CLI, streaming responses back via SSE.

---

## Files changed

- `fait-v2/agent-harness/Dockerfile` — node:20-slim base; installs curl, git, ca-certificates, AWS CLI v2 (via official installer), Claude Code CLI via npm; copies Express app; creates `/workspace` EFS mount point; HEALTHCHECK on `/health`; EXPOSE 3000
- `fait-v2/agent-harness/harness-server.js` — Express app with three endpoints: `GET /health` (status JSON), `POST /turn` (spawns `claude --print --dangerously-skip-permissions`, streams stdout/stderr as SSE events of type `text/log/done/error`), `GET /session` (returns userId, sessionId, workspaceDir, ccModel from env)
- `fait-v2/agent-harness/package.json` — `express ^4.18.0` dependency; `node >=20` engine constraint
- `fait-v2/agent-harness/.dockerignore` — excludes `node_modules`, `*.log`, `.git`, `README.md`
- `fait-v2/agent-harness/README.md` — Purpose, port, env var table, EFS mount explanation, API endpoint docs with request/response shapes, build note (ECR push is Rhodey's job), architecture note on per-user Fargate routing

---

## Parallelization used

No — single CC session, all files created in one pass. No dependencies between files warranted parallelization.

---

## CC sessions run

1 — `cat pipeline/brief-2846.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Acceptance criteria verification

- [x] `agent-harness/` directory created with all 5 files — verified via `ls -la`
- [x] `Dockerfile` installs node:20, AWS CLI, Claude Code CLI — confirmed in file content
- [x] `harness-server.js` has `/health`, `/turn`, `/session` endpoints — confirmed in file content
- [x] `/turn` streams CC CLI output via SSE — stdout/stderr piped to `res.write()` with `text/log` types; `done` event on close
- [x] `package.json` has `express` dependency — `"express": "^4.18.0"` confirmed
- [x] `dotnet build` still passes — `Build succeeded. 0 Warning(s) 0 Error(s)`
- [x] Commit message matches spec — `feat(fait-v2#2846): Fargate agent harness image with CC CLI HTTP shim`

---

## Known edge cases / things Clint should scrutinize

1. **`/workspace/{userId}` must exist at runtime** — `harness-server.js` passes `userWorkspaceDir` as `cwd` to `spawn()`. If EFS isn't mounted or the user directory doesn't exist, CC will fail to start. No guard exists currently — consider adding an `fs.existsSync()` check + mkdir before spawn.

2. **CC stdin pipe for long messages** — `ccProcess.stdin.write(briefContent)` followed by `.end()` is synchronous. Works fine for typical turn sizes; for very large systemPrompts this could theoretically block. Acceptable for v1.

3. **No request timeout on `/turn`** — If CC hangs, the SSE connection will stay open indefinitely. A `setTimeout` kill on the child process would be a good hardening step for a future sprint.

4. **`--dangerously-skip-permissions`** — Required for CC to run unattended inside Fargate. This is intentional and matches the spec. Clint should verify this is expected per the security model.

5. **No authentication on HTTP shim** — Port 3000 is not exposed externally (ALB routes only to the Blazor app; the harness is on the private subnet). Internal trust is acceptable for this design, but worth a note.

---

## How to test locally

```bash
# Build the image
cd ~/projects/fip/fait-v2/agent-harness
docker build -t fait-v2-harness .

# Run with a dummy workspace
mkdir -p /tmp/test-workspace/user123
docker run --rm -p 3000:3000 \
  -e WORKSPACE_DIR=/workspace \
  -v /tmp/test-workspace:/workspace \
  fait-v2-harness

# Health check
curl http://localhost:3000/health

# Session info
curl http://localhost:3000/session

# Turn dispatch (requires CC credentials in container env)
curl -X POST http://localhost:3000/turn \
  -H "Content-Type: application/json" \
  -d '{"userId":"user123","message":"What is 2+2?"}' \
  --no-buffer
```

> Note: CC credentials (`ANTHROPIC_API_KEY` or AWS Bedrock IAM role) must be available in the container for `/turn` to succeed.

---

## Build Cycle 2 — ADO#2846

**Commit:** `4723cba`
**Date:** 2026-05-06
**Cycle:** 2 (addressing Hawkeye review cycle 1 findings)

### Fixes Applied

| Fix | Severity | File | Change |
|-----|----------|------|--------|
| C1 | Critical | `Dockerfile` | Merged `unzip` into first `apt-get` RUN layer; removed broken `apt-get install -y unzip` from AWS CLI RUN (package lists already purged) |
| C2 | Critical | `harness-server.js` | Added `userId` path traversal validation — rejects `..`, `/`, `\`, non-alphanumeric; regex `/^[a-zA-Z0-9_-]{1,64}$/` |
| I3 | High | `harness-server.js` | Added `ended` flag + `endResponse()` helper to prevent double `res.end()` across `close`, `error`, and outer `catch` |
| I4 | High | `harness-server.js` | Added 5-minute SIGTERM timeout on CC process (`CC_TIMEOUT_MS` env override); `clearTimeout` on normal close/error |
| I5 | Medium | `harness-server.js` | Added `mkdirSync(userWorkspaceDir, { recursive: true })` guard before CC spawn |

### dotnet build

`Build succeeded. 0 Warning(s) 0 Error(s)` — no C# changes, sanity check only.

### CC sessions run

1 — `cat brief-c2-2846.md | claude --model sonnet --print --dangerously-skip-permissions`
