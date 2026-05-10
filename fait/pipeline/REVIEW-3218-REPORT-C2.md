# Review Report — ADO#3218 — Cycle 2

**Commit:** `bf60a5d6` — `fix(fait#3218): devops slug + DB seed tool names + agentic loop MCP dispatch (cycle 2)`  
**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-10  
**Cycle:** 2 of 2

---

## Verdict: NEEDS-CHANGES

Cycle 1 criticals C1 and C3 are fully resolved. C2 is partially resolved — DevOps name alignment passes, but two defects remain (one new, one pre-existing):

1. `ado_wiql_query` is missing from `MCP_TOOL_SPECS['azdo']` — Bedrock will never know this tool exists
2. `create_calendar_event` in the DB seed has no implementation, no allowlist entry, and no tool spec — will misfire to KB search if the AI ever tries to call it

---

## CC Review Summary

Ran `cat /tmp/review-3218-c2.md | claude --model sonnet --print --dangerously-skip-permissions` from `/home/fredw/projects/fip`.

CC confirmed:
- C1 PASS — both `MCP_TOOL_SPECS['ado']` and `MCP_TOOL_SPECS['devops']` aliases present at lines 388–389
- C3 PASS — dispatch branch correctly placed, correct fetch URL, correct error handling
- C2-A PASS — all 6 DevOps DB seed names match allowlist exactly
- C2-B FAIL — `ado_wiql_query` in allowlist + DB seed, but absent from `MCP_TOOL_SPECS['azdo']` (only 5 specs defined, not 6)
- C2-C FAIL — `create_calendar_event` in DB seed but not in allowlist, not in `MCP_TOOL_SPECS`, no endpoint
- C2-D FINDING — `graph_list_calendar` is a stale allowlist entry with no endpoint or spec (low severity)

Build checks: `node --check` clean, `dotnet build` succeeded (pre-existing warnings only, no new errors).

---

## Consistency Audit

| Sync Point | Result |
|------------|--------|
| `MCP_TOOL_SPECS['azdo']` ↔ `MCP_TOOL_ALLOWLIST['ado']` | ❌ `ado_wiql_query` in allowlist but NOT in specs |
| `MCP_TOOL_SPECS['azdo']` ↔ DB seed DevOps manifest | ❌ `ado_wiql_query` in DB seed but NOT in specs |
| `MCP_TOOL_SPECS['m365']` ↔ DB seed M365 manifest | ❌ `create_calendar_event` in DB seed, not in specs |
| DB seed M365 manifest ↔ `MCP_TOOL_ALLOWLIST['graph']` | ❌ `create_calendar_event` not in allowlist |
| `MCP_TOOL_SPECS['devops']` alias | ✅ Present (C1 fix) |
| `MCP_TOOL_SPECS['ado']` alias | ✅ Present (retained) |
| Dispatch `else if` branch placement | ✅ After `read_file`, before `else` default |

---

## Issues Found

| Severity | File | Issue | Fix |
|----------|------|-------|-----|
| **High** | `fait-v2/agent-harness/harness-server.js` ~line 384 | `ado_wiql_query` missing from `MCP_TOOL_SPECS['azdo']` — tool is in the allowlist and DB seed but Bedrock won't be given its spec; AI will never call it | Add a 6th entry to `MCP_TOOL_SPECS['azdo']` (see fix below) |
| **Medium** | `fait/src/FortressAI.Web/Services/DatabaseInitializationService.cs` line 554 | `create_calendar_event` in M365 DB seed with no allowlist entry, no spec, and no endpoint — if AI calls it, dispatch falls through to KB search | Remove from DB seed OR implement fully (endpoint + allowlist + spec) |
| **Low** | `fait-v2/agent-harness/harness-server.js` line 297 | `'graph_list_calendar'` in `MCP_TOOL_ALLOWLIST['graph']` with no endpoint or spec — stale entry | Remove from allowlist |

---

## Spec Fidelity

C1, C2 (alignment), and C3 are spec-compliant as fixed. The two remaining issues are gaps in the original implementation that surface under cycle 2 scrutiny — they're not regressions introduced by this commit but were not caught in C1.

---

## What to Fix (NEEDS-CHANGES)

### Fix 1 — Add `ado_wiql_query` to `MCP_TOOL_SPECS['azdo']`
**File:** `fait-v2/agent-harness/harness-server.js`  
**Location:** After the `ado_list_projects` entry in `MCP_TOOL_SPECS.azdo` (around line 384), before the closing `]`)

```diff
     {
       toolSpec: {
         name: 'ado_list_projects',
         description: 'List all Azure DevOps projects',
         inputSchema: { json: { type: 'object', properties: {}, required: [] } }
       }
-    }
+    },
+    {
+      toolSpec: {
+        name: 'ado_wiql_query',
+        description: 'Run a WIQL query against Azure DevOps',
+        inputSchema: { json: { type: 'object', properties: { wiql: { type: 'string', description: 'WIQL query string' }, project: { type: 'string', description: 'Project name (optional)' } }, required: ['wiql'] } }
+      }
+    }
   ]
```

### Fix 2 — Remove `create_calendar_event` from M365 DB seed (or implement it)
**File:** `fait/src/FortressAI.Web/Services/DatabaseInitializationService.cs` line 554

The 5th entry in `m365Manifest` (`create_calendar_event`) has no harness endpoint, no allowlist entry, and no tool spec. The simplest fix is removal:

```diff
-                    new { Name = "create_calendar_event", Description = "Create a calendar event", ... }
```

If the intent is to implement it: add a `/tools/graph_create_calendar_event` endpoint (note: rename to `graph_` prefix), add it to `MCP_TOOL_ALLOWLIST['graph']`, and add a spec entry to `MCP_TOOL_SPECS.m365`.

### Fix 3 (Low — cleanup) — Remove `graph_list_calendar` from allowlist
**File:** `fait-v2/agent-harness/harness-server.js` line 297

```diff
 'graph': new Set([
-    'graph_list_emails', 'graph_list_calendar', 'graph_get_email',
+    'graph_list_emails', 'graph_get_email',
     'graph_send_email', 'graph_list_files', 'graph_get_file_content',
     'graph_list_calendar_events'
 ]),
```

---

## Cycle 1 Criticals — Disposition

| ID | Description | Status |
|----|-------------|--------|
| C1 | `devops` alias missing from `MCP_TOOL_SPECS` | ✅ **RESOLVED** — `MCP_TOOL_SPECS['devops'] = MCP_TOOL_SPECS['azdo']` at line 389 |
| C2 | DB seed tool names don't match allowlist | ✅ **PARTIALLY RESOLVED** — DevOps 6/6 match; two M365/spec gaps remain |
| C3 | Agentic loop dispatch missing for `graph_*`/`ado_*` tools | ✅ **RESOLVED** — `else if` branch at lines 1992–2007, correctly placed and wired |
