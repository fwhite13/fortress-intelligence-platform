# Review Report — ADO#3288 + ADO#3289

**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-11  
**HEAD:** `6f612ed3`  
**Review cycle:** 1 of 2  
**Scope:** P0 logging additions — structured auth logging + CC spawn diagnostics

---

## Verdict: ✅ PASS

---

## CC Review Invocation

```bash
cat /tmp/clint-brief-3288-3289.md | claude --model sonnet --print --dangerously-skip-permissions
```

CC executed a 20-point checklist against the full diff and both changed files. All 20 checks passed. Two nitpicks noted, neither blocking.

---

## Spec Compliance Check

No formal developer brief was issued for these P0 fixes. The task brief (subagent context) specified the verification criteria directly. All criteria verified.

**Files changed:**
- `fait/src/FortressAI.Web/Program.cs` — ✅ commit `81f02cb1` (ADO#3288)
- `fait-v2/agent-harness/harness-server.js` — ✅ commits `81f02cb1` + `6f612ed3` (ADO#3288 + ADO#3289)

**Out-of-scope changes:** ✅ None detected. One file per commit, both in scope.

---

## Consistency Audit

**Files cross-referenced:**
- `Program.cs` endpoint logging ↔ `harness-server.js` caller logging — ✅ consistent; both log userId, both use appropriate masking/safe logging
- Token masking in Blazor (`[..8] + "..."`) ↔ `maskedIncoming` name in log call — ✅ masked value is what's passed, not raw token
- `getUserTokens` return shape (`{ ms365, ado }`) — ✅ unchanged by the diff

**Undocumented dependencies:** None found.

---

## Issues Found

| Severity | File | Location | Issue |
|----------|------|----------|-------|
| Nitpick | `harness-server.js` | startup IIFE ~L2556 | `execSync('claude --version', { timeout: 10000 })` blocks event loop up to 10s |
| Nitpick | `harness-server.js` | L55 (parse failure log) | `body=${responseBody}` on parse-fail path logs full response body — theoretical token exposure if server ever returns malformed 200 |

Neither nitpick is blocking. Both are server-side-only logs. No client exposure.

---

## Critical Issues

**0 Critical issues.**

---

## ADO#3288 — Detailed Findings

### Program.cs — `/api/internal/user-tokens/{userId}`

| Check | Result |
|-------|--------|
| Token masked before logging — first 8 chars + `...` only | ✅ `incomingToken[..8] + "..."` — confirmed `[..8]` not `[..80]` |
| Full token never in structured log properties | ✅ All 5 log calls pass only `userId` or `maskedIncoming` |
| `LogInformation`/`LogWarning` used (no `Console.WriteLine`) | ✅ Correct log levels throughout |
| Logging on FAIL paths (503, 401, 400) | ✅ All three failure paths logged |
| Logging on PASS path (200) | ✅ Two `LogInformation` calls on success |
| Token comparison semantics unchanged from old code | ✅ Semantically identical — `TryGetValue` absent case → empty string, same 401 result |

### harness-server.js — `getUserTokens()`

| Check | Result |
|-------|--------|
| `res.text()` called BEFORE `res.ok` check | ✅ L47-48: body consumed, then L49: `if (!res.ok)` |
| Non-ok path logs status, userId, body | ✅ L50 |
| Happy path works — `JSON.parse(body)` equivalent to `res.json()` | ✅ Same data, return shape unchanged |
| HTML 401 / non-JSON failure caught by try/catch | ✅ L54–57: `SyntaxError` caught, logs + returns null |
| All code paths return | ✅ Non-ok → L51, parse-fail → L56, ok → L58 |

---

## ADO#3289 — Detailed Findings

### harness-server.js — CC Spawn Logging

| Check | Result |
|-------|--------|
| `claude --version` at bootstrap, result logged | ✅ Startup IIFE, L2555–2561 |
| Pre-spawn log: command, cwd, userId, briefLen | ✅ L1767 |
| stdout chunks logged with `bytes=` count in real-time | ✅ L1792 (before buffer append) |
| stderr chunks: `bytes=` + first 500 chars | ✅ L1835 |
| `scrubSecrets()` still applied to stderr before client | ✅ L1836 |
| Close handler: exit code + `ccTextEmitted` logged | ✅ L1842 |
| Silent zero-exit warning | ✅ L1843–1845: `code === 0 && !ccTextEmitted` |
| Non-zero exit warning | ✅ L1846–1848: `code !== 0` |
| No infinite loops | ✅ No loops in new code |
| Buffer accumulation bounded | ✅ NDJSON line-split pattern unchanged; at most one partial line in buffer |

---

## Security Quick-Check

- **Token masking:** `[..8]` confirmed in source (not `[..80]` or any larger slice)
- **Structured properties:** Only `userId` (GUID) and `maskedIncoming` (8-char prefix) logged — never raw `incomingToken` or `expectedToken`
- **`execSync` injection risk:** Hardcoded string `'claude --version'` — no env var interpolation — no injection vector
- **Client exposure:** `scrubSecrets()` applied to stderr before `sendEvent` — no new client exposure introduced

---

## Positive Observations

- The `res.text()` before `res.ok` pattern is the correct approach — correctly anticipates non-JSON error bodies (HTML 401 pages) that would break `res.json()` silently
- Masking logic handles all edge cases cleanly (empty, 1–7 chars, ≥8 chars)
- Silent-zero-exit warning is exactly the right diagnostic for "CC ran but produced nothing" — this has been a hard-to-debug failure mode
- Pre-spawn brief length logging will immediately expose "empty brief" bugs

---

## Nitpicks (2, not blocking)

**N1:** `execSync('claude --version', { timeout: 10000 })` — 10s blocking window at startup. Not a bug; just be aware if container health checks have tight timeouts. Could reduce to 5000ms.  
**File:** `harness-server.js` ~L2556

**N2:** `body=${responseBody}` on the JSON parse-failure path logs the full response body. Theoretical risk if server ever returns a malformed 200 containing token data. Not currently possible given server contract, but worth noting for future defensive review.  
**File:** `harness-server.js` ~L55

---

## Summary

Clean P0 logging additions. Token masking is correct and thorough. All required diagnostic log points are present. The `getUserTokens` refactor (body-before-status, try/catch JSON parse) is a correctness improvement over the previous `res.json()` pattern. No issues blocking shipment.

---

_Clint Barton — Hawkeye — ADO#3288+3289 — Cycle 1_
