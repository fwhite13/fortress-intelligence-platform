# Review Report — ADO#2866 Cycle 2

**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-07  
**Commit:** `857b351`  
**File reviewed:** `agent-harness/harness-server.js`  
**Verdict:** ⚠️ **NEEDS-CHANGES**

---

## Summary

All three Cycle 1 issues are fixed correctly. One regression introduced: the process exit handler neutralizes the 30s timeout watchdog when a subprocess crash occurs mid-flight (after init, before tool response), leaving the Promise hanging indefinitely.

---

## Spec Compliance Check

**Cycle 1 issues from review brief:**
- C1 (MCP handshake sequence) — ✅ FIXED
- C2 (30s timeout watchdog) — ✅ FIXED
- C3 (SecretString guard) — ✅ FIXED

---

## C1 — MCP Handshake Sequence: ✅ PASS

The `invokeStitchTool` function is fully event-driven with correct ordering.

```js
// initialize sent after all listeners are attached (no race condition)
proc.stdin.write(JSON.stringify({ jsonrpc:'2.0', id:1, method:'initialize', ... }) + '\n');

// stdout handler drives the state machine
if (!initDone && msg.id === 1) {
    initDone = true;
    proc.stdin.write(/* notifications/initialized */ + '\n');  // notification — no response expected
    proc.stdin.write(/* tools/call id=2 */ + '\n');            // correct: immediately after notification
} else if (initDone && msg.id === toolCallId) {
    clearTimeout(timer);
    proc.kill();
    if (msg.error) reject(...); else resolve(msg.result);
}
```

- ✅ `initialize` sent after listeners registered — no race condition
- ✅ Event-driven wait for id=1 response before proceeding
- ✅ `notifications/initialized` + `tools/call` back-to-back is correct (notification has no response)
- ✅ Event-driven wait for id=2 response before resolving
- ✅ Message IDs correlated correctly (id=1, id=2)

---

## C2 — 30-Second Timeout Watchdog: ✅ PASS

```js
// Default parameter:
async function invokeStitchTool(toolName, args, timeoutMs = 30000) {

// Watchdog:
const timer = setTimeout(() => {
    proc.kill();
    reject(new Error(`stitch-mcp timeout after ${timeoutMs}ms`));
}, timeoutMs);

// Cleared on success:
clearTimeout(timer);
proc.kill();
```

- ✅ `timeoutMs = 30000` — exactly 30 seconds
- ✅ `proc.kill()` in timeout callback
- ✅ `reject(new Error(...))` in timeout callback
- ✅ `clearTimeout(timer)` on successful tool response

---

## C3 — SecretString Guard: ✅ PASS

```js
const secretValue = response.SecretString;
if (!secretValue) {
    console.warn('[harness] GCP secret is binary or empty — Stitch will be unavailable');
    return;
}
try {
    JSON.parse(secretValue);
} catch {
    console.warn('[harness] GCP secret is not valid JSON — Stitch will be unavailable');
    return;
}

// Write only reaches here if both guards passed
writeFileSync(credPath, secretValue, { mode: 0o600 });
```

- ✅ `if (!secretValue)` — null/empty guard → warns and returns
- ✅ `JSON.parse` inside try/catch → warns and returns on malformed JSON
- ✅ `writeFileSync` only executes after both guards pass
- ✅ No crash on either failure path

---

## Regression Found: R1 — Hung Promise on Post-Init Subprocess Crash

**Severity:** Important (not Critical — requires the subprocess to crash specifically between init ACK and tool response; rare in practice, but leaves an HTTP connection open forever when it does occur)

**File:** `agent-harness/harness-server.js`  
**Location:** `proc.on('exit', ...)` handler

**Issue:**

```js
proc.on('exit', (code) => {
    clearTimeout(timer);                                       // ← kills the 30s safety net
    if (!initDone) reject(new Error(`stitch-mcp exited...`)); // ← only guards pre-init exit
    // If initDone=true and toolDone=false → no reject, timer gone → Promise hangs forever
});
```

**Scenario:** `stitch-mcp` crashes after init (id=1 received, `initDone=true`) but before sending the `tools/call` response (id=2). The exit handler:
1. Calls `clearTimeout(timer)` — eliminates the 30s watchdog
2. Skips `reject()` because `initDone` is `true`
3. Promise never resolves or rejects — the `/tools/:toolName` HTTP route awaits forever

**Fix:**

```js
// Add toolDone flag alongside initDone:
let initDone = false;
let toolDone = false;

// Set it in the success/error branch (id=2 response):
toolDone = true;
clearTimeout(timer);
proc.kill();
if (msg.error) reject(...); else resolve(msg.result);

// Fix the exit handler:
proc.on('exit', (code) => {
    clearTimeout(timer);
    if (!initDone) {
        reject(new Error(`stitch-mcp exited ${code} before initialize response`));
    } else if (!toolDone) {
        reject(new Error(`stitch-mcp exited ${code} before tool response`));
    }
});
```

---

## Nitpicks (non-blocking)

- **N1:** `/tmp/gcp-service-account.json` (hardcoded path) — not a regression, but not configurable. Low priority.
- **N2:** `stitch-mcp` binary name hardcoded — same note.
- **N3:** Startup IIFE missing `.catch()` — low risk since `bootstrapGcpCredentials` internally swallows errors.

---

## What Tony Needs to Fix

**One change required before merge:**

In `invokeStitchTool`:
1. Add `let toolDone = false;` alongside the existing `let initDone = false;`
2. Set `toolDone = true;` in the id=2 success/error branch (before `clearTimeout`)
3. Update the `proc.on('exit', ...)` handler to also check `!toolDone` and reject if the subprocess exits before tool response is received

The fix is ~5 lines. No other changes needed.

---

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| C1: MCP handshake is event-driven and ordered correctly | ✅ PASS |
| C2: 30s timeout kills proc and rejects Promise | ✅ PASS |
| C3: SecretString null + JSON guard with early-return warn | ✅ PASS |
| No regressions introduced | ❌ R1 found — hung Promise on mid-flight crash |

---

_Clint Barton / Hawkeye — Code Reviewer_  
_ADO#2866 Cycle 2 — 2026-05-07_
