# Review Report — ADO#3244 Cycle 2
**Reviewer:** Clint (pipeline-reviewer)
**Commit reviewed:** `47282a58`
**Prior commit:** `eac6da83`
**Date:** 2026-05-11
**Cycle:** 2 of 2

---

## Verdict: PASS

All 4 cycle-1 issues are fixed. No new issues introduced.

---

## Cycle-1 Issue Verification

| # | Severity | Issue | Status |
|---|----------|-------|--------|
| 1 | Important | tool_result event type mismatch (harness-server.js) | **FIXED** |
| 2 | Important | CCProgressHub auth gap (CCProgressHub.cs) | **FIXED** |
| 3 | Nitpick | Dead `_taskCancelled` field (ChatView.razor) | **FIXED** |
| 4 | Nitpick | Hardcoded CSS color fallbacks (ChatView.razor style block) | **FIXED** |

---

## Detail

### #1 — tool_result event type mismatch (harness-server.js)

**Fix verified:**
- `const toolUseMap = new Map()` declared at line 1738 — before CC spawn.
- In `assistant` handler: `toolUseMap.set(block.id, block.name || 'tool')` for each `tool_use` block (line 1758).
- Dead `else if (evtType === 'tool_result')` branch **GONE** — removed in diff.
- Replaced with `else if (evtType === 'user' && Array.isArray(parsed.message?.content))` iterating for `block.type === 'tool_result'`, resolving name via `toolUseMap.get(block.tool_use_id)` (lines 1762–1770).
- `toolUseMap.clear()` in `ccProcess.on('close')` at line 1783 — no leakage between requests.

**Status: FIXED** ✅

---

### #2 — CCProgressHub auth gap (CCProgressHub.cs)

**Fix verified:**
- `using System.Security.Claims;` added.
- `JoinUserGroup`: reads `Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value`, compares to `userId`, throws `HubException("Cannot join another user's group.")` on mismatch.
- `LeaveUserGroup`: identical check.
- No path exists to add a connection to another user's group.

**Edge case noted (non-blocking):** If `callerId` is null (unauthenticated) and `userId` is null, `null != null` = false → would proceed to group name `"cc-user-"`. Degenerate case, not a practical attack surface.

**Status: FIXED** ✅

---

### #3 — Dead `_taskCancelled` field (ChatView.razor)

**Fix verified:**
- Field declaration `private bool _taskCancelled = false;` removed.
- All 3 set-sites removed: `CancelTask()`, `SendMessageAsync` reset block, `mode_switch` SSE handler.
- Grep for `_taskCancelled` across ChatView.razor → **0 matches**.

**Status: FIXED** ✅

---

### #4 — Hardcoded CSS color fallbacks (ChatView.razor style block + fortress.css)

**Fix verified:**
- `var(--color-text-on-accent, #fff)` → `var(--color-text-on-accent, var(--color-text-inverted))`
- `var(--color-accent-light, rgba(212, 175, 55, 0.1))` → `var(--color-accent-light, var(--color-background-subtle))`
- `var(--color-accent-light, rgba(212, 175, 55, 0.08))` → `var(--color-accent-light, var(--color-background-subtle))`
- Both backing variables added to `fortress.css :root`:
  - `--color-text-inverted: #ffffff;`
  - `--color-background-subtle: rgba(0, 0, 0, 0.04);`

**Pre-existing out-of-scope item:** Line 266 has `box-shadow: 0 4px 12px rgba(0,0,0,0.3)` in an inline style (not the `<style>` block). Not in the cycle-2 diff; not introduced by Tony.

**Status: FIXED** ✅

---

## New Issues Scan

Full cycle-2 diff touches: harness-server.js (+15/-11 lines), ChatView.razor (+4/-10 lines), CCProgressHub.cs (+7 lines), fortress.css (+2 lines), pipeline docs only.

No new issues found:
- No hardcoded secrets or Bedrock model IDs
- No broken logic branches in the NDJSON parser
- `toolUseMap` is correctly scoped to the per-request handler closure — no cross-request contamination
- Style block is clean — all fallbacks use CSS variable references
- Auth check pattern is correct for the stated threat model

---

## CC Invocation

```
cat /tmp/review-brief-3244-c2.md | claude --model sonnet --print --dangerously-skip-permissions
```

**CC output:**

```
## Cycle 2 Verification — ADO#3244 (commit 47282a58)

| # | Issue | Status |
|---|-------|--------|
| 1 | tool_result event type mismatch | **FIXED** |
| 2 | CCProgressHub auth gap | **FIXED** |
| 3 | Dead `_taskCancelled` field | **FIXED** |
| 4 | Hardcoded CSS color fallbacks | **FIXED** |
| — | New issues introduced | **NONE** |

Final verdict: PASS

Reasoning:
1. toolUseMap correctly tracks tool_use id→name at emit time, the dead evtType === 'tool_result'
   branch is gone, the new evtType === 'user' branch correctly iterates content blocks, and
   toolUseMap.clear() on close prevents leakage between requests.
2. Both JoinUserGroup and LeaveUserGroup now validate ClaimTypes.NameIdentifier against the
   requested userId and throw HubException on mismatch. The null-userId edge case is non-blocking.
3. grep confirms zero occurrences of _taskCancelled — field declaration and all 3 set-sites removed.
4. All three hardcoded fallbacks (#fff, both rgba(...)) replaced with CSS variable references;
   --color-text-inverted and --color-background-subtle properly declared in fortress.css :root.
   The pre-existing rgba(0,0,0,0.3) inline box-shadow is out of scope.
```

---

## Files Reviewed

- `fait-v2/agent-harness/harness-server.js` — lines 1735–1820
- `fait/src/FortressAI.Web/Hubs/CCProgressHub.cs` — full (29 lines)
- `fait/src/FortressAI.Web/Components/Chat/ChatView.razor` — state fields, cancel, style block
- `fait/src/FortressAI.Web/wwwroot/css/fortress.css` — `:root` block
