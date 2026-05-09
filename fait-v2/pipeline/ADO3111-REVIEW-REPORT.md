# Review Report — ADO#3111

**WI:** Move CC Routing to Harness (classifyRequest + ForceTaskMode + Run as Task button)  
**Commit:** `f86ce8c3`  
**Reviewer:** Clint Barton (Hawkeye)  
**Date:** 2026-05-09  

---

### Verdict: ✅ PASS

---

### CC Review Summary

CC read all four changed files and verified every check item against the actual code. All critical items pass. Two minor follow-ups documented — neither is a blocker.

No false positives dismissed.

---

### Spec Compliance Check

**§ Routing design:**
- `classifyRequest(message, history)` — ✅ synchronous, no Bedrock call
- Pattern `const taskMode = forceTaskMode || classifyRequest(message, history)` — ✅ client flag is override-only, not sole decider
- File extension trigger: `.docx`, `.xlsx`, `.pptx`, `.py`, `.csv` (+ pdf, js, ts, json, yaml, xml) — ✅ present
- Action verb trigger: `create|build|generate|write|make|produce|analyze|run|execute|compile|draft|develop|implement|code|script|automate` — ✅ all present
- Long message + action verb threshold: `message.length > 200` — ✅ correct
- `force_task_mode` / `ForceTaskMode` accepted from request body — ✅ dual-key `rawBody.ForceTaskMode ?? rawBody.force_task_mode ?? false`
- Old `TaskMode`/`taskMode` destructuring removed from routing — ✅ gone

**§ TurnRequest record:**
- `ForceTaskMode = false` parameter added — ✅ `IUserAgentRuntime.cs` line 51
- `TaskMode = false` still present (for ScheduledTaskBackgroundService) — ✅ preserved

**§ ChatView:**
- `_forceTaskMode = false` field — ✅ line 541
- `RunAsTask()`: sets flag, calls `SendMessage()` — ✅ correct
- `ForceTaskMode: _forceTaskMode` in TurnRequest — ✅ line 763
- Reset after send, not before — ✅ flag captured into record at line 758, reset at line 767
- Button: `@onclick="RunAsTask"`, disabled on empty input / streaming / CC running / harness not ready — ✅

**§ CSS:**
- All three color properties use `var(--chat-...)` variables — ✅ border, text, accent all use CSS vars with fallbacks

---

### Consistency Audit

| Check | Status |
|-------|--------|
| `rawBody.ForceTaskMode` → harness routing | ✅ consistent |
| `TurnRequest.ForceTaskMode` → C# model | ✅ consistent |
| `_forceTaskMode` → `ForceTaskMode:` in request builder | ✅ consistent |
| `@onclick="RunAsTask"` → method exists in code-behind | ✅ consistent |
| `TaskMode` still in TurnRequest for scheduler | ✅ preserved, not broken |

---

### Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| Follow-up | `harness-server.js` | 849 | `wasCC` read but never written — sticky CC path is permanently dead code | Populate `wasCC: true` on history entries returned from CC turns, or remove the dead branch. Tony flagged this. |
| Follow-up | `harness-server.js` | 857 | Stale `req.body?.TaskMode` reference in first `console.log` — misleading now that TaskMode is removed from routing | Remove or replace with `classifiedTaskMode` log |

---

### node --check
✅ Passes clean.

---

### What to Fix
Nothing required. Two follow-ups for a future turn:
1. Populate `wasCC` on assistant history entries if sticky CC path is desired
2. Clean up stale `TaskMode` reference in line 857 log

---

_Reviewed with Claude Code (Sonnet). ADO#3111 ships._
