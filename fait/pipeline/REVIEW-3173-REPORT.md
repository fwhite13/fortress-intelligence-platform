# Review Report: ADO#3173 — On-Demand tab + History tab + Failed-task banner

## CC Invocation
```
cat /tmp/clint-review-brief-3173.md | claude --model sonnet --print --dangerously-skip-permissions
```
Working directory: `/home/fredw/projects/fip/fait`
Files read by CC: `TaskEditModal.razor`, `Tasks.razor`, `ChatView.razor`

---

## Verdict: NEEDS-CHANGES

**Reason:** ISSUE-1 is a correctness bug (on-demand task edit writes a spurious `CronExpression = "0 9 * * *"` to the DTO). ISSUE-2 is a visible UX bug in the pagination "Load more" logic. Both must be fixed. ISSUE-3 and ISSUE-4 are low-priority notes.

All three priority ACs (C1, C2, C3) **PASS**.

---

## AC Verification

| # | Item | Result |
|---|------|--------|
| 1 | On-Demand list: name, prompt preview (100 chars), last run timestamp, last_run_status badge | ✅ PASS |
| 2 | Run Now: creates run row, fire-and-forget via `Task.Run`, UI updates without blocking | ✅ PASS |
| 3 | Edit/Delete: edit opens `TaskEditModal` with `IsOnDemand=true`; delete uses `ShowMessageBox` confirmation | ✅ PASS |
| 4 | On-Demand empty state shown when no tasks | ✅ PASS |
| 5 | History ordered by `started_at` desc | ✅ PASS |
| 6 | History columns: task name, schedule type badge, started, duration (`CompletedAt - StartedAt`), status badge | ✅ PASS |
| 7 | Expandable row: full error on `failed`, `result_summary` on success; only renders when content present | ✅ PASS |
| 8 | Pagination with page size 50 | ⚠️ PARTIAL — page size is 50 but pagination "Load more" logic has a bug (see ISSUE-2) |
| 9 | History empty state shown when no runs | ✅ PASS |
| 10 | Banner appears when `failure_count > 0` (and `is_active = true`) | ✅ PASS |
| 11 | Banner dismissible via component state only (no DB/API call) | ✅ PASS |
| 12 | Banner contains link to `/tasks` | ✅ PASS |
| 13 | Banner does not render when no failures | ✅ PASS |

### Priority ACs

**[C1] RunNowAsync creates `scheduled_task_runs` row with correct fields BEFORE dispatch: ✅ PASS**

Exact trace (`Tasks.razor:470–487`):
```csharp
var run = new FortressAI.Shared.Models.ScheduledTaskRun
{
    Id = Guid.NewGuid(),
    TaskId = task.Id,           // ✅ TaskId set
    StartedAt = DateTime.UtcNow, // ✅ StartedAt set
    Status = "running"           // ✅ Status = "running"
};
await using var ctx = await DbFactory.CreateDbContextAsync();
ctx.ScheduledTaskRuns.Add(run);
await ctx.SaveChangesAsync();    // ✅ persisted BEFORE Task.Run below

task.LastRunAt = DateTime.UtcNow;
StateHasChanged();               // UI update (non-blocking)

_ = Task.Run(async () =>         // ✅ fire-and-forget
{
    await foreach (var evt in AgentRuntime.SendTurnAsync(userId, turnRequest)) { ... }
});
```
DB row is written and committed before `Task.Run`. `SendTurnAsync` is inside the `Task.Run` lambda. Method returns immediately after fire-and-forget dispatch.

---

**[C2] History query scoped to `Session.UserId`: ✅ PASS**

Exact LINQ WHERE clause (`Tasks.razor:345–347`):
```csharp
.Include(r => r.Task)
.Where(r => r.Task != null && r.Task.UserId == Session.UserId)
.OrderByDescending(r => r.StartedAt)
```
Identical clause in `LoadMoreHistoryAsync` at line 379. Filter navigates `r.Task.UserId` and matches against `Session.UserId` (current authenticated user). No URL param or route param exists that could expose another user's runs — `@page "/tasks"` has no parameters.

---

**[C3] Banner query = `IsActive && FailureCount > 0`: ✅ PASS**

Exact LINQ (`ChatView.razor:1368–1371`):
```csharp
_hasFailedTasks = await ctx.ScheduledTasks
    .AnyAsync(t => t.UserId == Session.UserId
                && t.IsActive
                && t.FailureCount > 0);
```
Both `t.IsActive` and `t.FailureCount > 0` are present with `&&`. Predicate will NOT match paused/inactive tasks that have failures (correct). Banner render condition (`ChatView.razor:72`):
```razor
@if (_agentReady && _hasFailedTasks && !_failedTaskBannerDismissed)
```
Banner cannot render before `_agentReady = true`.

---

## Issues Found

### ❌ ISSUE-1 — BUG (Must Fix): On-Demand edit writes spurious `CronExpression`
**File:** `TaskEditModal.razor:141`  
**Severity:** Important (correctness bug — corrupts on-demand task row on edit)

The CREATE path correctly nulls cron for on-demand tasks:
```csharp
// line 127
CronExpression = IsOnDemand ? null : cron,  // ✅ correct
```

But the EDIT `UpdateScheduledTaskDto` unconditionally sends `cron`:
```csharp
// lines 137-145
var dto = new UpdateScheduledTaskDto
{
    Name = _name,
    Prompt = _prompt,
    CronExpression = cron,   // ❌ BUG
    ...
};
```

When `IsOnDemand = true`, `cron` is evaluated as:
```csharp
var cron = (!IsOnDemand && _cronPreset == "custom") ? _customCron : _cronPreset;
// = (false && ...) ? ... : "0 9 * * *"
// = "0 9 * * *"   ← default preset value
```

Editing an on-demand task writes `CronExpression = "0 9 * * *"` to the update DTO, which may corrupt the `scheduled_tasks` row.

**Fix:**
```csharp
CronExpression = IsOnDemand ? null : cron,
```

---

### ⚠️ ISSUE-2 — Minor Bug: Pagination "Load more" disappears after first extra page
**File:** `Tasks.razor:259`  
**Severity:** Minor (UX regression — third page of history unreachable)

Current condition:
```razor
@if (_runHistory.Count == HistoryPageSize)  // compares total list size to 50
```

- Initial load: `_runHistory.Count = 50` → button shows ✓  
- After first "Load more": `_runHistory.Count = 100` → `100 != 50` → **button hides**, even if a third page exists ✗

**Fix:** Track the last page's result count separately:
```csharp
private int _lastHistoryPageCount = 0;
// In LoadHistoryAsync: _lastHistoryPageCount = newPage.Count;
// In LoadMoreHistoryAsync: _lastHistoryPageCount = moreRuns.Count;
```
```razor
@if (_lastHistoryPageCount == HistoryPageSize)
```

---

### ℹ️ ISSUE-3 — Perf Note: Double `GetTasksAsync` call on init
**File:** `Tasks.razor:312, 327`  
**Severity:** Low (perf, not correctness)

`OnInitializedAsync` calls `GetTasksAsync(Session.UserId)` twice — once in `LoadRecurringTasksAsync` and once in `LoadOnDemandTasksAsync`. A single call with client-side filter split would halve DB round-trips. Not blocking.

---

### ℹ️ ISSUE-4 — UX Gap: `LastRunStatus` not updated optimistically after Run Now
**File:** `Tasks.razor:482–483`  
**Severity:** Low (UX, not correctness)

`RunNowAsync` updates `task.LastRunAt` optimistically but not `task.LastRunStatus`. The Last Result badge stays stale until page reload. Suggest adding `task.LastRunStatus = "running"` before `StateHasChanged()` and optionally reloading after `Task.Run` completes via `InvokeAsync(LoadOnDemandTasksAsync)`. Not blocking.

---

## Additional Pass Checks

| Check | Result |
|-------|--------|
| No duplicate `@inject IDbContextFactory` in `ChatView.razor` | ✅ PASS — exactly one instance at line 18 |
| `TaskEditModal.IsOnDemand = true` hides cron fields | ✅ PASS — `@if (!IsOnDemand)` wraps schedule dropdown + custom cron field |
| `ScheduleType = "on_demand"` set in create path | ✅ PASS — line 126 |
| History expandable row closure-safe (`var localRun = context`) | ✅ PASS — both RowTemplate (line 204) and ChildRowContent (line 243) capture local variable |
| No hardcoded color/px/rem in .razor files | ✅ PASS — only `var(--mud-palette-text-secondary)` CSS token (acceptable) and `white-space: pre-wrap` |

---

## Summary

All three critical ACs pass cleanly. ISSUE-1 (cron corruption on on-demand edit) and ISSUE-2 (pagination logic) require fixes before merge.

**Fix targets:**
1. `TaskEditModal.razor:141` — `CronExpression = IsOnDemand ? null : cron`
2. `Tasks.razor:259` — track `_lastHistoryPageCount` and compare against `HistoryPageSize`

ISSUE-3 and ISSUE-4 are low-priority and can be deferred.

---

*Review by Clint Barton (Hawkeye) — code-reviewer*  
*Date: 2026-05-10*
