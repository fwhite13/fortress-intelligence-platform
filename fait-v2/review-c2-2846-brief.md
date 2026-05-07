# Adversarial Code Review — ADO#2846 Cycle 2

## Context

This is REVIEW CYCLE 2 — verifying 5 specific fixes from cycle 1 NEEDS-CHANGES verdict.
Files: `agent-harness/Dockerfile`, `agent-harness/harness-server.js`

Cycle 1 commit: `0ff8b4f`
Cycle 2 commit: `4723cba`

## Your Job

Verify all 5 fixes are correct and complete. Check for regressions. Be adversarial.

Read these files:
- `agent-harness/Dockerfile`
- `agent-harness/harness-server.js`

---

## Fix Verification Checklist

### C1: Dockerfile unzip placement
**Required:** `unzip` must appear in the FIRST `RUN` block alongside curl/git/ca-certificates. The AWS CLI `RUN` block must NOT have a separate `apt-get install unzip` (which would fail because apt lists were purged).

Verify:
1. First RUN block includes `unzip`
2. AWS CLI RUN block has no `apt-get install` call at all
3. AWS CLI RUN still calls `unzip` (the binary) — just now it's already installed

### C2: userId validation
**Required:** Before `userWorkspaceDir` path construction, `userId` must be validated with regex `^[a-zA-Z0-9_-]{1,64}$`. Must return HTTP 400 on invalid. Must guard against: `..`, `/`, `\`, null bytes, empty string, non-string.

Verify:
1. Regex test present and correct
2. Applied BEFORE path construction
3. Returns 400 on failure
4. Check: does `{"userId":"../../etc","message":"x"}` get rejected?
5. Check: does `{"userId":"a".repeat(65),"message":"x"}` get rejected (length limit)?
6. Check: does `{"userId":"valid-user_1","message":"x"}` pass?

### I3: ended flag + endResponse helper
**Required:** `let ended = false` + `endResponse()` helper that guards with `if (ended) return`. All exit paths (close, error, outer catch, timeout) must use `endResponse()` — no bare `res.end()` calls.

Verify:
1. `ended` flag declared before spawn
2. `endResponse` helper sets `ended = true` before writing
3. `ccProcess.on('close')` uses `endResponse`, not bare `res.end()`
4. `ccProcess.on('error')` uses `endResponse`, not bare `res.end()`
5. Outer `catch (err)` uses `endResponse`
6. NO bare `res.end()` calls anywhere in the `/turn` handler
7. Double-end is now impossible regardless of event ordering

### I4: 5-minute SIGTERM timeout
**Required:** `setTimeout` with 5-minute default (300000ms). `clearTimeout` called in the `close` handler on clean exit. Timeout callback calls `ccProcess.kill('SIGTERM')` and uses `endResponse`.

Verify:
1. `setTimeout` present with `300000` default
2. Uses `process.env.CC_TIMEOUT_MS` with parseInt (not raw string)
3. Timeout callback kills process with SIGTERM
4. Timeout callback calls `endResponse` (uses the `ended` guard — no double-end if close fires first)
5. `clearTimeout(timeout)` called in `close` handler
6. `clearTimeout(timeout)` also called in `error` handler (otherwise timer leaks on spawn error)

### I5: mkdirSync before spawn
**Required:** `mkdirSync(userWorkspaceDir, { recursive: true })` called before `spawn()`. Must handle error gracefully (not crash handler).

Verify:
1. `mkdirSync` call present before spawn
2. `{ recursive: true }` option present
3. Wrapped in try/catch — mkdir error handled gracefully
4. Called AFTER validation (can't path-traverse to create arbitrary dirs)

---

## Regression Check

Check that nothing from cycle 1's PASS items was broken:
- HEALTHCHECK still present in Dockerfile
- Layer ordering still correct (harness-server.js copied last)
- CLAUDE_CODE_ENTRYPOINT and CLAUDE_CODE_DISABLE_AUTO_MEMORY still in spawn env
- stdin write + end still correct
- SSE headers still set before any streaming
- /health and /session endpoints still present and correct

---

## Security re-check of userId validation

The original vulnerability: `{"userId":"../../etc","message":"x"}` would set `cwd: /workspace/../../etc` = `/etc`, then spawn CC with `--dangerously-skip-permissions` in `/etc`.

After the fix, trace the exact path:
1. `userId = "../../etc"`
2. `typeof userId !== 'string'` → false (it is a string)
3. `userId.includes('..')` → TRUE → returns 400

Also check: what if userId is `null` from JSON? `typeof null !== 'string'` → TRUE → 400. Good.
What if userId is an object from JSON injection? Same typeof check → 400. Good.

---

## Verdict Criteria

**PASS:** All 5 fixes correctly implemented. No regressions. No new issues.
**NEEDS-CHANGES:** Any fix incomplete, incorrect, or any regression found.

Be specific. If a fix is 90% right but has a gap (e.g., clearTimeout missing from error handler), call it out.
