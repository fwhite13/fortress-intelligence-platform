# QA Report: ADO#3218 — MCP toolConfig Wiring (Blazor + Harness)

**Verdict: ✅ QA PASS**

**Analyst:** Black Widow (Natasha Romanoff)
**Date:** 2026-05-10
**Commit:** `f1af77a8`
**Task Def:** `fred-dev:177` / `fait-v2-agent-harness:18`

---

## Tests Run

- Code-level: 9 — 9 passed
- CloudWatch: 1 — 1 passed
- ADO state: 1 — 1 confirmed

---

## Service Health (Pre-Confirmed)

| Check | Result | Detail |
|-------|--------|--------|
| ECS Service `fred-dev:177` | ✅ CONFIRMED | ACTIVE 1/1 HEALTHY (pre-confirmed by deployer) |

---

## Code-Level Checks — harness-server.js (commit `f1af77a8`)

| # | Check | Result | Evidence |
|---|-------|--------|----------|
| 1 | `MCP_TOOL_SPECS['devops']` present | ✅ PASS | Line 404: `MCP_TOOL_SPECS['devops'] = MCP_TOOL_SPECS['azdo'];` — devops alias wired |
| 2 | `ado_wiql_query` in `MCP_TOOL_SPECS` | ✅ PASS | Lines 303 + 387: entry in allowlist + full spec defined |
| 3 | `enabledMcpSlugs` in /turn handler destructuring | ✅ PASS | Line 1393: `const enabledMcpSlugs = rawBody.EnabledMcpSlugs ?? rawBody.enabledMcpSlugs ?? [];` |
| 4 | Dynamic `toolConfig` build via `allTools` | ✅ PASS | Lines 1858–1864: `const allTools = [...BUILTIN_TOOL_SPECS]` + slug loop → `toolConfig = { tools: allTools, ... }` |
| 5 | `startsWith('graph_')` dispatch branch | ✅ PASS | Line 2008: `toolUseAccumulator.name.startsWith('graph_')` in agentic loop |
| 6 | `graph_list_calendar` (bare, stale) ABSENT from `MCP_TOOL_ALLOWLIST` | ✅ PASS | Allowlist contains `graph_list_calendar_events` only — bare `graph_list_calendar` not present |
| 7 | `node --check harness-server.js` | ✅ PASS | Exit 0, no syntax errors |

---

## Code-Level Checks — DatabaseInitializationService.cs

| # | Check | Result | Evidence |
|---|-------|--------|----------|
| 8 | M365 seed tools — exact set, no extras | ✅ PASS | Lines 550–553: `graph_list_emails`, `graph_get_email`, `graph_send_email`, `graph_list_calendar_events` — `create_calendar_event` ABSENT ✓ |
| 9 | DevOps seed tools — all 6 present | ✅ PASS | Lines 510–515: `ado_list_projects`, `ado_get_work_item`, `ado_list_work_items`, `ado_create_work_item`, `ado_update_work_item`, `ado_wiql_query` all present |

---

## CloudWatch Check — `/ecs/fred-dev`

| Check | Result | Detail |
|-------|--------|--------|
| Latest stream startup | ✅ PASS | `ecs/fred/863a1ee367ef438094a9b408f431ad09` — service startup normal, MCP transport calls succeeding (devops 200, brave 200, m365 200), no fatal errors |

**Log highlights:**
- `[McpTransport] ListTools http://localhost:8080/internal/mcp/devops → 200` ✅
- `[McpTransport] ListTools http://localhost:8080/internal/mcp/brave → 200` ✅
- `[McpTransport] ListTools http://localhost:8080/internal/mcp/m365 → 200` ✅
- No exceptions, no startup failures

---

## ADO State Check

| Item | Result | Detail |
|------|--------|--------|
| ADO#3218 state | ✅ CONFIRMED CLOSED | State: `Closed`, no state change needed |

---

## Summary

All 9 code-level checks pass. The MCP toolConfig wiring is correct end-to-end:

- **Harness:** `MCP_TOOL_SPECS` fully populated with m365 + azdo/ado/devops aliases. `ado_wiql_query` is the 6th ADO spec (lines 387+). `enabledMcpSlugs` is properly destructured from the /turn request body. `allTools` dynamic build correctly merges built-in specs + enabled MCP specs per request. Agentic loop dispatches `graph_*`/`ado_*` tool calls. Stale `graph_list_calendar` is absent from the allowlist.

- **Blazor DB seed:** M365 tools seeded with the correct 4-tool set. DevOps seeded with all 6 tools including `ado_wiql_query`. No extras (`create_calendar_event` absent).

- **Runtime:** CloudWatch confirms clean startup, all 3 MCP servers responding 200 at ListTools.

---

## Verdict

**✅ PASS — ADO#3218 MCP toolConfig wiring verified. Deployment confirmed healthy.**
