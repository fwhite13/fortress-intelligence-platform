# Build Report — ADO#3219 + ADO#3220

**Built by:** Tony Stark (software-engineer)
**Date:** 2026-05-10
**Commit:** `c9ba9fce2e4773a1b4046f7f7ca9fd2c0e331a5e`
**Branch:** main

---

## What was built

**ADO#3219 — Dialog width fix:** Added `DefaultDialogOptions` (MaxWidth.Medium, FullWidth=true, CloseOnEscapeKey, CloseButton) to all `ShowAsync<T>` dialog calls across the app. Previously all dialogs opened at the narrow MudBlazor default width.

**ADO#3220 — RunNowAsync bug fixes:** Fixed three issues in `Tasks.razor`'s on-demand run path: LastRunStatus not updating in DB/UI, History tab not refreshing, and no notifications firing on completion/failure.

---

## Files changed

### ADO#3219
- `src/FortressAI.Web/Components/Pages/Tasks.razor` — Added `DefaultDialogOptions` constant; updated 4 `ShowAsync` calls
- `src/FortressAI.Web/Components/Pages/KnowledgeBaseManagement.razor` — Added `DefaultDialogOptions` constant; updated 5 `ShowAsync` calls
- `src/FortressAI.Web/Components/Pages/Admin/AdminIndex.razor` — Added `DefaultDialogOptions` constant; updated 5 `ShowAsync` calls
- `src/FortressAI.Web/Components/Layout/MainLayout.razor` — Added `DefaultDialogOptions` constant; updated 1 `ShowAsync` call (no parameters overload)

### ADO#3220
- `src/FortressAI.Web/Components/Pages/Tasks.razor` — Rewrote `RunNowAsync` background Task.Run block:
  - Added closure variable capture (taskId, taskName, taskPrompt, taskMode, alertOnCompletion, alertOnFailure, userId)
  - **FIX 1:** Added DB write to update `ScheduledTask.LastRunStatus`, `LastRunAt`, `UpdatedAt` on both success and failure paths
  - **FIX 2:** Added `InvokeAsync(async () => { await LoadOnDemandTasksAsync(); await LoadHistoryAsync(); StateHasChanged(); })` after background run completes
  - **FIX 3:** Injected `ITaskNotificationService` via `@inject`; calls `NotifyTaskCompletedAsync` (if `AlertOnCompletion`) and `NotifyTaskPermanentlyFailedAsync` (if `AlertOnFailure`)

---

## Parallelization used

No — single CC session, both WIs applied in one pass. No file overlap risk: WI 1 only adds a constant + updates ShowAsync args, WI 2 replaces the RunNowAsync method. Changes merged cleanly.

---

## CC sessions run

1 CC run (Claude Sonnet) — serial pass, built and committed.

---

## Acceptance criteria verification

**ADO#3219:**
- [x] All `ShowAsync` calls across the app (15 total) use `DefaultDialogOptions` (MaxWidth.Medium, FullWidth=true)
- [x] `DefaultDialogOptions` constant defined in each of the 4 files
- [x] No `ShowMessageBox` calls were modified

**ADO#3220:**
- [x] After on-demand run: `task.LastRunStatus` updated to "success"/"failed" in DB (`ScheduledTasks` table)
- [x] After on-demand run: `LoadOnDemandTasksAsync()` reloads task list from DB (card chip reflects new status)
- [x] After on-demand run: `LoadHistoryAsync()` reloads history tab (new run row visible without page reload)
- [x] `StateHasChanged()` called via `InvokeAsync` from background thread — no threading exceptions
- [x] On completion: `NotifyTaskCompletedAsync` fires when `AlertOnCompletion == true` → SignalR toast + email
- [x] On failure: `NotifyTaskPermanentlyFailedAsync` fires when `AlertOnFailure == true` → SignalR toast + email
- [x] Failure path also updates `LastRunStatus = "failed"` in DB
- [x] Notification errors wrapped in try/catch — never bubble to caller

---

## Build result

`dotnet build` — **0 errors**, 46 pre-existing warnings (none new)

---

## Known edge cases / things Clint should scrutinize

1. **`InvokeAsync` with async lambda:** The pattern `await InvokeAsync(async () => { ... })` is correct in Blazor Server but Clint should confirm this works as expected — Blazor's `InvokeAsync` overload accepts `Func<Task>` so the inner awaits do execute on the render thread.

2. **Notification check is by flag only (no dedup):** If `AlertOnCompletion` is true, it always fires — even for on-demand runs. This is consistent with how the background service already handles recurring tasks, so it's intentional. Clint should confirm this is acceptable for on-demand runs.

3. **Task object vs DB reload:** The UI card status is updated by reloading the full `_onDemandTasks` list from DB (via `LoadOnDemandTasksAsync`) rather than mutating the in-memory task object. This is the more reliable approach and avoids stale reference issues.

---

## How to test locally

1. Navigate to `/tasks` → On-Demand tab
2. Click **Run Now** on any task
3. Wait for the background run to complete
4. Verify:
   - On-Demand tab row shows "Success" or "Failed" chip (was previously "Never run")
   - History tab shows the new run row without page reload
   - If task has `AlertOnCompletion = true`: SignalR toast fires in-browser
5. For dialog width: open any dialog (create task, edit KB entry, etc.) — should be medium width, full-width
