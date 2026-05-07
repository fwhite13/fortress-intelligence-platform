# Review Report — ADO#2889 (Cycle 2)
**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-07  
**Commit:** `3e5612f`  
**Files reviewed:** `list_emails.js`, `send_teams_message.js`, `graph-error.js`

---

### Verdict: ⚠️ NEEDS-CHANGES

---

## Spec Compliance Check

All three files from the fix commit are present and were reviewed. Scope is correct — no out-of-scope changes detected.

---

## CC Review Summary

CC (Sonnet) ran adversarial analysis against all four cycle 1 issues plus regression scan. Findings were validated with direct SDK source inspection (`GraphErrorHandler.ts` in `node_modules/@microsoft/microsoft-graph-client/src/`) and a Node.js runtime proof.

**CC was correct on the core I1 failure.** CC's proposed fix was slightly off on the body shape — corrected below with exact fix.

---

## Issue-by-Issue Verification

### C1 — Mutual Exclusion Guard (`list_emails.js`) ✅ FIXED

Guard uses `&&` — correct, throws only when **both** `filter` AND `search` are provided.

```js
if (filter && search) {
    throw Object.assign(
        new Error('Microsoft Graph does not support $filter and $search simultaneously...'),
        { statusCode: 400, code: 'INVALID_PARAMS' }
    );
}
```

**Dead-code nitpick (non-blocking):** The `code: 'INVALID_PARAMS'` assigned to the thrown error is never read by `handleGraphError` — the error has no `body.error` or `response.error`, so it falls through to the `statusCode` branch and returns `GRAPH_400`. The custom code is silently discarded. Not a functional bug (message is preserved), but the intent is lost. Not blocking.

---

### C2 — `$orderby` conditional on `!search` (`list_emails.js`) ✅ FIXED

```js
if (!search) {
    req = req.orderby('receivedDateTime DESC');
}
```

Logically correct. `$orderby` is suppressed when `search` is present — matches Microsoft Graph API constraint.

**Edge case (non-blocking):** `search = ""` is falsy → `!search` is `true` → `orderby` applied, but `if (search)` is also false so no `$search` is sent. Empty-string search degrades to a normal paginated query — acceptable behavior.

---

### C3 — Teams endpoint (`send_teams_message.js`) ✅ FIXED

```js
.api(`/chats/${chatId}/messages`)
```

No `/me/` prefix. Correct endpoint.

**Minor gap (non-blocking):** No `chatId` validation. If `undefined`/`null`/empty is passed, the request goes to `/chats/undefined/messages` and Graph returns 404 — which will be caught by `handleGraphError` cleanly. Defensive guard would be cleaner but not blocking.

---

### I1 — Graph error body parsing (`graph-error.js`) ❌ NOT FIXED

**FAIL — structured error code extraction is unreachable.**

#### Root Cause

The Microsoft Graph JS SDK v3 (`GraphErrorHandler.ts`, `constructErrorFromResponse`) sets:
```ts
gError.body = JSON.stringify(error);  // body = string, e.g. '{"code":"ErrorAccessDenied","message":"..."}'
gError.code = error.code;             // code set directly on the GraphError instance
```

`err.body` is a **JSON string**, not a parsed object. The current implementation:

```js
const graphError = err.body?.error ?? err.response?.error;
```

- `err.body` = `'{"code":"ErrorAccessDenied","message":"..."}'` (a string)
- `"...".error` → `undefined` (strings don't have `.error`)
- `err.response?.error` → `undefined` (typically not set)
- Falls through to `if (err.statusCode)` → returns `{ code: 'GRAPH_403', message: '...' }`

**Structured error codes like `ErrorAccessDenied`, `InvalidAuthenticationToken`, etc. are NEVER surfaced.** Callers always get `GRAPH_<statusCode>`.

Runtime proof:
```
// With current code:
handleGraphError({ statusCode: 403, code: 'ErrorAccessDenied', body: '{"code":"ErrorAccessDenied",...}' })
// Returns: { error: { code: 'GRAPH_403', message: '...' } }  ← code lost

// With fix:
// Returns: { error: { code: 'GRAPH_ErrorAccessDenied', message: '...' } }  ✓
```

#### SDK note on `body` shape

`gError.body = JSON.stringify(error)` — `error` here is `graphError.error` from the API response (the inner error object directly). So `JSON.parse(err.body)` yields `{ code: "...", message: "...", innerError: {...} }` — **no `.error` wrapper**. CC's proposed fix (`parsedBody?.error`) would still miss it.

#### Required Fix

```js
export function handleGraphError(err) {
    // Graph SDK v3: err.body is JSON.stringify of the error object (no .error wrapper)
    // err.code is also set directly on the GraphError instance
    const parsedBody = typeof err.body === 'string'
        ? (() => { try { return JSON.parse(err.body); } catch { return null; } })()
        : err.body;
    const graphError = (parsedBody?.code ? parsedBody : null) ?? err.response?.error;
    if (graphError) {
        return {
            error: {
                code: `GRAPH_${graphError.code ?? err.statusCode ?? 'ERROR'}`,
                message: graphError.message ?? err.message
            }
        };
    }
    // Direct err.code fallback (also set by SDK)
    if (err.code) {
        return { error: { code: `GRAPH_${err.code}`, message: err.message } };
    }
    if (err.statusCode) {
        return { error: { code: `GRAPH_${err.statusCode}`, message: err.message } };
    }
    console.error('[fip-mcp] Graph error:', err);
    return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
}
```

---

## Issues Summary

| # | Severity | File | Issue | Status |
|---|----------|------|-------|--------|
| C1 | ~~Critical~~ | `list_emails.js` | Mutual exclusion guard | ✅ Fixed |
| C2 | ~~Critical~~ | `list_emails.js` | `$orderby` conditional | ✅ Fixed |
| C3 | ~~Critical~~ | `send_teams_message.js` | Teams endpoint | ✅ Fixed |
| I1 | **Important** | `graph-error.js` | `err.body` is a string — structured codes never surface | ❌ Still broken |

---

## Nitpicks

- **N1:** `list_emails.js` — `code: 'INVALID_PARAMS'` on the thrown error is dead (never read by `handleGraphError`). Consider using `err.code` path in the fix above, or document that it's only for direct consumers.
- **N2:** `send_teams_message.js` — guard `chatId` before use to prevent silent `/chats/undefined/messages` requests.

---

## What to Fix (Tony)

**Only one file needs a change:** `src/utils/graph-error.js`

Apply the fix shown in the I1 section above. Key points:
1. `JSON.parse(err.body)` before accessing `.code` — the body is a string in SDK v3
2. The parsed body is the error object **directly** (no `.error` wrapper) — check `parsedBody?.code`
3. Add `err.code` fallback (SDK sets this directly too) for belt-and-suspenders

3 lines to change, no other files touched. Then cycle 3 will be a quick verify.

---

_Hawkeye out._
