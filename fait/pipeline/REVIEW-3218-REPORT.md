# Review Report — ADO#3218

## Verdict: FAIL

**Cycle:** 1 of 2  
**Reviewer:** Clint Barton (Hawkeye)  
**CC Model:** Sonnet

---

## CC Review Summary

CC confirmed three independent critical defects, all verified by tracing actual code paths — not comments or assertions in the build report. Two pass items and three no-op/pass findings round out the coverage. No false positives identified; all three criticals are real bugs that will fail silently at runtime.

---

## Spec Compliance Check

**Spec:** Two-part fix — Blazor `EnabledMcpSlugs` in TurnRequest + ChatView slug extraction; Harness `MCP_TOOL_SPECS` + dynamic toolConfig injection.

**Blazor side:** ✅ `TurnRequest` additive field added, `ChatView.razor` slug extraction logic is syntactically correct.

**Harness side:** ❌ `MCP_TOOL_SPECS` defined but uses wrong key for Azure DevOps. ❌ Agentic loop not updated to dispatch MCP tool calls.

**Spec compliance verdict:** ❌ NON-COMPLIANT — implementation is incomplete; MCP tools are added to toolConfig spec but never routed when called.

---

## Consistency Audit

**Files cross-referenced:**

| Pair | Check | Result |
|------|-------|--------|
| `DatabaseInitializationService.cs` (seed) ↔ `McpToolService.cs` (DevOpsSlug) | DB slug for Azure DevOps | `devops` ✓ |
| `McpToolService.cs` (FullName construction) ↔ `ChatView.razor` (slug extraction) | Slug extraction from `{slug}__{toolName}` | `devops` extracted ✓ |
| ChatView `enabledMcpSlugs` = `["devops"]` ↔ `harness MCP_TOOL_SPECS` keys | Key lookup | **`devops` key MISSING** ❌ |
| DB m365 manifest names ↔ `MCP_TOOL_SPECS.m365` names | Tool name alignment | **All names differ** ❌ |
| DB devops manifest names ↔ `MCP_TOOL_SPECS.azdo` names | Tool name alignment | **All names differ** ❌ |
| `MCP_TOOL_SPECS` tool names ↔ Agentic loop dispatch | Dispatch handlers exist | **No handlers for graph_* / ado_*** ❌ |
| ADO#3215 `pendingToolResults` ↔ ADO#3218 changes | Regression | Untouched ✓ |

---

## Critical Issues

### C1: `devops` slug key missing from `MCP_TOOL_SPECS`

| Field | Detail |
|-------|--------|
| **File** | `fait-v2/agent-harness/harness-server.js`, lines 318–392 |
| **Category** | Consistency — slug mismatch across system boundary |

**Evidence:**

`DatabaseInitializationService.cs` seeds Azure DevOps with slug `'devops'`:
```sql
VALUES ({0}, 'Azure DevOps', 'devops', ...)
```

`McpToolService.cs` confirms: `DevOpsSlug.Slug = "devops"`. `FullName` is built as `devops__list_devops_projects`, etc. ChatView extracts the prefix before `__` → sends `enabledMcpSlugs = ["devops"]`.

`MCP_TOOL_SPECS` keys: `m365`, `azdo`, `ado` (alias). **No `devops` key.** The harness lookup:

```js
if (MCP_TOOL_SPECS[slug]) {   // MCP_TOOL_SPECS['devops'] === undefined → false
    allTools.push(...MCP_TOOL_SPECS[slug]);
}
```

Azure DevOps tools are **never** added to Bedrock `toolConfig`. The `ado`/`azdo` alias is useless as written.

**Impact:** Bedrock has no visibility into any Azure DevOps tools, regardless of MCP enablement. Silent failure — no error, no log.

**Fix:**
```js
// Replace:
MCP_TOOL_SPECS['ado'] = MCP_TOOL_SPECS['azdo'];

// With:
MCP_TOOL_SPECS['ado']    = MCP_TOOL_SPECS['azdo'];
MCP_TOOL_SPECS['devops'] = MCP_TOOL_SPECS['azdo'];  // matches DB slug
```

---

### C2: MCP_TOOL_SPECS tool names don't match DB manifest tool names

| Field | Detail |
|-------|--------|
| **File** | `harness-server.js` lines 319–385 vs `DatabaseInitializationService.cs` lines 510–521, 556–561 |
| **Category** | Consistency — parallel definitions drift |

**DevOps comparison:**

| DB manifest name | MCP_TOOL_SPECS name |
|-----------------|---------------------|
| `list_devops_projects` | `ado_list_work_items` |
| `get_work_item` | `ado_get_work_item` |
| `query_work_items` | `ado_create_work_item` |
| `list_repositories` | `ado_update_work_item` |
| `list_pipelines` | `ado_list_projects` |
| `trigger_pipeline` | *(missing)* |
| `create_work_item` | *(missing)* |
| `update_work_item` | *(missing)* |
| `add_work_item_comment` | *(missing)* |
| `create_branch` | *(missing)* |
| `create_pull_request` | *(missing)* |
| `update_pull_request` | *(missing)* |

**M365 comparison:**

| DB manifest name | MCP_TOOL_SPECS name |
|-----------------|---------------------|
| `list_emails` | `graph_list_emails` |
| `get_email` | `graph_get_email` |
| `send_email` | `graph_send_email` |
| `list_calendar_events` | `graph_list_calendar_events` |
| `create_calendar_event` | *(missing)* |

**Impact:** Even if C1 were fixed, the Bedrock model would be offered tool names from `MCP_TOOL_SPECS` (e.g. `graph_list_emails`) that don't correspond to anything `McpToolService` knows about. The actual tool dispatch path in `McpToolService.ExecuteToolAsync` strips the slug prefix and looks up the raw name (`list_emails`) — it would never find `graph_list_emails`. The harness HTTP routes `/tools/graph_list_emails` do exist and match `MCP_TOOL_SPECS` names, which is consistent, but the `McpToolService` path diverges.

**Fix:** The MCP_TOOL_SPECS should be built from the same names the harness routes actually implement (`graph_*`, `ado_*`) — which they currently are. However, the DB manifest names need to be aligned so that `McpToolService.ExecuteToolAsync` can dispatch correctly. The cleanest fix: update the DB seed in `DatabaseInitializationService.cs` to use the `graph_`/`ado_` prefixed names to match. This aligns all three layers.

---

### C3: Agentic loop has no dispatch handlers for `graph_*` or `ado_*` tools

| Field | Detail |
|-------|--------|
| **File** | `harness-server.js`, lines 1899–1999 |
| **Category** | Correctness — missing code path |

**Evidence:** The tool dispatch is a flat `if/else if` chain with branches only for:
`list_workspace_files`, `read_memory`, `write_memory`, `create_document`, `list_files`, `read_file`

The `else` default:
```js
} else {
    // default: search_knowledge_base
    const kbResult = await executeKbSearch(toolInput.query, toolInput.kb_type || 'personal');
```

If Bedrock calls `graph_list_emails` (with input `{ max_results: 10 }`), it falls through to KB search. `toolInput.query` is `undefined`. The KB search runs silently with `undefined` query, returns garbage, and the agentic loop continues — the user gets a hallucinated response with no error.

**Impact:** This is the most severe defect. The M365 `m365` slug DOES exist in `MCP_TOOL_SPECS`, so M365 tools are correctly added to toolConfig when M365 is enabled. But when Bedrock calls any of those tools, every call silently misfires into KB search with an undefined query. The feature appears to work (no crash, no 4xx) but does nothing.

**Fix:** Add an MCP dispatch branch before the `else` default:
```js
} else if (
    toolUseAccumulator.name.startsWith('graph_') ||
    toolUseAccumulator.name.startsWith('ado_')
) {
    try {
        const mcpRes = await fetch(`http://localhost:${PORT}/tools/${toolUseAccumulator.name}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ userId, ...toolInput })
        });
        const mcpData = await mcpRes.json();
        toolResultText = JSON.stringify(mcpData);
    } catch (mcpErr) {
        toolResultText = `MCP tool error (${toolUseAccumulator.name}): ${mcpErr.message}`;
        isError = true;
    }
}
```

---

## Pass Items

| # | Item | Result |
|---|------|--------|
| 4 | `toolChoice: { auto: {} }` format | ✅ Correct Bedrock SDK v3 shape; no-op vs default behavior — safe |
| 5 | `inputSchema` format in MCP_TOOL_SPECS | ✅ Matches BUILTIN_TOOL_SPECS shape exactly |
| 6 | ADO#3215 `pendingToolResults` / `assistantContent` regression | ✅ Untouched |
| 7 | `TurnRequest` additive — no callers broken | ✅ `null` default, named parameters, no breaking change |
| 8 | `null` vs empty array serialization for `enabledMcpSlugs` | ✅ Harness null-coalesces to `[]` correctly |

---

## What to Fix (for Tony)

Three independent fixes required, all in `fait-v2/agent-harness/harness-server.js`:

### Fix 1 — Add `devops` alias (line 388 area)
```js
// After the existing azdo alias:
MCP_TOOL_SPECS['ado']    = MCP_TOOL_SPECS['azdo'];
MCP_TOOL_SPECS['devops'] = MCP_TOOL_SPECS['azdo'];  // ← ADD THIS
```

### Fix 2 — Align MCP_TOOL_SPECS names with actual tool names
The `ado_*` names in `MCP_TOOL_SPECS.azdo` don't match the DB manifest names (`get_work_item`, `query_work_items`, etc.) OR the harness HTTP routes (`/tools/ado_get_work_item`). There are two coherent options:

**Option A (recommended):** Keep `MCP_TOOL_SPECS` names as-is (they match the harness routes), and update the DB seed (`DatabaseInitializationService.cs`) to prefix tool names with `ado_` and `graph_` to match. This makes the harness the single source of truth for tool names.

**Option B:** Read the tool specs from `AvailableTool.FullName` at request time instead of hardcoding them in `MCP_TOOL_SPECS`. This eliminates the drift problem entirely and is more future-proof — but requires passing `availableTools` to the harness.

### Fix 3 — Add MCP tool dispatch in the agentic loop (~line 1991)
Add a dispatch branch for `graph_*` and `ado_*` tools **before** the `else` default. See code in C3 above.

All three fixes are required. C3 alone would cause M365 tools to misfire even though their slug is currently correct. C1 must be fixed for DevOps tools to appear in toolConfig at all.

---

## CC Invocation Used

```bash
cd /home/fredw/projects/fip/fait && cat /tmp/review-3218.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

_Hawkeye — Review complete. Send it back._
