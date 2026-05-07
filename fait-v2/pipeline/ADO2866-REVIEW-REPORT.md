# Review Report: ADO#2866 — Stitch MCP Integration into FAIT v2 Fargate Harness

**Reviewer:** Hawkeye (Clint Barton)
**Date:** 2026-05-07
**Commit:** `0f90656`
**Cycle:** 1
**Verdict:** ⚠️ NEEDS-CHANGES

---

## Spec Compliance Check

**Spec:** `memory/projects/fait-v2-spec-2026-04-27.md` §6.3 (Design Agent / Stitch MCP)

**§2 Codebase Map:**
- `agent-harness/package.json` — ✅ `stitch-mcp: latest` + `@aws-sdk/client-secrets-manager: ^3.0.0` added
- `agent-harness/Dockerfile` — ✅ `RUN npm install -g stitch-mcp` added after npm install
- `agent-harness/harness-server.js` — ✅ GCP bootstrap, `STITCH_TOOLS` set, `invokeStitchTool()`, health endpoint, tool dispatch added
- `src/FortressAI.V2.Web/Services/IDesignAgentService.cs` — ✅ `IsStitchAvailableAsync(string userId, CancellationToken ct = default)` updated
- `src/FortressAI.V2.Web/Services/DesignAgentService.cs` — ✅ `IHttpClientFactory` injected; live health check implemented; 3 callers updated
- `src/FortressAI.V2.Web/Components/Agent/DesignAgentView.razor` — ✅ `_userId` passed to `IsStitchAvailableAsync`
- `CLAUDE.md` — ✅ created at repo root

**§6 Out of Scope:** ✅ No out-of-scope changes detected.

**§7 Acceptance Criteria:**
- [x] `stitch-mcp` installed in agent harness (package.json + Dockerfile) — ✅ verified
- [x] GCP credentials bootstrapped from `fait-v2/gcp-stitch-service-account` at startup — ✅ verified
- [x] `GOOGLE_APPLICATION_CREDENTIALS` set before harness listens — ✅ async IIFE pattern confirmed
- [x] Stitch tools callable via `POST /tools/{toolName}` — ✅ 7-tool STITCH_TOOLS set + dispatch
- [x] `/tools/stitch/health` returns `{ available: bool, reason }` — ✅ verified
- [x] `IsStitchAvailableAsync` calls harness health endpoint — ✅ verified
- [x] Graceful degradation — bootstrap failure logs warning, health returns `available: false` — ✅ catch swallows, env var not set, health returns false correctly
- [x] `dotnet build` 0 errors 0 warnings — ✅ per build report
- [ ] `invokeStitchTool` correctly implements MCP JSON-RPC 2024-11-05 protocol — ❌ **FAILS** — see C1 below
- [ ] Subprocess timeout — ❌ **MISSING** — see C2 below

**Spec compliance verdict:** ❌ NON-COMPLIANT on two ACs — C1 and C2 block PASS.

---

## CC Review Summary

Used Claude Code (Sonnet) with an adversarial brief targeting all 4 review focus areas. CC ran against the full harness-server.js, DesignAgentService.cs, IDesignAgentService.cs, and DesignAgentView.razor content.

CC surfaced 2 Critical and 2 Important findings. I confirmed all 4 as real — no false positives. CSS variable concern from CC was flagged as "unverifiable from payload"; I verified directly with grep — result is below.

---

## Consistency Audit

**Files Cross-Referenced:**

| Check | Result |
|---|---|
| `STITCH_TOOLS` set (7 tools) ↔ CLAUDE.md tool list (7 tools) | ✅ exact match |
| `STITCH_TOOLS` tool names ↔ brief AC tool names | ✅ exact match |
| `/tools/stitch/health` endpoint URL ↔ `IsStitchAvailableAsync` URL | ✅ match: `http://{ip}:{port}/tools/stitch/health` |
| `{ available: bool }` JSON field name ↔ C# `TryGetProperty("available")` | ✅ exact match |
| `IDesignAgentService.IsStitchAvailableAsync(string userId, CancellationToken ct)` ↔ `DesignAgentService` impl ↔ Razor call | ✅ consistent |
| `IHttpClientFactory` "HarnessClient" named client ↔ `Program.cs` registration | ✅ `AddHttpClient("HarnessClient")` confirmed at line 90 |
| S3 artifact storage in `DesignAgentService` ↔ spec §14.2 (AWS-First) | ✅ correct — spec §14.2 explicitly uses S3 for v1 Fargate build |
| MudBlazor icon `font-size: Npx !important` overrides in Razor ↔ existing component pattern | ✅ established pattern (see `AgentPluginBadge.razor`) — not a CSS violation |

**Undocumented Dependencies:** `Program.cs` `AddHttpClient("HarnessClient")` at line 90 — pre-existing registration, not modified in this WI. Correct.

---

## Critical Issues — 2

### C1: MCP Protocol Handshake Violation — Every Tool Call Will Fail
- **File:** `agent-harness/harness-server.js` — `invokeStitchTool()`
- **Category:** correctness / protocol
- **Issue:** The current implementation sends `initialize` and `tools/call` back-to-back without (a) waiting for the `initialize` response and (b) sending the required `notifications/initialized` notification. The MCP 2024-11-05 spec requires this sequence:
  1. Client → Server: `initialize` request (id=1)
  2. Server → Client: `initialize` response (id=1)
  3. Client → Server: `notifications/initialized` notification (no id field)
  4. Client → Server: `tools/call` request (id=2)
  5. Server → Client: `tools/call` response (id=2)

  The current code skips steps 2 and 3, writing both messages then immediately closing stdin. Any MCP-compliant server will either reject `tools/call` as out-of-order or queue it and never respond because stdin is closed before the handshake completes. This is a protocol violation that will cause every Stitch tool call to fail with `"stitch-mcp exited N with no result"`.

- **Evidence:**
  ```javascript
  // Current — writes both messages then closes stdin immediately
  proc.stdin.write(initMsg);
  proc.stdin.write(callMsg);
  proc.stdin.end();
  ```

- **Impact:** 100% of Stitch tool calls fail. The error surfaces at the `/tools/:toolName` endpoint as HTTP 500 (`"stitch-mcp exited N with no result"`). The fallback path triggers every time.

- **Fix:** Implement a proper two-phase stdio exchange:
  ```javascript
  function invokeStitchTool(toolName, args) {
      return new Promise((resolve, reject) => {
          const proc = spawn('stitch-mcp', [], {
              env: { ...process.env },
              stdio: ['pipe', 'pipe', 'pipe']
          });

          const initMsg = JSON.stringify({
              jsonrpc: '2.0', id: 1,
              method: 'initialize',
              params: {
                  protocolVersion: '2024-11-05',
                  capabilities: {},
                  clientInfo: { name: 'fait-v2-harness', version: '1.0' }
              }
          }) + '\n';

          const initializedNotif = JSON.stringify({
              jsonrpc: '2.0',
              method: 'notifications/initialized',
              params: {}
          }) + '\n';

          const callMsg = JSON.stringify({
              jsonrpc: '2.0', id: 2,
              method: 'tools/call',
              params: { name: toolName, arguments: args || {} }
          }) + '\n';

          let stdout = '';
          let stderr = '';
          let initResponseReceived = false;

          const timeout = setTimeout(() => {
              proc.kill();
              reject(new Error('stitch-mcp timed out after 30s'));
          }, 30_000);

          proc.stdout.on('data', (chunk) => {
              stdout += chunk.toString();
              const lines = stdout.split('\n');
              for (const line of lines.slice(0, -1)) {
                  if (!line.trim()) continue;
                  try {
                      const msg = JSON.parse(line);
                      if (!initResponseReceived && msg.id === 1) {
                          initResponseReceived = true;
                          // Send initialized notification then the tool call
                          proc.stdin.write(initializedNotif);
                          proc.stdin.write(callMsg);
                          proc.stdin.end();
                      } else if (msg.id === 2) {
                          clearTimeout(timeout);
                          proc.kill();
                          if (msg.error) {
                              return reject(new Error(msg.error.message || JSON.stringify(msg.error)));
                          }
                          return resolve(msg.result);
                      }
                  } catch (_) { /* skip non-JSON lines */ }
              }
              stdout = lines[lines.length - 1]; // keep partial last line
          });

          proc.stderr.on('data', (chunk) => { stderr += chunk.toString(); });

          proc.on('close', (code) => {
              clearTimeout(timeout);
              reject(new Error(`stitch-mcp exited ${code} with no result. stderr: ${stderr.slice(0, 200)}`));
          });

          proc.on('error', (err) => {
              clearTimeout(timeout);
              reject(err);
          });

          // Send only initialize first — tools/call goes AFTER initialize response
          proc.stdin.write(initMsg);
      });
  }
  ```

---

### C2: No Subprocess Timeout — Hung Request Risk
- **File:** `agent-harness/harness-server.js` — `invokeStitchTool()`
- **Category:** correctness / resource management
- **Issue:** `invokeStitchTool()` resolves only on `proc.on('close')`. There is no `setTimeout` watchdog. If `stitch-mcp` hangs (GCP API stall, network timeout, process blocks), the Promise never resolves. The `/tools/:toolName` handler has no timeout. The result is a hung HTTP request that holds a connection and a live subprocess indefinitely.

- **Impact:** A single stalled GCP API call can lock up a harness connection. In Fargate, with a finite ALB connection pool, enough hangs can make the harness unresponsive. This is a resource leak and denial-of-service vector.

- **Fix:** Add a 30-second timeout (incorporated into the C1 fix above). Minimum acceptable timeout: 30 seconds. Maximum: 60 seconds (Stitch can be slow per Tony's note).

---

## Important Issues — 1

### I1: `response.SecretString` Not Guarded Against Undefined
- **File:** `agent-harness/harness-server.js` — `bootstrapGcpCredentials()`
- **Category:** correctness
- **Issue:** If the Secrets Manager secret is stored as **binary** (not string), `response.SecretString` is `undefined`. `writeFileSync(credPath, undefined, { mode: 0o600 })` writes the literal string `"undefined"` to `/tmp/gcp-service-account.json`. The file exists → `GOOGLE_APPLICATION_CREDENTIALS` is set → health endpoint returns `available: true` — but `stitch-mcp` fails with a cryptic JSON parse error on every invocation.

  This is the worst failure mode: health says OK, every tool call fails, no obvious error trail.

- **Evidence:**
  ```javascript
  writeFileSync(credPath, response.SecretString, { mode: 0o600 });
  // response.SecretString is undefined for binary secrets
  ```

- **Fix:**
  ```javascript
  if (!response.SecretString) {
      throw new Error('Secret is stored as binary (not string) — cannot use as GCP service account credentials. Store as plaintext JSON in Secrets Manager.');
  }
  writeFileSync(credPath, response.SecretString, { mode: 0o600 });
  ```
  This causes the `catch` to log a meaningful warning and leaves `GOOGLE_APPLICATION_CREDENTIALS` unset, so health correctly returns `available: false`.

---

## Nitpicks — 1

### N1: `writeFileSync` mode not applied to pre-existing files
- **File:** `agent-harness/harness-server.js` — `bootstrapGcpCredentials()`
- `{ mode: 0o600 }` in `writeFileSync` only applies the mode when creating the file, not when overwriting an existing one. In Fargate, `/tmp` is always fresh per container, so this is production-safe. It's a local dev / container reuse latent bug. Not blocking.
- Optional fix: `chmodSync(credPath, 0o600)` after the write.

---

## Positive Observations

- Credential bootstrap timing is correct — `await bootstrapGcpCredentials()` in the async IIFE runs before `app.listen()`. No race condition.
- `env: { ...process.env }` in `spawn` correctly propagates `GOOGLE_APPLICATION_CREDENTIALS` to the subprocess after bootstrap.
- `{ mode: 0o600 }` syntax is valid for `writeFileSync` — correct file permission intent.
- Health endpoint correctly reflects `false` when bootstrap fails (env var not set → `existsSync` returns false). Logic is sound.
- `IHttpClientFactory.CreateClient("HarnessClient")` usage is correct — `client.Timeout = TimeSpan.FromSeconds(5)` on the returned `HttpClient` is valid.
- `OperationCanceledException`, `HttpRequestException`, `InvalidOperationException` from `GetBoolean()` — all caught by the blanket `catch (Exception ex)` → returns `false`. Graceful degradation is solid.
- CSS variable compliance in `DesignAgentView.razor` — **confirmed clean**. No hardcoded `#hex`, `rgb()`, or `rgba()` values. All spacing/layout uses `var(--space-*)`, all colors use `var(--color-*)`. The `font-size: Npx !important` patterns on MudIcon overrides are an established codebase convention (matches `AgentPluginBadge.razor`), not a CSS violation.
- `Program.cs` already has `AddHttpClient("HarnessClient")` at line 90 — no missing DI registration.
- S3 artifact storage in `DesignAgentService.SaveArtifactAsync` is correct for the v1 Fargate build (spec §14.2 AWS-First addendum explicitly uses S3).
- `STITCH_TOOLS` set and CLAUDE.md tool list are in exact agreement — 7 tools, identical names.
- CLAUDE.md is well-structured and accurately documents the integration.

---

## What to Fix (NEEDS-CHANGES)

Tony, two things to fix. Both are in `agent-harness/harness-server.js`.

### Fix 1 (C1 + C2 together): Replace `invokeStitchTool` with proper MCP handshake + timeout

The current implementation has two problems that should be fixed in one pass: (1) the MCP protocol handshake is incomplete — you must wait for the `initialize` response before sending `notifications/initialized` and then `tools/call`; and (2) there's no subprocess timeout. Both are fixed in the code block in C1 above — use that as the replacement.

The key changes:
- Send only `initMsg` initially
- In the `stdout` data handler, parse incoming lines as they arrive (don't wait for close)
- When you see `id=1` response (initialize ACK), send `notifications/initialized` notification then `callMsg`
- When you see `id=2` response (tool call result), resolve/reject and kill the process
- Add `setTimeout` watchdog (30 seconds) that kills and rejects

### Fix 2 (I1): Guard `response.SecretString` in `bootstrapGcpCredentials`

Add the null/undefined check before `writeFileSync` — see I1 fix above. Three lines. Prevents the health-says-ok-but-all-tools-fail silent failure mode.

### Fix 3 (N1, optional): Add `chmodSync` after writeFileSync

Not required for production (Fargate always has fresh `/tmp`), but good hygiene. Up to you.

---

## Acceptance Criteria Verification

| AC | Status |
|----|--------|
| `stitch-mcp` package installed (package.json + Dockerfile) | ✅ Verified |
| GCP credentials bootstrapped from `fait-v2/gcp-stitch-service-account` | ✅ Verified |
| `GOOGLE_APPLICATION_CREDENTIALS` set before harness listens | ✅ Verified |
| `generate_screen_from_text` callable via `POST /tools/generate_screen_from_text` | ✅ Routing verified — but will fail until C1 fixed |
| `extract_design_context` callable via `POST /tools/extract_design_context` | ✅ Routing verified — but will fail until C1 fixed |
| All 7 Stitch tools routed | ✅ Verified |
| `/tools/stitch/health` returns `{ available: bool, reason }` | ✅ Verified |
| `IsStitchAvailableAsync` calls harness health endpoint | ✅ Verified |
| Graceful degradation — bootstrap failure → `available: false` | ✅ Verified |
| Stitch MCP documented in `CLAUDE.md` | ✅ Verified |
| `dotnet build` 0 errors 0 warnings | ✅ Per build report |
| MCP JSON-RPC protocol correct | ❌ C1 blocks — handshake incomplete |
| Subprocess timeout present | ❌ C2 blocks — no timeout |

---

_You see what others miss. Your CC specs are adversarial by design. Every review makes the codebase stronger._
