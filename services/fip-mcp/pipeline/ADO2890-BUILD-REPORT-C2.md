# ADO#2890 — fip-mcp Build Report — Cycle 2

**Date:** 2026-05-07
**Commit:** `8dda6ea`
**Branch:** main
**Status:** PASSED

---

## CC Invocation

```
CLAUDE_CODE_ENTRYPOINT=ado-pipeline CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 claude --model sonnet --print --dangerously-skip-permissions
```

---

## Files Changed

| File | Change |
|------|--------|
| `services/fip-mcp/src/tools/ado/list_work_items.js` | Added `wiqlEscape()` helper and applied to all user-supplied WIQL values; updated batch fetch comment |
| `services/fip-mcp/src/tools/ado/update_work_item.js` | Added `$expand=relations` to PATCH URL so `wi.relations` is populated in response |
| `services/fip-mcp/src/tools/ado/list_iterations.js` | Wrapped `adoGet` in try/catch to surface descriptive error on 404 (invalid team/project) |

---

## Fix Confirmation

| ID | Issue | Status |
|----|-------|--------|
| I1 | WIQL injection in `list_work_items.js` — `wiqlEscape()` added and applied to all 5 interpolated values | Applied |
| I2 | `update_work_item.js` missing `$expand=relations` — added to PATCH URL | Applied |
| I3 | `list_iterations.js` silent 404 — try/catch with descriptive error message added | Applied |

---

## Pre-flight

```
Pre-flight passed. Safe to commit.
```

## Git Log Confirmation

```
8dda6ea fix(fip-mcp#2890): WIQL injection escape, update expand=relations, iterations 404 handling
```
