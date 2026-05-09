# Review Report — ADO#3093

### Verdict: NEEDS-CHANGES

---

### Spec Compliance Check

**Spec:** ADO#3093 — Runtime Preference Detection (harness + endpoint)

**§2 Codebase Map:**
- `agent-harness/harness-server.js` — ✅ Modified with `PREFERENCE_PATTERNS`, `hasPreferenceSignal`, `firePreferenceWrite`
- `src/FortressAI.V2.Web/Program.cs` — ✅ Modified with `POST /api/memory/write` endpoint and `MemoryWriteRequest` record

**§7 Acceptance Criteria:**
- [x] 1. `PREFERENCE_PATTERNS` array with ≥5 patterns — ✅ 11 patterns present, all required ones covered
- [x] 2. `hasPreferenceSignal(text)` function exists — ✅ Returns bool correctly
- [x] 3. `firePreferenceWrite(userId, message)` function exists — ✅ Fire-and-forget, no await
- [x] 4. `firePreferenceWrite` sends `X-Internal-Token` header — ✅ Conditional send (see Critical #1 caveat)
- [ ] 5. Called in Bedrock streaming path after assistant response — ❌ **NOT MET** — function is defined but NEVER CALLED anywhere in the file
- [x] 6. Does NOT block response — ✅ By definition (no await, `.catch()` only)
- [x] 7. `node --check` passes — ✅ Verified
- [x] 8. `POST /api/memory/write` endpoint exists — ✅
- [x] 9. Dual-auth: X-Internal-Token OR cookie auth — ✅ Correct
- [x] 10. `MemoryWriteRequest(TopicSlug, Content, UserId?, Source?)` — ✅ Matches spec
- [x] 11. Calls `IRAGWriteService.WriteFactAsync` — ✅ Line 684
- [x] 12. Returns 200 success, 400 on missing fields — ✅

**Spec compliance verdict:** ❌ NON-COMPLIANT — AC#5 not met

---

### CC Review Summary

CC independently confirmed all findings. Ran full adversarial pass with `--dangerously-skip-permissions` against both files.

**Confirmed real issues:** Defect #1 (firePreferenceWrite never called) — verified by grep showing exactly 2 hits for `firePreferenceWrite`: line 894 (definition) and zero call sites. Same for `hasPreferenceSignal`. Neither function appears anywhere in the Bedrock ConverseStream path (lines 1104–1266).

**CC also noted:** The build report states "implementation was already present from prior work" — this was the signal. Prior work added the functions; the integration step (calling them) was never done.

---

### Consistency Audit

**Files Cross-Referenced:**
- `harness-server.js` `INTERNAL_API_TOKEN` (line 78) ↔ `Program.cs` `Harness:InternalApiToken` config key — ✅ Token read and used consistently
- `harness-server.js` JSON body fields (`userId`, `topicSlug`, `content`, `source`) ↔ `Program.cs` `MemoryWriteRequest` PascalCase record — ✅ ASP.NET Core Minimal APIs use `JsonSerializerDefaults.Web` (case-insensitive by default), no custom serializer options configured — deserialization is correct
- `harness-server.js` `FAIT_BASE_URL` ↔ endpoint path `/api/memory/write` — ✅ Consistent with existing `/api/memory/search` pattern

---

### Critical Issues [1]

#### C1: `firePreferenceWrite` is defined but never called — feature does not execute at runtime
- **File:** `agent-harness/harness-server.js`
- **Category:** Correctness / Spec non-compliance
- **Issue:** `hasPreferenceSignal` and `firePreferenceWrite` are defined at lines 890–907 but have **zero call sites** in the entire file. The Bedrock ConverseStream path (lines 1104–1266) does not invoke either function at any point — not before the stream, during it, or after it. The preference detection feature is completely non-functional at runtime.
- **Evidence:**
  ```bash
  grep -n "firePreferenceWrite\|hasPreferenceSignal" harness-server.js
  # Output:
  # 890: function hasPreferenceSignal(text) {
  # 894: function firePreferenceWrite(userId, message) {
  # (no call sites)
  ```
- **Impact:** Zero preferences are ever written. The `POST /api/memory/write` endpoint is never reached from the harness. The entire ADO#3093 harness-side feature does not work.
- **Fix:** Add the following after the Bedrock stream loop completes (after line 1258, before `res.end()` at line 1261):
  ```diff
  +           // ADO#3093 — Preference detection: fire-and-forget write after response
  +           if (hasPreferenceSignal(message)) {
  +               firePreferenceWrite(userId, message);
  +           }
              console.log(`[harness] /turn: stream complete, sending done event for userId=${userId}`);
              sendEvent({ type: 'done', inputTokens, outputTokens });
              res.end();
  ```
  Placement rationale: after the streaming loop, before `res.end()` — fire-and-forget, does not block response, Bedrock path only (CC path exits via the `if (taskMode)` branch at line 1101).

---

### Important Issues [1]

#### I1: `INTERNAL_API_TOKEN` falsy guard silently drops auth header
- **File:** `agent-harness/harness-server.js`, lines 78, 896
- **Category:** Reliability
- **Issue:** `const INTERNAL_API_TOKEN = process.env.INTERNAL_API_TOKEN || ''`. Empty string is falsy. If the env var is not set, the `X-Internal-Token` header is silently omitted from `firePreferenceWrite`. The request hits the server with no token and no session cookie → 401 → silently swallowed by `.catch()`. No log entry, no observable failure.
- **Evidence:**
  ```js
  if (INTERNAL_API_TOKEN) headers['X-Internal-Token'] = INTERNAL_API_TOKEN;
  // If env var unset: empty string → falsy → header not sent → 401 on server
  ```
- **Fix:** Log a warning at startup if `INTERNAL_API_TOKEN` is unset, so deployment misconfigs are caught immediately rather than silently failing:
  ```js
  if (!INTERNAL_API_TOKEN) {
      console.warn('[harness] WARNING: INTERNAL_API_TOKEN not set — preference writes will fail with 401');
  }
  ```
  This is also already a risk on `firePreferenceWrite` specifically — the guard is correct pattern but the silent failure mode is dangerous in production.

---

### Nitpicks [2]

| # | File | Line | Issue |
|---|------|------|-------|
| N1 | harness-server.js | 901 | `topicSlug` hardcoded to `'user-preferences'` — all preference types (name, tool preference, identity) land in one bucket. No impact on correctness but may cause broad RAG retrieval. Consider subtype slugs (e.g., `'user-preferences/name'`) as follow-up. |
| N2 | harness-server.js | 620 | Duplicate env var read: `const internalToken = process.env.INTERNAL_API_TOKEN \|\| ''` re-reads directly instead of using module-level `INTERNAL_API_TOKEN` constant. Inconsistency only — not a bug. |

---

### Spec Fidelity
The C# endpoint (Program.cs) fully meets spec: correct dual-auth, correct model, correct service call, correct return codes. The harness side has the correct infrastructure (patterns, detection function, write function) but the integration glue — the call that connects them to the actual request handling — is missing. This is AC#5 explicitly: "Called in the Bedrock streaming path after the assistant response."

---

### What to fix (NEEDS-CHANGES)

**Fix 1 (required — blocks ship):** In `harness-server.js`, after the Bedrock stream for-await loop (around line 1258, before `res.end()`), add:
```js
if (hasPreferenceSignal(message)) {
    firePreferenceWrite(userId, message);
}
```

**Fix 2 (recommended):** Add a startup warning if `INTERNAL_API_TOKEN` is unset so deployment misconfig is immediately visible in logs.

Once Fix 1 is in and `node --check` passes, this is a PASS.
