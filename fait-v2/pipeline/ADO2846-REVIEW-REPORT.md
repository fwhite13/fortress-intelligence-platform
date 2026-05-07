# Review Report — ADO#2846

**WI:** FAIT v2: Fargate agent harness image - OpenClaw runtime, CC CLI, MCP clients, HTTP shim
**Commit:** `0ff8b4f`
**Review Cycle:** 1 of 2
**Reviewer:** Hawkeye (Clint Barton)
**CC Model:** sonnet

---

### Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

Build report claims 5 files created: `Dockerfile`, `harness-server.js`, `package.json`, `.dockerignore`, `README.md`. All present. Endpoints match spec (`/health`, `/turn`, `/session`). CC CLI invocation matches spec (`--print --dangerously-skip-permissions`). SSE streaming present.

**Spec compliance:** ✅ STRUCTURALLY COMPLIANT — but two implementation bugs block PASS.

---

## CC Review Summary

CC ran adversarial review against all 4 files. Checklist hit rate: 22/23 PASS, 2 FAIL on self-flagged items, plus 2 CC-discovered bugs not on the original checklist. No false positives in CC findings — I confirmed all 4 issues independently.

**CC command:** `cat review-2846-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Consistency Audit

Single-component deliverable — no cross-file consistency issues beyond Dockerfile→harness-server.js contract (env vars, port, paths). All consistent.

`FAIT_USER_ID` / `FAIT_SESSION_ID` env var names in `/session` endpoint: reasonable but not cross-checked against actual Fargate task definition. Flag for Maria to verify against task def before deployment.

---

## Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| **Critical** | `Dockerfile` | Line 12 | `apt-get install -y unzip` called without `apt-get update` — package lists were purged at the end of the first RUN block. Build will fail with `E: Unable to locate package unzip`. | Move `unzip` into the first `RUN` block alongside `curl`, `git`, `ca-certificates`. |
| **Critical** | `harness-server.js` | Line 24 | `userId` from `req.body` interpolated directly into filesystem path with no sanitization. `{"userId":"../../etc","message":"x"}` → CC spawned with `cwd: /etc` and `--dangerously-skip-permissions`. Path traversal out of user workspace. | Validate `userId` rejects `/`, `..`, null bytes before line 24. Regex: `/^[a-zA-Z0-9_-]+$/`. |
| **High** | `harness-server.js` | Lines 64–72 | Double `res.end()` when process errors: Node.js fires both `error` AND `close` events on a spawn error. Both handlers call `res.end()`. Second call throws `ERR_HTTP_HEADERS_SENT` / write-after-end, potentially crashing the handler. | Add `let streamEnded = false` flag; guard both `res.write()` and `res.end()` calls in both handlers. |
| **High** | `harness-server.js` | — | No timeout on `/turn`. Hung CC process = SSE connection open forever. Fargate task stays alive burning hours. | `setTimeout(() => { ccProcess.kill('SIGTERM'); res.write(...error event...); res.end(); }, process.env.CC_TIMEOUT_MS || 300000)` — clear on `close`. |
| **Medium** | `harness-server.js` | Line 24 | No `userWorkspaceDir` existence check before `spawn()`. If EFS not mounted or user dir missing, `cwd` ENOENT produces HTTP 200 + SSE error — misleading to caller. | `fs.mkdirSync(userWorkspaceDir, { recursive: true })` before spawn, or check + return 503 before SSE headers are set. |

---

## Nitpicks

- **N1:** `express.json({ limit: '10mb' })` — for an orchestration-internal shim, 1mb is more appropriate.
- **N2:** No `--no-install-recommends` on apt-get calls — minor image bloat, not functional.
- **N3:** `FAIT_USER_ID`/`FAIT_SESSION_ID` env var names in `/session` endpoint not validated against Fargate task definition. Silently returns `null` if names differ. Low risk but worth a cross-check before first deploy.

---

## Positive Observations

- Dockerfile layer ordering is correct — `harness-server.js` copied last so code edits don't bust npm install cache.
- `CLAUDE_CODE_ENTRYPOINT: 'fargate-harness'` and `CLAUDE_CODE_DISABLE_AUTO_MEMORY: '1'` correctly set in spawn env.
- `--dangerously-skip-permissions` is intentional and appropriate for per-user isolated Fargate containers. Security model is sound.
- SSE done/error event structure is correct.
- stdin write + end ordering is correct (no race condition).
- HEALTHCHECK, EXPOSE, CMD all correct.
- `.dockerignore` excludes `node_modules` — correct.

---

## Acceptance Criteria Verification

- [x] `agent-harness/` directory created with all files — ✅ verified
- [x] `Dockerfile` installs node:20, AWS CLI, Claude Code CLI — ✅ correct base + installer script; ❌ **build-breaking unzip bug blocks this**
- [x] `/health`, `/turn`, `/session` endpoints — ✅ all present
- [x] `/turn` streams CC CLI output via SSE — ✅ stdout/stderr piped; done/error events present
- [x] `package.json` has `express` — ✅ `^4.18.0`
- [x] Commit message matches spec — ✅ per build report

---

## What Tony Needs to Fix

**Required before PASS:**

**1. Dockerfile — move `unzip` to first RUN block**
```diff
 RUN apt-get update && apt-get install -y \
     curl \
     git \
     ca-certificates \
+    unzip \
     && rm -rf /var/lib/apt/lists/*

 # Install AWS CLI v2 (for S3 workspace access)
-RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o /tmp/awscli.zip \
-    && apt-get install -y unzip \
-    && unzip /tmp/awscli.zip -d /tmp \
+RUN curl "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip" -o /tmp/awscli.zip \
+    && unzip /tmp/awscli.zip -d /tmp \
     && /tmp/aws/install \
     && rm -rf /tmp/awscli.zip /tmp/aws
```

**2. harness-server.js — sanitize `userId` before path construction**
```js
// After: const { sessionId, userId, message, systemPrompt } = req.body;
if (!userId || !message) {
    return res.status(400).json({ error: 'userId and message required' });
}

// ADD THIS:
if (!/^[a-zA-Z0-9_-]+$/.test(userId)) {
    return res.status(400).json({ error: 'userId contains invalid characters' });
}
```

**3. harness-server.js — guard against double res.end()**
```js
let streamEnded = false;

ccProcess.on('close', (code) => {
    if (streamEnded) return;
    streamEnded = true;
    res.write(`data: ${JSON.stringify({ type: 'done', exitCode: code })}\n\n`);
    res.end();
});

ccProcess.on('error', (err) => {
    if (streamEnded) return;
    streamEnded = true;
    res.write(`data: ${JSON.stringify({ type: 'error', message: err.message })}\n\n`);
    res.end();
});
```

**4. harness-server.js — add CC process timeout**
```js
const CC_TIMEOUT_MS = parseInt(process.env.CC_TIMEOUT_MS || '300000', 10); // 5 min default

const timeoutId = setTimeout(() => {
    if (streamEnded) return;
    streamEnded = true;
    ccProcess.kill('SIGTERM');
    res.write(`data: ${JSON.stringify({ type: 'error', message: 'CC process timeout' })}\n\n`);
    res.end();
}, CC_TIMEOUT_MS);

ccProcess.on('close', (code) => {
    clearTimeout(timeoutId);
    if (streamEnded) return;
    streamEnded = true;
    res.write(`data: ${JSON.stringify({ type: 'done', exitCode: code })}\n\n`);
    res.end();
});
```

**5. harness-server.js — workspace dir guard (medium, should fix)**
```js
// Before spawn():
const fs = require('fs');
fs.mkdirSync(userWorkspaceDir, { recursive: true });
```
(Or check + return 503 before SSE headers are set, which gives the caller a proper HTTP error code.)

---

_Hawkeye — review cycle 1 complete. 2 critical, 2 high, 1 medium. NEEDS-CHANGES. Fix and resubmit for cycle 2._
