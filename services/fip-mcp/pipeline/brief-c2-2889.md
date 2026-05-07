# BUILD BRIEF — ADO#2889 — MS365 MCP Connector (Cycle 2 — Review Fixes)
**Sprint 3, Lane 2 | fip-mcp**
**Agent:** Tony Stark | **Cycle:** 2 | **Date:** 2026-05-07

---

## Context

Cycle 2 — fix 4 issues from Clint's C1 FAIL verdict. Only touch what's listed.

**Repo:** `~/projects/fip/services/fip-mcp/` | **Branch:** `main` | **Current HEAD:** `7a905e7`

---

## Fix C1 — `list_emails.js`: filter + search mutual exclusion

**Problem:** Both `filter` and `search` are accepted simultaneously. Graph API returns HTTP 400 when both are present.

**Fix in `src/tools/ms365/list_emails.js`:** Add a guard at the start of the function:
```javascript
if (filter && search) {
    throw Object.assign(
        new Error('Microsoft Graph does not support $filter and $search simultaneously. Use one or the other.'),
        { statusCode: 400, code: 'INVALID_PARAMS' }
    );
}
```

---

## Fix C2 — `list_emails.js`: Unconditional `$orderby` breaks search calls

**Problem:** `.orderby('receivedDateTime DESC')` is applied on every request. Graph rejects `$orderby` when `$search` is present — separate 400 from C1.

**Fix in `src/tools/ms365/list_emails.js`:** Make orderby conditional:
```javascript
// Only apply $orderby when NOT using $search
if (!search) {
    query = query.orderby('receivedDateTime DESC');
}
```

---

## Fix C3 — `send_teams_message.js`: Wrong endpoint

**Problem:** Uses `/me/chats/{chatId}/messages` — this path does not exist in Graph v1.0. Every call returns 404.

**Fix in `src/tools/ms365/send_teams_message.js`:** Remove `/me/` prefix:
```javascript
// WRONG:
await client.api(`/me/chats/${chatId}/messages`).post(...)
// CORRECT:
await client.api(`/chats/${chatId}/messages`).post(...)
```

---

## Fix I1 — `graph-error.js`: Parse actual Graph error body

**Problem:** `err.message` is a generic SDK string like "Error making request". Actual Graph error codes (ErrorItemNotFound, ErrorAccessDenied, etc.) live in `err.body.error`.

**Fix in `src/utils/graph-error.js`:**
```javascript
export function handleGraphError(err) {
    // Try to extract Graph error details from response body
    const graphError = err.body?.error ?? err.response?.error;
    if (graphError) {
        return {
            error: {
                code: `GRAPH_${graphError.code ?? err.statusCode ?? 'ERROR'}`,
                message: graphError.message ?? err.message
            }
        };
    }
    if (err.statusCode) {
        return { error: { code: `GRAPH_${err.statusCode}`, message: err.message } };
    }
    return { error: { code: 'GRAPH_ERROR', message: err.message ?? 'Microsoft Graph error' } };
}
```

---

## Mandatory Rules

- **CC CLI MANDATORY:**
  ```bash
  CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
  cat brief-c2-2889.md | claude --model sonnet --print --dangerously-skip-permissions
  ```
- Work dir: `~/projects/fip/services/fip-mcp/`
- Only fix the 4 listed issues — no scope creep
- Commit: `fix(fip-mcp#2889): filter+search guard, orderby conditional, Teams endpoint fix, graph-error body parsing`
- Smoke test: `node src/server.js` starts cleanly

---

## ADO Comment (MANDATORY)
```bash
mcporter call devops.add_comment --args '{"project":"Fortress","id":2889,"text":"**[Tony Stark — BUILD cycle 2]**\nCommit {hash}: C1-C3 + I1 fixes. Build: SUCCEEDED."}'
```

## Deliverables
1. Cycle 2 section appended to `~/projects/fip/services/fip-mcp/pipeline/ADO2889-BUILD-REPORT.md`
2. Commit pushed to `origin/main`
3. ADO comment on #2889
4. Report back to Maria
