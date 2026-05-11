# Review Report — ADO#3237

### Verdict: PASS

---

### Spec Compliance Check

**Fix scope:** `ScheduledTaskBackgroundService.cs` only — exactly the right file.

**Root cause claim (from commit message):** `ProcessTaskAsync` built `TurnRequest` with `EnabledMcpSlugs` absent (null default) → harness received `enabledMcpSlugs=[]` → model had no graph/MCP tools to call → "auth error" was actually "tool not available." ✅ Verified against code — prior to this commit `EnabledMcpSlugs` was simply not passed to `TurnRequest`, confirmed by git diff.

**Out of scope:** No other files modified. ✅

---

### Consistency Audit

**Files cross-referenced:**

| File | Check | Result |
|------|-------|--------|
| `ScheduledTaskBackgroundService.cs` | `IMcpToolService` resolved from `scope.ServiceProvider` | ✅ |
| `Program.cs:285` | `IMcpToolService` registered as `AddScoped` | ✅ Scoped → resolved from scoped provider: correct |
| `McpToolService.cs:139` | `GetActiveServersForUserAsync` return type | ✅ Returns `List<McpServer>` (never null) |
| `McpServer.cs:10` | `Slug` property type | ✅ `string Slug = string.Empty` (never null) |
| `McpToolService.cs:124` | How FullName is constructed | ✅ `$"{server.Slug}__{def.Name}"` — fix reads `server.Slug` directly, ChatView splits `__`, same value |
| `ChatView.razor:966` | `EnabledMcpSlugs: count > 0 ? list : null` pattern | ✅ Fix uses identical null-sentinel pattern |

---

### CC Review Summary

CC read all five specified files. Findings confirmed:

**Dismissed as non-issues (no false positives from CC):**
- Semantic difference (fix doesn't filter servers with empty tool manifests) — harmless, harness finds no tools for that slug and moves on
- `updated_at`-style timestamps in claim SQL — pre-existing, unrelated

**Pre-existing issues noted (not introduced by this fix, not blocking PASS):**
1. **Fire-and-forget scope risk** — `ScheduledTaskBackgroundService.cs:205` creates a child scope from `services` inside a `Task.Run` body. By the time it executes, the outer `using var scope` in `PollAndDispatchAsync` may be disposed. Pre-existing, not introduced here.
2. **Two-failure permanent deactivation** — A transient MCP DB error across two consecutive poll cycles would permanently set `IsActive = false` on the task. The deactivation policy is pre-existing and applies equally to all failure types. Worth noting to Fred/Tony but not a blocker.

---

### Issues Found

| Severity | File | Issue | Fix |
|----------|------|-------|-----|
| Nitpick | `ScheduledTaskBackgroundService.cs` | Pre-existing: fire-and-forget `Task.Run` in notification block creates a child scope from `services` that may outlive its parent scope's `Dispose()` | Move notification to its own `_scopeFactory.CreateScope()` call — out of scope for this PR |
| Nitpick | `ScheduledTaskBackgroundService.cs` | Pre-existing: two consecutive task failures (including transient MCP errors) permanently deactivate the task | Consider a transient-retry grace period — out of scope for this PR |

No Critical or Important issues.

---

### DI Scope Audit

`ScheduledTaskBackgroundService` is a singleton (BackgroundService). 

✅ The scope is created correctly in `PollAndDispatchAsync` with `using var scope = _scopeFactory.CreateScope()` — disposed at method end.

✅ `IMcpToolService` is `AddScoped` in Program.cs — resolved from `scope.ServiceProvider` (scoped provider) — correct pattern.

✅ `McpToolService.GetActiveServersForUserAsync` creates its own `await using var db` — no shared EF DbContext. Tasks in the poll cycle are processed sequentially (not concurrent), so the single scoped `McpToolService` instance is never accessed concurrently.

---

### Slug Correctness

ChatView builds slugs by calling `GetConversationToolsAsync` → extracts from `AvailableTool.FullName` by splitting on `"__"`. The fix calls `GetActiveServersForUserAsync` → reads `McpServer.Slug` directly. These are identical values: `McpToolService.cs:124` constructs tool names as `$"{server.Slug}__{def.Name}"`.

The fix uses **user-level** server lookup. ChatView uses **conversation-level** (per-conversation toggles). For `ScheduledTask` (no `ConversationId`), user-level is the correct approach.

---

### Null/Error Safety

✅ `GetActiveServersForUserAsync` always returns a non-null list.

✅ `McpServer.Slug = string.Empty` default — null guard in `.Where(s => !string.IsNullOrEmpty(s))` handles edge case correctly.

✅ `EnabledMcpSlugs: count > 0 ? list : null` — correct null sentinel, consistent with ChatView.

✅ Exception path: any throw from `GetActiveServersForUserAsync` is caught by `ProcessTaskAsync`'s `catch(Exception ex)`, sets `newStatus = "failed"`, logs it, saves the run row. No risk of crashing the background service loop.

---

### Spec Fidelity

The fix directly addresses the root cause described in ADO#3237. Before this fix, `EnabledMcpSlugs` was null → harness received empty slug list → never loaded graph/MCP tool definitions → model couldn't call those tools. After this fix, the user's connected server slugs are passed at task dispatch time, consistent with how ChatView handles it for interactive sessions.

---

### What ships with this PASS

Code is correct, DI scope is safe, null handling is complete, error propagation is graceful, and the slug extraction approach is consistent with the rest of the codebase. The two pre-existing nitpicks noted above should be tracked as separate ADO work items, not held against this fix.

---

_Reviewed by Clint Barton (Hawkeye) | 2026-05-10_
