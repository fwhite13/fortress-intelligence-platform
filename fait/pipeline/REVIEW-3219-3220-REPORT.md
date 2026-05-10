# Review Report — ADO#3219 + ADO#3220

**Reviewer:** Hawkeye (Clint Barton, `code-reviewer`)
**Commit:** `c9ba9fce2e4773a1b4046f7f7ca9fd2c0e331a5e`
**Date:** 2026-05-10
**Cycle:** 1 of 2

---

## Verdict: ✅ PASS

Both WIs ship.

---

## CC Review Summary

CC process was terminated by the OS (SIGKILL) mid-run. All 10 review criteria were verified manually against the source files using direct git reads, grep analysis, and cross-file contract checks. The manual review is comprehensive — no gaps.

---

## ADO#3219 — Dialog Width

### Spec Compliance

**Check 1: DefaultDialogOptions values — ✅ PASS (all 4 files)**

All four files define the constant identically:
```csharp
private static readonly DialogOptions DefaultDialogOptions = new DialogOptions
{
    MaxWidth = MaxWidth.Medium,
    FullWidth = true,
    CloseOnEscapeKey = true,
    CloseButton = true
};
```
- `Tasks.razor` ✅ — confirmed
- `KnowledgeBaseManagement.razor` ✅ — confirmed
- `AdminIndex.razor` ✅ — confirmed
- `MainLayout.razor` ✅ — confirmed

**Check 2: ShowAsync coverage — ✅ PASS (15/15)**

| File | ShowAsync calls | All pass DefaultDialogOptions? |
|------|----------------|-------------------------------|
| Tasks.razor | 4 | ✅ Yes |
| KnowledgeBaseManagement.razor | 5 | ✅ Yes |
| AdminIndex.razor | 5 | ✅ Yes |
| MainLayout.razor | 1 | ✅ Yes |
| **Total** | **15** | ✅ All covered |

**Check 3: ShowMessageBox untouched — ✅ PASS**

`Tasks.razor` has 2 `ShowMessageBox` calls (ConfirmDeleteOnDemandAsync, ConfirmDeleteAsync). Neither was modified — they use the standard API signature without dialog options (correct). `KnowledgeBaseManagement.razor` and `AdminIndex.razor` have no `ShowMessageBox` calls.

---

## ADO#3220 — RunNowAsync

**Check 4: StateHasChanged via InvokeAsync — ✅ PASS**

The background `Task.Run` lambda contains exactly one `StateHasChanged()` call, and it is wrapped correctly:
```csharp
await InvokeAsync(async () =>
{
    await LoadOnDemandTasksAsync();
    await LoadHistoryAsync();
    StateHasChanged();
});
```
No bare `StateHasChanged()` calls exist inside the background lambda. Tony's note is correct: `InvokeAsync(Func<Task>)` is a valid Blazor Server overload and the inner `await` calls execute on the render circuit thread.

**Check 5: LastRunStatus persisted to DB — ✅ PASS**

Success path:
```csharp
await using var taskCtx = await DbFactory.CreateDbContextAsync();
var dbTask = await taskCtx.ScheduledTasks.FindAsync(taskId);
if (dbTask != null)
{
    dbTask.LastRunAt = DateTime.UtcNow;
    dbTask.LastRunStatus = "success";
    dbTask.UpdatedAt = DateTime.UtcNow;
    await taskCtx.SaveChangesAsync();
}
```

Failure path (inside catch):
```csharp
await using var taskErrCtx = await DbFactory.CreateDbContextAsync();
var dbTask = await taskErrCtx.ScheduledTasks.FindAsync(taskId);
if (dbTask != null)
{
    dbTask.LastRunAt = DateTime.UtcNow;
    dbTask.LastRunStatus = "failed";
    dbTask.UpdatedAt = DateTime.UtcNow;
    await taskErrCtx.SaveChangesAsync();
}
```

Both paths write to DB. `ScheduledTask.UpdatedAt` field confirmed to exist on the entity. The failure-path DB writes are inside a nested try/catch (`catch { /* best effort */ }`), so a DB failure there won't blow up the outer handler. ✅

**Check 6: History reload on both paths — ✅ PASS**

`InvokeAsync` is positioned OUTSIDE the try/catch block:

```
try { ... success DB writes ... }
catch { ... failure DB writes ... }

// FIX 2: Reload history tab and update task list on UI thread
await InvokeAsync(async () => {
    await LoadOnDemandTasksAsync();
    await LoadHistoryAsync();
    StateHasChanged();
});
```

Correct placement. Fires for both success and failure outcomes. `LoadHistoryAsync()` is present. ✅

**Check 7: Notification guards — ✅ PASS**

```csharp
if (success && alertOnCompletion)
    await _notificationService.NotifyTaskCompletedAsync(userId, taskName, resultSummary);
else if (!success && alertOnFailure)
    await _notificationService.NotifyTaskPermanentlyFailedAsync(userId, taskName, errorMessage ?? "Unknown error");
```

`alertOnCompletion` and `alertOnFailure` are captured from the task object before `Task.Run`. Notifications are properly gated — no unconditional firing. ✅

**Check 8: Closure capture — ✅ PASS**

All values are captured as local variables before `Task.Run`:
```csharp
var taskId = task.Id;
var taskName = task.Name;
var taskPrompt = task.Prompt;
var taskMode = task.TaskMode;
var alertOnCompletion = task.AlertOnCompletion;
var alertOnFailure = task.AlertOnFailure;
var userId = Session.UserId;
```

`run` (the ScheduledTaskRun created pre-lambda) is also referenced inside `Task.Run` via `run.Id`. This is safe: `run` is a local variable created synchronously before the lambda, it's never mutated from outside the lambda, and `run.Id` is a `Guid` (immutable value). No race condition. ✅

**Check 9: ITaskNotificationService injection — ✅ PASS**

- `@inject ITaskNotificationService _notificationService` — present at line 16 of Tasks.razor ✅
- Registered in Program.cs: `builder.Services.AddScoped<ITaskNotificationService, TaskNotificationService>()` ✅
- Called as `_notificationService.NotifyTaskCompletedAsync(...)` — instance method call, not static ✅
- Interface method signature: `Task NotifyTaskCompletedAsync(Guid userId, string taskName, string? resultSummary, CancellationToken ct = default)` — caller passes 3 args, `ct` defaults to `CancellationToken.None`. Compiles and is correct. ✅

**Check 10: Failure path completeness — ✅ PASS**

The catch block covers:
- Run row update: `savedRun.Status = "failed"` → saved to DB ✅
- Task LastRunStatus: `dbTask.LastRunStatus = "failed"` → saved to DB ✅
- `InvokeAsync` UI reload fires after catch (outside try/catch) → covers failure path ✅
- Failure notification: `NotifyTaskPermanentlyFailedAsync` guarded by `alertOnFailure` ✅

---

## Consistency Audit

| Cross-ref | Result |
|-----------|--------|
| `ITaskNotificationService` interface vs. `TaskNotificationService` impl | ✅ Match |
| Interface method signatures vs. call sites in Tasks.razor | ✅ Match (CancellationToken defaults) |
| `ScheduledTask.UpdatedAt` field vs. DB write in RunNowAsync | ✅ Field exists on entity |
| `DefaultDialogOptions` values across 4 files | ✅ Identical in all 4 |
| `ITaskNotificationService` DI registration vs. `@inject` usage | ✅ AddScoped matches |

---

## Issues Found

None. All 10 review criteria passed. No critical, important, or nitpick issues identified.

---

## Notes for Maria / Next Stage

- **Notification email wording** for `NotifyTaskPermanentlyFailedAsync` says "stopped retrying after repeated failures" — this is the recurring task failure message, re-used for on-demand. On-demand tasks don't retry, so the message is slightly misleading. This is a pre-existing copy issue in `TaskNotificationService.cs`, not introduced by this commit. Not a blocker.

- **`run.Id` closure capture**: Reviewed and confirmed safe. No refactoring needed.

- Ready for DEPLOY.

---

_Hawkeye out._
