# Review Report — ADO#2866 Cycle 3

**Reviewer:** Clint Barton (Hawkeye)
**Date:** 2026-05-07
**Repo:** `fait-v2` | **Branch:** `main` | **Commit:** `f8a8c00`
**File:** `agent-harness/harness-server.js`

---

### Verdict: ✅ PASS

---

## Fix Summary

Tony added a `toolDone` guard to the `proc.on('exit', ...)` handler in `invokeStitchTool` to plug the hung-Promise gap when `stitch-mcp` crashes after the initialize ACK but before the tool response arrives.

---

## Git Diff Verified

```diff
+        let toolDone = false;
 
         ...
 
+                    toolDone = true;
                     if (msg.error) reject(...)
                     else resolve(msg.result);
 
         proc.on('exit', (code) => {
             clearTimeout(timer);
             if (!initDone) reject(new Error(`stitch-mcp exited ${code} before initialize response`));
+            else if (!toolDone) reject(new Error(`stitch-mcp exited ${code} before tool response`));
         });
```

Exactly **three lines added**. Nothing else changed in the function or file.

---

## Criterion Verification

| # | Criterion | Result |
|---|-----------|--------|
| 1 | `let toolDone = false;` declared alongside `initDone` | ✅ PASS |
| 2 | `toolDone = true;` set in id=2 branch before resolve/reject | ✅ PASS |
| 3 | Exit handler: `if (!initDone)` → `else if (!toolDone)` chain | ✅ PASS |
| 4 | No regressions — exactly the described fix, nothing else | ✅ PASS |

---

## Adversarial Analysis (CC-verified)

**Race condition — `proc.kill()` before `toolDone = true`?**
Safe. Node.js is single-threaded. `proc.kill()` sends a signal but the `exit` event is queued asynchronously. The current synchronous frame (`toolDone = true` → `reject`/`resolve`) completes before the exit handler can run. `toolDone` is always `true` before the exit handler fires.

**`else if` vs `if` — double-reject risk?**
`else if` is correct. There is no code path where `toolDone = true` without `initDone = true` (the branch on line 93 gates on `initDone &&`). Using plain `if` would cause double-reject on post-init crashes. The implementation is correct.

**Can `toolDone = true` with `initDone = false`?**
No. The `toolDone = true` assignment only executes inside the `initDone && msg.id === toolCallId` branch. No logic error possible.

---

## State Coverage

| Scenario | initDone | toolDone | Exit behavior |
|----------|----------|----------|---------------|
| Crash before init ACK | false | false | `if (!initDone)` → reject ✅ |
| Crash after init, before tool response | true | false | `else if (!toolDone)` → reject ✅ |
| Clean kill after tool response received | true | true | Silent — Promise already settled ✅ |

All three states covered. No hung Promise possible.

---

## Cycle History

| Cycle | Verdict | Summary |
|-------|---------|---------|
| C1 | NEEDS-CHANGES | MCP handshake, subprocess timeout, SecretString guard |
| C2 | NEEDS-CHANGES | `toolDone` guard missing from exit handler |
| **C3** | **PASS** | `toolDone` guard correctly implemented |

---

_Hawkeye — You see what others miss._
