# Review Report — ADO#2889 (Cycle 3)

**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-07  
**Commit:** `8d7b2fa`  
**File:** `src/utils/graph-error.js`

### Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

The C3 spec required:
1. ✅ `err.code` checked first
2. ✅ `err.body` parsed as JSON string via `JSON.parse()` — NOT object access
3. ✅ Parsed body checked for `.code` directly — no `.error` wrapper
4. ✅ Falls through to `GRAPH_<statusCode>` → `GRAPH_ERROR` generics
5. ❌ No regressions — **FAILS** (logging regression, see BUG 1)

**Spec compliance:** ❌ NON-COMPLIANT on item 5

---

## Consistency Audit

**Files cross-referenced:**
- `src/utils/graph-error.js` ↔ `src/server.js` (7 call sites) — ✅ return shape `{ error: { code, message } }` consistent across all callers
- `handleGraphError` ↔ `handleToolError` (same file) — ❌ `handleToolError` logs on fallback, `handleGraphError` does not

---

## Critical Issues — 0

---

## Important Issues — 1

### I1: Server-side logging completely removed — regression

- **File:** `src/utils/graph-error.js`
- **Category:** Correctness / regression
- **Issue:** The old code had `console.error('[fip-mcp] Graph error:', err)` in the fallback path. The new code removes it with no replacement. None of the 7 callers in `server.js` add their own logging. `handleToolError` (the parallel handler) still logs. Every Graph API error is now completely silent on the server.
- **Impact:** Production Graph failures produce no server log output. Debugging silent MCP errors will require guessing.
- **Fix:**
  ```diff
  - return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
  + console.error('[fip-mcp] Graph error:', err);
  + return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
  ```
  Alternatively, log at the top of the function unconditionally (before the `err.code` check) so all paths are covered.

---

## Nitpicks — 1

### N1: Inconsistent `message` fallback in `err.code` and `statusCode` branches

- **File:** `src/utils/graph-error.js` (lines 8, 23)
- Lines 8 and 23 return `message: err.message` with no `?? 'Microsoft Graph error'` guard. The final fallback (line 25) is defensive. Low risk — SDK v3 errors with `.code` essentially always have `.message` — but inconsistent. Not blocking.

---

## Positive Observations

- `JSON.parse(err.body)` is exactly right for SDK v3. Prior cycle used object access which was wrong.
- `parsed?.code` uses safe optional chaining — correct.
- `JSON.parse` is properly wrapped in `try/catch` with fall-through on invalid JSON.
- `err.code` correctly checked before body parsing.
- No `.error` wrapper in parsed body — matches SDK v3 flat serialization.
- Return shape is consistent across all 7 callers.
- `err.code` falsy check (empty string, 0) works correctly with JS truthiness.

---

## What to Fix

**Required (blocks ship):**

Add `console.error('[fip-mcp] Graph error:', err)` back. Either:
- Before the final `return` on line 25 (matches prior behavior), or
- At the top of the function before `if (err.code)` (preferred — logs all paths including SDK-identified errors)

The preferred approach:
```js
export function handleGraphError(err) {
    console.error('[fip-mcp] Graph error:', err);
    // Graph SDK v3: err.code is set directly by SDK for well-known Graph errors
    if (err.code) { ...
```

**Optional (nitpick):**  
Add `?? 'Microsoft Graph error'` to lines 8 and 23 for consistency.

---

## CC Review Summary

CC ran full adversarial pass on `graph-error.js` + caller context in `server.js`. All 5 spec requirements verified structurally correct. CC independently identified both issues above. BUG 2 (message fallback) classified as nitpick by Clint — SDK v3 always sets `.message` when `.code` is present. BUG 1 (logging) confirmed real regression by cross-referencing `handleToolError` pattern and all 7 call sites.
