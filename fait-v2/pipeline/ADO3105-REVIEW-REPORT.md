## Review Report — ADO#3105

### Verdict: PASS WITH WARNINGS

**Commit:** `77f00607`  
**File reviewed:** `fait-v2/agent-harness/harness-server.js`  
**Also checked:** `fait-v2/src/FortressAI.V2.Web/Components/Chat/ChatView.razor`  
**Build:** `node --check` ✅ passes

---

### CC Review Summary

CC performed full adversarial analysis of the `scrubSecrets()` function, regex patterns, application coverage, non-destructive behavior, and the ChatView.razor scope changes. No FAIL criteria were triggered. Two server-side log gaps are flagged as Important.

---

### Spec Compliance Check

**§2 Codebase Map:** Only `harness-server.js` was in scope. The ChatView.razor changes in this commit appear to be incidental cleanup from a prior WI (removal of a file upload UI block) — all upload code and handlers still exist in the file; no dangling references. ✅ compliant.

**§7 Acceptance Criteria:**
- [x] `scrubSecrets(text)` exists ✅
- [x] Bearer tokens pattern present ✅
- [x] AWS AKIA key pattern present ✅
- [x] `sk-` OpenAI-style key pattern present ✅
- [x] `password/secret/token/key=value` pattern present ✅
- [x] CC stdout relay scrubbed ✅
- [x] Error messages to client scrubbed ✅
- [x] Non-destructive for normal text ✅
- [x] `node --check` passes ✅

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**Files cross-referenced:** `harness-server.js` (all 5 relay points verified)

All 5 client-facing output points confirmed scrubbed:
| Application Point | Line | Status |
|---|---|---|
| Raw body dump log | 914 | ✅ `scrubSecrets(JSON.stringify(rawBody).substring(0, 500))` |
| CC stdout relay | 1066 | ✅ `scrubSecrets(chunk.toString())` |
| CC stderr relay | 1067 | ✅ `scrubSecrets(chunk.toString())` |
| CC process error handler | 1102 | ✅ `scrubSecrets(err.message)` |
| Bedrock catch → sendEvent | 1264 | ✅ `scrubSecrets(err.message)` |

---

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Important | harness-server.js | 1263 | `console.error` in Bedrock catch block logs `err.message` + `err.stack` **unscrubbed** to server logs. Client-facing `sendEvent` at line 1264 is scrubbed, but if a Bedrock error contains a credential (e.g., an AWS key in a rejected URL), it leaks server-side. | Wrap both `err.message` and `err.stack` in `scrubSecrets()` in the `console.error` call. |
| Important | harness-server.js | 914 | **Truncation before scrub**: `scrubSecrets(JSON.stringify(rawBody).substring(0, 500))` truncates first, then scrubs. A credential positioned near byte 500 could be truncated to <8 chars, bypassing the key=value pattern's `{8,}` minimum-length guard. Server-log-only risk. | Reverse order: `JSON.stringify(rawBody).replace(...)` scrub first, then truncate, OR scrub the full JSON and truncate after. |
| Nitpick | harness-server.js | 118-124 | `new RegExp(pattern.source, pattern.flags)` cloning is technically correct but unnecessary — `.replace()` already resets `lastIndex`. No bug risk. | Optional: use patterns directly without cloning, or keep as defensive practice. |
| Nitpick | harness-server.js | 123 | key=value pattern has no word boundaries (`\b`). `monkey=stablemaster123` would be redacted (`mon[REDACTED]`) because `key` appears as a substring of `monkey`. Low probability in practice for structured log data. | Add `\b` before the alternation: `(?:\b(?:password|passwd|secret|token|key)\b)` |
| Nitpick | harness-server.js | 119 | Base64 pattern only matches `==` double-padded strings. Most real JWTs and API tokens omit padding entirely. Coverage gap for unpadded secrets (they'd need Bearer or sk- patterns to be caught). | Accept as-is or extend pattern. |

---

### Spec Fidelity

All 5 acceptance criteria are met. The scrubber is present, covers all required patterns, is applied at all client-facing relay points, and is non-destructive for normal text. `null` and empty-string inputs are safely handled. `node --check` passes.

---

### What to fix (Important items — recommend fixing before shipping to prod)

**1. Scrub the server-side Bedrock error log (line 1263)**
```diff
- console.error(`[harness] /turn: Bedrock ConverseStream error for userId=${userId}: ${err.message}`, err.stack);
+ console.error(`[harness] /turn: Bedrock ConverseStream error for userId=${userId}: ${scrubSecrets(err.message)}`, scrubSecrets(err.stack ?? ''));
```

**2. Scrub before truncating the raw body dump (line 914)**
```diff
- console.log(`[harness] /turn: raw body dump: ${scrubSecrets(JSON.stringify(rawBody).substring(0, 500))}`);
+ console.log(`[harness] /turn: raw body dump: ${scrubSecrets(JSON.stringify(rawBody)).substring(0, 500)}`);
```

These are Important but do not block client-facing security — the only risk is credential leakage in server-side logs. Ship at Tony's discretion, but address in the same sprint.

---

_Review by Hawkeye (Clint Barton) — 2026-05-09_
