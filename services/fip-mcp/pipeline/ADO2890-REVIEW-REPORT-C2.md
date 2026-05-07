# Review Report — ADO#2890 Cycle 2

**Verdict: NEEDS-CHANGES**

**Cycle:** 2 of 2  
**Fix commit:** `8dda6ea`  
**Files reviewed:** `src/tools/ado/list_work_items.js`, `src/tools/ado/update_work_item.js`, `src/tools/ado/list_iterations.js`  
**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-07

---

## CC Review Summary

Brief written to `/tmp/review-2890-c2-brief.md`. Invoked as:
```
CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
claude --model sonnet --print --dangerously-skip-permissions < /tmp/review-2890-c2-brief.md
```

CC flagged one confirmed issue: `err.statusCode` property mismatch in the I3 fix. Confirmed independently by reading `ado-client.js` — the client throws a plain `new Error(...)` with no numeric status property attached. All other findings from CC were confirmed clean.

---

## Fix Verification

### I1 — WIQL Injection Fix ✅ PASS

`wiqlEscape()` is correctly defined:
```javascript
function wiqlEscape(value) {
  return value.replace(/'/g, "''");
}
```

Applied to all 5 user-supplied values at interpolation point:
| Value | Escaped? |
|-------|----------|
| `project` | ✅ `wiqlEscape(project)` |
| `state` | ✅ `wiqlEscape(state)` |
| `type` | ✅ `wiqlEscape(type)` |
| `assignedTo` | ✅ `wiqlEscape(assignedTo)` |
| `iteration` | ✅ `wiqlEscape(iteration)` |

Applied to values only — hardcoded field names (in `[brackets]`) and SQL keywords are untouched. Clean strings with no single quotes pass through unchanged. No bypass paths.

---

### I2 — `$expand=relations` on PATCH URL ✅ PASS

Exact URL in code:
```javascript
const wi = await adoPatch(`/_apis/wit/workitems/${id}?$expand=relations&api-version=7.1`, ops);
```

Both `$expand=relations` and `api-version=7.1` present. `parentId` extraction logic intact and correct:
```javascript
const parentRelation = wi.relations?.find(r => r.rel === 'System.LinkTypes.Hierarchy-Reverse');
const parentId = parentRelation
  ? parseInt(parentRelation.url.split('/').pop(), 10)
  : null;
```

Optional chaining on `wi.relations` handles items with no relations gracefully (returns `null`).

---

### I3 — 404 Error Message ❌ FAIL

**Root cause confirmed.** The 404 check is dead code.

`ado-client.js` throws on non-2xx responses as:
```javascript
throw new Error(`[ADO] GET ${path} failed: ${res.status} ${res.statusText} — ${body}`);
```

This is a plain `Error` — no `statusCode` property, no `status` property. The status code is embedded in the **message string** only.

The catch block in `list_iterations.js` checks:
```javascript
if (err.statusCode === 404) {
```

`err.statusCode` is always `undefined`. The condition is never true. The intended friendly error message is never shown. The raw ADO error (with URL and PAT-bearing context) propagates instead.

**Fix:**

Option A — Parse the status from the error message string (matches current client behavior):
```javascript
if (err.message?.includes(' 404 ')) {
```

Option B — Attach `statusCode` in `ado-client.js` and use it everywhere (cleaner long-term):
```javascript
// In ado-client.js, for each thrower:
const err = new Error(`[ADO] GET ${path} failed: ${res.status} ${res.statusText} — ${body}`);
err.statusCode = res.status;
throw err;
```
Then the existing `err.statusCode === 404` in `list_iterations.js` works correctly, and any future error-type checks across the codebase will work consistently.

**Recommendation:** Option B. Attaching `.statusCode` to ADO errors in the client is the right fix — it makes all current and future status-based branching work without string parsing. It's a 2-line change per thrower in `ado-client.js` (4 throwers total: `adoGet`, `adoPost`, `adoPostPatch`, `adoPatch`).

---

## Issues Found

| # | Severity | File | Issue | Fix |
|---|----------|------|-------|-----|
| I1 | **Important** | `list_iterations.js` | `err.statusCode` is never set by `ado-client.js` — 404 branch is dead code | Attach `.statusCode = res.status` in all 4 `ado-client.js` throwers (Option B), or parse message string (Option A) |

---

## Regression Check ✅ Clean

- `wiqlEscape` only replaces `'` → `''`. Operators used are `=` and `UNDER` (not `LIKE`), so no WIQL wildcard characters are affected.
- `$expand=relations` in `update_work_item.js` adds a `relations` array to the response. Existing field mapping reads only from `wi.fields` — unaffected. `wi.relations?.find()` handles `undefined` gracefully.
- Non-404 errors in `list_iterations.js` still re-throw via `throw err` — no silent swallowing regardless of the `statusCode` bug.

---

## Spec Compliance

N/A for cycle 2 — targeted fix verification only.

---

## What to Fix

Tony, one thing to close before this ships:

**`ado-client.js`** — in all 4 throwers (`adoGet`, `adoPost`, `adoPostPatch`, `adoPatch`), attach the HTTP status code to the thrown error:

```javascript
// Example for adoGet — apply same pattern to adoPost, adoPostPatch, adoPatch
if (!res.ok) {
  const body = await res.text().catch(() => '');
  const err = new Error(`[ADO] GET ${path} failed: ${res.status} ${res.statusText} — ${body}`);
  err.statusCode = res.status;
  throw err;
}
```

Once that's done, the `err.statusCode === 404` check in `list_iterations.js` works as written. No changes needed to the iterations file itself.

I1, I2 from cycle 1: fully clean. This is the only remaining blocker.
