# Review Report — ADO#2856

**Task:** fip-mcp Web Search tool (Brave Search API)  
**Commit:** `0cde72c`  
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1 of 1  
**Date:** 2026-05-07

---

### Verdict: PASS ✅

All seven acceptance criteria pass. Two important findings (no fetch timeout, dotenv/ESM hoisting) should be addressed — the timeout in this PR, dotenv as a follow-up. No blockers.

---

### Spec Compliance Check

**Files reviewed:**
- `src/tools/search/search-client.js` — ✅ new, as specified
- `src/tools/search/web_search.js` — ✅ new, as specified
- `src/server.js` — ✅ modified, `web_search` tool registered

**§7 Acceptance Criteria:**

| AC | Result | Notes |
|----|--------|-------|
| `web_search` registered in `server.js` | ✅ PASS | `server.tool('web_search', ...)` exact name match |
| Brave API URL + `X-Subscription-Token` header correct | ✅ PASS | URL and header exact; no `Authorization: Bearer` |
| Missing `BRAVE_API_KEY` → graceful `isError: true`, no crash | ✅ PASS | `isAPIKeyConfigured()` guard + try/catch fallback |
| Count capped at 20 | ✅ PASS | Double-guarded: Zod `.max(20)` + `Math.min(count, 20)` |
| Returns `{ title, url, description, age }[]` | ✅ PASS | Clean map, no raw Brave internals leaked |
| No API key in logs/errors | ✅ PASS | Full trace — key value never appears anywhere |
| ESM throughout | ✅ PASS | `export`/`import`, `.js` extensions, no `require()` |

**Spec compliance verdict:** ✅ COMPLIANT

---

### Consistency Audit

**Cross-file checks:**
- `search-client.js` exports `isAPIKeyConfigured` / `braveWebSearch` ↔ `server.js` imports both — ✅ match
- `web_search.js` imports `braveWebSearch` from `./search-client.js` (`.js` extension) — ✅ correct
- `isAPIKeyConfigured` guard pattern ↔ ADO's `isPATConfigured` pattern — ✅ functionally equivalent, minor structural difference (see F5)
- Missing-key error message format matches ADO pattern — ✅ consistent

**API key exposure trace:**

| Site | Content | Key Exposed? |
|------|---------|-------------|
| `search-client.js:11` | `'...BRAVE_API_KEY env var not set...'` | ❌ No |
| `search-client.js:30` | `` `...${response.status} ${response.statusText}` `` | ❌ No |
| `server.js:540` | `console.error('[fip-mcp] web_search error:', err)` | ❌ No |
| `server.js:541` | `` `Search error: ${err.message}` `` | ❌ No |
| `search-client.js:25` | `'X-Subscription-Token': BRAVE_API_KEY` | Outbound header only — never logged |

AC6 confirmed clean.

---

### Critical Issues: 0

None.

---

### Important Issues: 2

#### I1 — No fetch timeout on Brave API call
- **File:** `src/tools/search/search-client.js` (line 21)
- **Category:** Correctness / Reliability
- **Issue:** `fetch()` has no `AbortController` / `signal` timeout. If Brave API hangs (network partition, service degradation), the `await fetch(...)` holds the connection indefinitely. In a single-process Node server, repeated `web_search` calls under a hung Brave endpoint accumulate zombie connections until TCP timeout (~minutes).
- **Impact:** Under adversarial conditions or Brave API incidents, this becomes a connection exhaustion vector.
- **Fix:**
  ```diff
  - const response = await fetch(`${BRAVE_BASE_URL}/web/search?${params}`, {
  -   headers: {
  + const response = await fetch(`${BRAVE_BASE_URL}/web/search?${params}`, {
  +   signal: AbortSignal.timeout(10_000),
  +   headers: {
  ```

#### I2 — dotenv/ESM hoisting: `BRAVE_API_KEY` captured before `dotenv.config()` runs
- **File:** `src/server.js:6` vs `src/tools/search/search-client.js:2`
- **Category:** Correctness / Configuration
- **Issue:** In Node.js ESM, static `import` declarations are evaluated depth-first during module graph construction — before the importing module's body executes. Execution order on startup:
  1. `search-client.js` evaluates → `const BRAVE_API_KEY = process.env.BRAVE_API_KEY` captures whatever is in env at that moment
  2. All other imported modules evaluate
  3. **Then** `server.js` body begins → `dotenv.config()` fires at line 6
  
  Result: if `BRAVE_API_KEY` is in a `.env` file, it's not yet loaded when `search-client.js` captures it. `isAPIKeyConfigured()` returns `false` for the entire process lifetime.
- **Impact:** Non-issue in production (ECS env vars injected before process start). Breaks local dev with `.env` files — `web_search` always returns "not configured" regardless of what's in `.env`. Same issue affects `AZDO_PAT` and other module-level captures (predates this PR).
- **Fix:** Move `dotenv.config()` to a top-level entry shim that runs before the main module graph loads. Quickest fix: add `import 'dotenv/config'` as the very first line of `server.js` (side-effect import) and remove the manual `dotenv.config()` call.

---

### Nitpicks: 4

- **N1 — `count` not `.int()`** (`server.js:529`) — `z.number().min(1).max(20)` allows `3.5`, which becomes `"3.5"` in URLSearchParams. Brave expects integer. Fix: `.int()`. Not blocking.
- **N2 — Empty `query` not rejected** (`server.js:528`) — `z.string()` allows `""`, which wastes a Brave API call. Fix: `.min(1)`. Not blocking.
- **N3 — `isAPIKeyConfigured()` guard inside try/catch vs ADO pattern outside** (`server.js:533`) — Functionally identical since the guard cannot throw. Pattern inconsistency only. Not blocking.
- **N4 — `Accept-Encoding: gzip` manual header** (`search-client.js:24`) — undici's native fetch negotiates compression automatically; this header is redundant and potentially confusing. Not blocking.

---

### Positive Observations

- **Double-guarded count cap** — both Zod `.max(20)` and `Math.min(count, 20)` means the API call is always safe even if the schema validation layer is bypassed
- **API key isolation** — key lives only in `search-client.js`, never flows into `web_search.js` or `server.js`, clean separation of concerns
- **Consistent error return shape** — `isError: true` pattern matches the rest of the codebase identically
- **Clean result mapping** — only `{ title, url, description, age }` returned, no Brave API internals leaking through, `r.age ?? null` null-safe

---

### What to Fix (before merge)

**I1 — Fetch timeout (Important — should fix now):**
```js
// search-client.js, line 21
const response = await fetch(`${BRAVE_BASE_URL}/web/search?${params}`, {
  signal: AbortSignal.timeout(10_000),   // ← add this
  headers: {
    'Accept': 'application/json',
    'Accept-Encoding': 'gzip',
    'X-Subscription-Token': BRAVE_API_KEY,
  },
});
```

**I2 — dotenv hoisting (follow-up PR acceptable):**
```js
// server.js, line 1 — replace current dotenv pattern
import 'dotenv/config';   // must be first import; remove dotenv.config() call below
```

---

_You see what others miss. Your CC specs are adversarial by design._
