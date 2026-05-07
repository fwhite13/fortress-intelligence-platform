# Review Report — ADO#2890 — fip-mcp ADO MCP Connector

**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 1
**Commits Reviewed:** `4a02a12` (initial) + `dca5c72` (create POST fix)
**Date:** 2026-05-07

---

## Verdict: NEEDS-CHANGES

3 issues require fixes before this ships. No critical blockers, but two correctness bugs and one fragility concern that must be addressed.

---

## CC Invocation Used

```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
claude --model sonnet --print --dangerously-skip-permissions < /tmp/review-2890-brief.md
```

Brief written to `/tmp/review-2890-brief.md` covering all 10 scrutiny areas from the assignment.

---

## Spec Compliance Check

**§ Files modified:** All 8 new ADO files present + `server.js` updated. ✅

**§ Acceptance Criteria:**

| Criterion | Result |
|---|---|
| All 7 tools registered in `server.js` | ✅ |
| PAT auth header: `Basic base64(:PAT)` | ✅ |
| `create_work_item`: POST + `application/json-patch+json` | ✅ |
| `update_work_item`: PATCH + `application/json-patch+json` | ✅ |
| JSON Patch builds dynamically — no undefined fields | ✅ |
| Missing PAT → graceful error, not server crash | ✅ |
| `get_work_item` maps all standard fields + extracts `parentId` from relations | ✅ |
| WIQL query only includes provided filter clauses | ✅ (but injection risk — see I1) |
| ESM throughout — no `require()` | ✅ |
| No PAT in error messages, logs, or return values | ✅ |

**Spec compliance verdict:** ✅ COMPLIANT on all criteria — but 3 issues found during adversarial review.

---

## Consistency Audit

- `ado-client.js` exports (`adoGet`, `adoPost`, `adoPatch`, `adoPostPatch`, `isPATConfigured`) ↔ all tool files — ✅ consistent
- `server.js` tool names ↔ function names in tool files — ✅ consistent, no copy-paste errors
- `adoPostPatch` (POST + json-patch+json) ↔ `create_work_item.js` — ✅ correct method used
- `adoPatch` (PATCH + json-patch+json) ↔ `update_work_item.js` — ✅ correct method used
- PAT guard in `server.js` ↔ all 7 tool handlers — ✅ all covered

---

## Issues Found

### Important Issues — 3

#### I1: WIQL Injection — Unescaped Single Quotes in `list_work_items.js`

- **File:** `src/tools/ado/list_work_items.js` (lines ~13–17)
- **Category:** Correctness / Security
- **Issue:** All five filter values (`project`, `state`, `type`, `assignedTo`, `iteration`) are interpolated directly into WIQL string literals without escaping single quotes. In WIQL, single quotes inside string literals must be escaped by doubling them (`''`). A value like `O'Brien` produces malformed WIQL → ADO 400 error. This is a correctness bug for any user with a single quote in their display name, and a WIQL injection vector for a malicious caller.
- **Evidence:**
  ```js
  const conditions = [`[System.TeamProject] = '${project}'`];
  if (state)      conditions.push(`[System.State] = '${state}'`);
  if (type)       conditions.push(`[System.WorkItemType] = '${type}'`);
  if (assignedTo) conditions.push(`[System.AssignedTo] = '${assignedTo}'`);
  if (iteration)  conditions.push(`[System.IterationPath] UNDER '${iteration}'`);
  ```
- **Fix:**
  ```js
  // Add at top of file (or in ado-client.js as shared util)
  const wiqlEscape = s => s.replace(/'/g, "''");

  // Apply to every interpolated value:
  const conditions = [`[System.TeamProject] = '${wiqlEscape(project)}'`];
  if (state)      conditions.push(`[System.State] = '${wiqlEscape(state)}'`);
  if (type)       conditions.push(`[System.WorkItemType] = '${wiqlEscape(type)}'`);
  if (assignedTo) conditions.push(`[System.AssignedTo] = '${wiqlEscape(assignedTo)}'`);
  if (iteration)  conditions.push(`[System.IterationPath] UNDER '${wiqlEscape(iteration)}'`);
  ```

---

#### I2: `update_work_item.js` — Missing `$expand` on PATCH URL Means `parentId` Always Returns `null`

- **File:** `src/tools/ado/update_work_item.js` (line ~21)
- **Category:** Correctness
- **Issue:** The PATCH URL has no `$expand` parameter. ADO does not return `relations` by default on PATCH responses. The tool then tries to extract `parentId` from `wi.relations`, which will always be `undefined`, so `parentId` is always `null` in the update response — regardless of the actual parent relationship. The code is functionally broken for callers that rely on the returned `parentId`.
- **Evidence:**
  ```js
  // update_work_item.js — no $expand:
  const wi = await adoPatch(`/_apis/wit/workitems/${id}?api-version=7.1`, ops);

  // these lines always evaluate to parentId: null
  const parentRelation = wi.relations?.find(r => r.rel === 'System.LinkTypes.Hierarchy-Reverse');
  const parentId = parentRelation
    ? parseInt(parentRelation.url.split('/').pop(), 10)
    : null;
  ```
  Compare: `get_work_item.js` correctly uses `?$expand=all&api-version=7.1`.
- **Fix:**
  ```diff
  - const wi = await adoPatch(`/_apis/wit/workitems/${id}?api-version=7.1`, ops);
  + const wi = await adoPatch(`/_apis/wit/workitems/${id}?$expand=relations&api-version=7.1`, ops);
  ```

---

#### I3: `list_iterations.js` — Silent Team Fallback Is Fragile; No 404 Handling

- **File:** `src/tools/ado/list_iterations.js` (line ~12–13)
- **Category:** Correctness / UX
- **Issue:** When `team` is not provided, the code silently falls back to the project name as the team segment. This works only for projects whose default team was never renamed from the ADO-default `"<ProjectName> Team"` or `"<ProjectName>"` pattern. If the team has been renamed, this call returns a 404 with no useful message to the caller. The tool's parameter description in `server.js` acknowledges this, but the code itself doesn't catch the 404 and return a helpful error.
- **Evidence:**
  ```js
  // list_iterations.js:12-13
  // ADO default team name is the project name
  const teamSegment = encodeURIComponent(team ?? project);
  ```
- **Fix (recommended):** Catch 404 specifically and surface a helpful message:
  ```js
  // In the catch block or around the adoGet call:
  if (err.message?.includes('404')) {
    return {
      content: [{ type: 'text', text: `Team not found. If your project's default team has been renamed, specify the 'team' parameter explicitly.` }],
      isError: true
    };
  }
  ```
  Alternatively, if the fragility is accepted, this is the lowest-severity I3 and can be flagged as a known limitation in code comments rather than handled. But given this is an MCP tool that external callers will use, a clear error message is better than a raw 404 passthrough.

---

## Nitpicks — 1

#### N1: `list_work_items.js` — No Batch Chunking for >200 WIQL Results

- **File:** `src/tools/ado/list_work_items.js`
- The `workitems?ids=` ADO endpoint has a hard 200-ID limit per request. The schema in `server.js` caps `top` at `max: 200`, which exactly matches this limit — so in practice the tool is safe. However, the tool function itself has no defensive chunking. If called directly (bypassing MCP schema), >200 IDs will produce a silent ADO 400.
- **Not blocking.** Consider adding a comment: `// ADO workitems?ids= has a 200-ID hard limit; schema enforces max:200`.

---

## Passed Checks

| Check | Finding |
|---|---|
| PAT auth header format | ✅ `Buffer.from(':' + PAT).toString('base64')` — correct |
| PAT leak (errors, logs, return values) | ✅ No PAT in any output path |
| `create_work_item` — POST method | ✅ Uses `adoPostPatch` (POST + json-patch+json), fixed by dca5c72 |
| `create_work_item` — `$` prefix + URL encoding | ✅ `` `$${encodeURIComponent(type)}` `` |
| `update_work_item` — dynamic ops array | ✅ Guards on `undefined` before each push; empty array throws before network call |
| `get_work_item` — all 13 fields present + null-safe | ✅ All fields mapped; optional fields use `?? null`; `AssignedTo`/`CreatedBy` use `.displayName ?? null` |
| `get_work_item` — `parentId` from `relations` | ✅ Correctly uses `System.LinkTypes.Hierarchy-Reverse` with optional chaining |
| `add_comment` — API version | ✅ `7.1-preview.3` |
| PAT guard — all 7 server.js handlers | ✅ `isPATConfigured()` checked at top of every handler; secondary throw guard in `ado-client.js` |
| Missing PAT — no server crash | ✅ Two-layer defense: server.js guard + client throw-catch |
| ESM compliance — all 8 files | ✅ No `require()` anywhere; all imports/exports are ESM |
| server.js — 7 tools registered | ✅ All 7 tools present |
| server.js — tool name strings | ✅ No copy-paste errors |
| server.js — `user` passed to all 7 tools | ✅ |
| `list_iterations` — `7.1-preview.4` vs `7.1` | ✅ Uses `7.1` (stable for iterations) |
| WIQL `iteration` filter operator | ✅ `UNDER` (correct for hierarchical matching) |
| dca5c72 fix — no remnant PATCH in create | ✅ Clean, no remnants |

---

## What to Fix (Tony's action items)

**1. `src/tools/ado/list_work_items.js`** — Add `wiqlEscape` and apply to all 5 interpolated WIQL values. ~5 lines.

**2. `src/tools/ado/update_work_item.js`** — Add `$expand=relations` to PATCH URL. 1 line change.

**3. `src/tools/ado/list_iterations.js`** — Add 404 catch with a helpful error message pointing the caller to specify `team` explicitly. ~5 lines.

These are all small, targeted fixes. No architectural issues. No new files needed.

---

## Positive Observations

- **dca5c72 fix is clean and complete.** No remnants of the wrong PATCH method. `adoPostPatch` clearly documents the POST+json-patch+json combination — good naming.
- **Two-layer PAT guard** (server.js check + client-level throw) is excellent defense-in-depth. No crash path on missing PAT.
- **`get_work_item` field mapping** is thorough — all 13 fields, null-safe throughout, `relations` correctly expanded.
- **ESM compliance is perfect** — clean throughout all 8 files.
- **Dynamic patch array in `update_work_item`** is correctly guarded; the empty-array early return is a good UX touch.
- **`adoPostPatch` function name** clearly communicates the unusual combination (POST method with json-patch+json content type) — avoids future confusion.

---

_Hawkeye — REVIEW stage — ADO#2890 Cycle 1_
