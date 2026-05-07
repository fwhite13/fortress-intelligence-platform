# Review Report — ADO#2889

**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Date:** 2026-05-07  
**Branch:** main | **Commit:** 7a905e7  
**Scope:** MS365 MCP Connector — email, calendar, Teams tools added to `fip-mcp`

---

### Verdict: FAIL

3 critical bugs. 2 are guaranteed runtime failures on real calls. Fix all 3, resubmit.

---

### CC Review Summary

CC read all 11 new/changed files. Confirmed 3 critical issues, 1 needs-changes, 1 low severity. I independently verified each critical finding against the raw source files. All findings stand. No false positives.

The good news: rawToken threading is solid, `create_calendar_event` Teams flag is correct (both `isOnlineMeeting` AND `onlineMeetingProvider` are set), forge-kb tools are untouched, ESM compliance is clean, package.json is correct, server starts clean.

The bad news: list_emails has two bugs (one Tony already flagged, one he missed), and send_teams_message hits a non-existent endpoint.

---

### Consistency Audit

**Files Cross-Referenced:**
- `auth.js` validateToken() return object ↔ `server.js` req.user usage — ✅ rawToken attached in authMiddleware, passed as `req.user.rawToken` to createMcpServer
- `server.js` createMcpServer(user, rawToken) ↔ all 7 MS365 tool registrations — ✅ rawToken threaded via `createGraphClient(rawToken)` for each
- `graph-client.js` → `@microsoft/microsoft-graph-client` authProvider — ✅ correct pattern
- All MS365 tools → `handleGraphError` (from graph-error.js) — ✅ consistent; no mismatches with forge-kb tools using `handleToolError`
- forge-kb tool registrations in server.js — ✅ all 6 still present (search_kb, list_kbs, add_to_kb, get_kb_metadata, get_job_status, list_kb_files)

**rawToken leakage check:**
- `auth.js` — ✅ no logging of rawToken
- `server.js` — ✅ no logging of rawToken
- `graph-client.js` — ✅ no logging of rawToken

---

### Critical Issues [3]

#### C1: `list_emails.js` — No guard against simultaneous `$filter` + `$search`
- **File:** `src/tools/ms365/list_emails.js` (lines 14–19)
- **Category:** Correctness / Graph API contract
- **Issue:** Tony flagged this himself. `$filter` and `$search` are both optional schema params. Code applies each independently if provided. Microsoft Graph returns HTTP 400 when both are present on `/me/messages`. Zero guard exists.
- **Evidence:**
  ```javascript
  if (filter) {
      req = req.filter(filter);
  }
  if (search) {
      req = req.search(`"${search}"`);
  }
  ```
- **Impact:** Any call with both `filter` and `search` populated returns a 400 error at runtime.
- **Fix:**
  ```diff
  + if (filter && search) {
  +     throw new Error('$filter and $search are mutually exclusive — provide one or the other, not both');
  + }
    if (filter) {
        req = req.filter(filter);
    }
    if (search) {
        req = req.search(`"${search}"`);
    }
  ```

#### C2: `list_emails.js` — Unconditional `$orderby` breaks `$search` requests
- **File:** `src/tools/ms365/list_emails.js` (line 12)
- **Category:** Correctness / Graph API contract
- **Issue:** Tony flagged C1 but missed this one. `.orderby('receivedDateTime DESC')` is applied unconditionally on every request. Microsoft Graph also rejects `$orderby` when `$search` is used on `/me/messages` (returns 400: sort not supported with search). This is a SEPARATE failure path from C1 — even if C1 is fixed, a search-only call still returns 400 because of the unconditional orderby.
- **Evidence:**
  ```javascript
  let req = graphClient
      .api('/me/messages')
      .select('id,subject,from,receivedDateTime,bodyPreview,isRead')
      .top(top)
      .orderby('receivedDateTime DESC');   // ← unconditional
  ```
- **Impact:** `$search` calls fail at runtime even after C1 is fixed. Both must be fixed together.
- **Fix:**
  ```diff
  - .orderby('receivedDateTime DESC');
  + // orderby applied below — not compatible with $search
  
    if (filter) {
        req = req.filter(filter);
    }
    if (search) {
        req = req.search(`"${search}"`);
  + } else {
  +     req = req.orderby('receivedDateTime DESC');
    }
  ```

#### C3: `send_teams_message.js` — Wrong Graph API endpoint
- **File:** `src/tools/ms365/send_teams_message.js` (line 9)
- **Category:** Correctness / Graph API contract
- **Issue:** The docstring even says `/me/chats/{chatId}/messages` — that endpoint does not exist in Microsoft Graph. The correct path for posting to a chat is `/chats/{chatId}/messages` (no `/me/` prefix).
- **Evidence:**
  ```javascript
  const msg = await graphClient
      .api(`/me/chats/${chatId}/messages`)   // ← /me/ prefix is wrong
      .post({ ... });
  ```
- **Impact:** Every `send_teams_message` call returns 404. The tool is completely broken.
- **Fix:**
  ```diff
  - .api(`/me/chats/${chatId}/messages`)
  + .api(`/chats/${chatId}/messages`)
  ```
  Also fix the JSDoc `@param` comment at line 4 to remove the `/me/` from the path description.

---

### Important Issues [1]

#### I1: `graph-error.js` — Doesn't extract Graph SDK error body; swallows error codes
- **File:** `src/utils/graph-error.js` (lines 8–9)
- **Issue:** The `@microsoft/microsoft-graph-client` SDK exposes Graph error details in `err.body` as `{ error: { code: string, message: string } }`. The current handler only reads `err.message`, which is a generic string. All Graph-specific error codes (`ErrorItemNotFound`, `ErrorAccessDenied`, `ErrorMailboxNotEnabledForRESTAPI`, etc.) are discarded and replaced with `GRAPH_404`, `GRAPH_403`, etc. This makes error responses useless for debugging Graph permission/scope issues.
- **Evidence:**
  ```javascript
  const msg = err.message ?? 'Microsoft Graph error';
  return { error: { code: `GRAPH_${err.statusCode}`, message: msg } };
  ```
- **Fix:**
  ```javascript
  export function handleGraphError(err) {
      if (err.statusCode) {
          const body = typeof err.body === 'string' ? JSON.parse(err.body) : (err.body ?? {});
          const code = body?.error?.code ?? `GRAPH_${err.statusCode}`;
          const msg  = body?.error?.message ?? err.message ?? 'Microsoft Graph error';
          return { error: { code, message: msg } };
      }
      console.error('[fip-mcp] Graph error:', err);
      return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
  }
  ```

---

### Nitpicks [1]

- **N1:** `get_email.js` — `attachments` is included in `.select()` but not included in the returned object. Either remove it from select (saves bandwidth) or add `attachments: msg.attachments` to the return. Not blocking.

---

### Positive Observations

- **rawToken threading is clean.** `auth.js` → `authMiddleware` → `req.user.rawToken` → `createMcpServer(user, rawToken)` → `createGraphClient(rawToken)` inside each tool. The chain is correct and no token leakage anywhere.
- **`create_calendar_event.js` Teams meeting flag is correct.** Both `isOnlineMeeting: true` AND `onlineMeetingProvider: 'teamsForBusiness'` are set together inside the `if (isTeamsMeeting)` block. Good catch — a lot of implementations only set one of these.
- **Forge-kb zero regressions.** All 6 existing tools (search_kb, list_kbs, add_to_kb, get_kb_metadata, get_job_status, list_kb_files) are unchanged in server.js. Tool handler paths untouched.
- **ESM compliance is perfect.** Every new file uses `import`/`export`. No `require()` anywhere.
- **Server starts clean.** `node src/server.js` starts without errors or warnings.
- **graph-client.js pattern.** The authProvider callback approach is the correct pattern for the MS Graph SDK when integrating with a caller-supplied Bearer token.

---

### Spec Fidelity

| Check | Status |
|-------|--------|
| 7 Graph tool files created | ✅ All present |
| graph-client.js factory | ✅ Correct |
| graph-error.js error handler | ⚠️ Present but incomplete (I1) |
| auth.js rawToken return | ✅ Threading works (design note in CC output) |
| server.js createMcpServer(user, rawToken) | ✅ Correct |
| All 7 tools registered | ✅ Confirmed |
| No forge-kb regressions | ✅ Confirmed |
| filter+search guard on list_emails | ❌ Missing (C1 + C2) |
| send_teams_message correct endpoint | ❌ Wrong endpoint (C3) |

---

### What to Fix (Tony, before resubmit)

**1. `src/tools/ms365/list_emails.js`**

Add mutual-exclusion guard at function start AND make orderby conditional on search:

```javascript
export async function listEmails(graphClient, { top = 10, filter = null, search = null }) {
    // Guard: Graph rejects $filter + $search together
    if (filter && search) {
        throw new Error('$filter and $search are mutually exclusive — provide one or the other, not both');
    }

    let req = graphClient
        .api('/me/messages')
        .select('id,subject,from,receivedDateTime,bodyPreview,isRead')
        .top(top);

    if (filter) {
        req = req.filter(filter).orderby('receivedDateTime DESC');
    } else if (search) {
        req = req.search(`"${search}"`);
        // Note: $orderby is NOT added — Graph rejects it with $search
    } else {
        req = req.orderby('receivedDateTime DESC');
    }

    const response = await req.get();
    return (response.value ?? []).map(msg => ({ ... }));
}
```

**2. `src/tools/ms365/send_teams_message.js`**

One-line fix: remove `/me/` prefix from the API path:
```javascript
.api(`/chats/${chatId}/messages`)
```
Also fix the JSDoc comment at line 4.

**3. `src/utils/graph-error.js`**

Replace the handler body with the version in I1 above so Graph error codes surface properly.

---

### New Anti-Pattern for MEMORY.md

**Graph API: $filter + $search + $orderby mutual exclusion on /me/messages**
- `/me/messages` rejects `$filter` + `$search` together (HTTP 400)
- `/me/messages` also rejects `$orderby` when `$search` is present (HTTP 400)
- Pattern: always guard with `if (filter && search) throw` AND make `$orderby` conditional on `!search`
- Correct endpoint for Teams chat messages: `/chats/{chatId}/messages` — NOT `/me/chats/{chatId}/messages`

---

_Hawkeye — ADO#2889 Cycle 1 — FAIL — 3 critical, 1 needs-changes, 1 nitpick_
