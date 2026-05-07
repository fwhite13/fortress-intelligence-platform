# BUILD BRIEF — ADO#2866 — Stitch MCP Integration (Cycle 2 — Review Fixes)
**Sprint 3, Lane 2 | FAIT v2 Epic #2835**
**Agent:** Tony Stark | **Cycle:** 2 | **Date:** 2026-05-07

---

## Context

Cycle 2 — fix 3 issues from Clint's cycle 1 review. Do NOT change anything else.

**Repo:** `~/projects/fip/fait-v2/` | **Branch:** `main` | **Current HEAD:** `0f90656`
**File to fix:** `agent-harness/harness-server.js`

---

## Fix 1 (CRITICAL) — MCP Protocol Handshake

**Problem:** `invokeStitchTool` sends `initialize` + `tools/call` back-to-back and closes stdin immediately. MCP 2024-11-05 requires:
1. Send `initialize` request
2. Wait for `initialize` response (id=1)
3. Send `notifications/initialized` notification
4. Send `tools/call` request
5. Wait for `tools/call` response

Skipping steps 2 and 3 means every Stitch tool call fails with `"stitch-mcp exited N with no result"`.

**Fix:** Rewrite `invokeStitchTool` to properly sequence the MCP handshake — wait for the initialize response before sending initialized notification, then send the tool call.

Correct pattern:
```javascript
async function invokeStitchTool(toolName, args, timeoutMs = 30000) {
    return new Promise((resolve, reject) => {
        const proc = spawn('stitch-mcp', [], { env: process.env });
        let buffer = '';
        let initDone = false;
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
                    if (msg.error) reject(new Error(msg.error.message || JSON.stringify(msg.error)));
                    else resolve(msg.result);
                }
            }
        });

        proc.stderr.on('data', (d) => console.error('[stitch-mcp stderr]', d.toString()));
        proc.on('exit', (code) => {
            clearTimeout(timer);
            if (!initDone) reject(new Error(`stitch-mcp exited ${code} before initialize response`));
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
```

---

## Fix 2 (CRITICAL) — Subprocess Timeout

Already included in Fix 1 above — the `setTimeout` watchdog with `timeoutMs = 30000` is part of the rewrite. Make sure the 30-second timeout applies and the proc is killed on timeout.

---

## Fix 3 (IMPORTANT) — Guard `response.SecretString`

**Problem:** If the Secrets Manager secret is stored as binary, `response.SecretString` is `undefined`. Writing `"undefined"` to the credentials file means the health endpoint returns `available: true` but every tool call fails silently with a JSON parse error.

**Fix:** In `bootstrapGcpCredentials()`, validate that `SecretString` is present and is valid JSON before writing:

```javascript
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
        fs.writeFileSync(credPath, secretValue, { mode: 0o600 });
        process.env.GOOGLE_APPLICATION_CREDENTIALS = credPath;
        console.log('[harness] GCP credentials bootstrapped');
    } catch (err) {
        console.warn('[harness] GCP credentials not available — Stitch will be unavailable:', err.message);
    }
}
```

---

## Mandatory Rules

- **CC CLI MANDATORY:**
  ```bash
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  cat brief-c2-2866.md | claude --model sonnet --print --dangerously-skip-permissions
  ```
- Work dir: `~/projects/fip/fait-v2/`
- Only change `agent-harness/harness-server.js` — no scope creep
- Commit: `fix(fait-v2#2866): MCP handshake sequence, subprocess timeout, SecretString guard`
- Run `dotnet build` to confirm no Blazor regressions (0 errors, 0 warnings)

---

## ADO Comment (MANDATORY)
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2866,"text":"**[Tony Stark — BUILD cycle 2]**\nCommit {hash}: MCP handshake fix, 30s timeout, SecretString guard. Build: SUCCEEDED."}'
```

---

## Deliverables
1. Cycle 2 section appended to `~/projects/fip/fait-v2/pipeline/ADO2866-BUILD-REPORT.md`
2. Commit pushed to `origin/main`
3. ADO comment on #2866
4. Report back to Maria
