# Build Report: ADO#3173

## CC Invocation
```
cat /tmp/cc-brief-3173.md | claude --model sonnet --print --dangerously-skip-permissions
```
Working directory: `/home/fredw/projects/fip/fait`

## Commit
`aa7573eb1e6b86a79e1394e2c27cfa297eef55c5`
Message: `feat(fait#3173): On-Demand tab, History tab, failed-task banner`

## Files Modified
- `src/FortressAI.Web/Components/Pages/Tasks.razor` — On-Demand tab content, History tab content, new injections (`IDbContextFactory`, `IUserAgentRuntime`), all new @code methods
- `src/FortressAI.Web/Components/Tasks/TaskEditModal.razor` — `IsOnDemand` parameter, conditional cron/schedule UI, dynamic title/button labels, ScheduleType routing in SaveAsync
- `src/FortressAI.Web/Components/Chat/ChatView.razor` — `_hasFailedTasks`/`_failedTaskBannerDismissed` state, `MudAlert` banner markup, `CheckFailedTasksAsync()` method wired into `HandleAgentReady` and `OnInitializedAsync`

## Build Result
**0 errors, 37 warnings** (all pre-existing MUD0002/CS8602 analyzer warnings — none introduced by this WI)

## Acceptance Criteria Verification
- [x] On-Demand tab shows list with name, prompt preview (100 chars), last run, last_run_status badge
- [x] Run Now creates scheduled_task_runs row (Status="running"), fires SendTurnAsync fire-and-forget, updates UI
- [x] Edit/Delete work correctly; Delete requires confirmation dialog
- [x] On-Demand empty state shown when no tasks
- [x] History tab shows flat list of all runs, ordered by started_at desc
- [x] History columns: task name, schedule type badge, started, duration, status badge
- [x] Expandable row shows error on failed, result_summary on success
- [x] History pagination (Load more / page size 50)
- [x] History empty state shown when no runs
- [x] Failed-task banner on /chat when failure_count > 0 AND is_active = true
- [x] Banner dismissible (component state only — `_failedTaskBannerDismissed` flag)
- [x] Banner links to /tasks via `<MudLink Href="/tasks">`
- [x] Banner does not render when no failed tasks (guarded by `_hasFailedTasks`)
- [x] [C1] Run Now creates scheduled_task_runs row with task_id, started_at, status=running
- [x] [C2] History query scoped to `Session.UserId` via `.Where(r => r.Task.UserId == Session.UserId)`
- [x] [C3] Banner query: `t.IsActive && t.FailureCount > 0` — both conditions required

## Implementation Notes
- `IDbContextFactory` was already injected in ChatView.razor — no duplicate added
- `HandleAgentReady()` remains `void`; `CheckFailedTasksAsync()` is fire-and-forget via `_ = CheckFailedTasksAsync()`
- `CheckFailedTasksAsync()` also called from `OnInitializedAsync` when `_agentReady = true` (non-cold-start path)
- `TaskRunHistoryItem` implemented as inner class (not record) to support mutable state
- `_expandedRunIds` is `HashSet<Guid>` — toggle pattern per brief spec
- `RunNowAsync` fire-and-forget uses `Task.Run` to avoid blocking Blazor render thread
- On-Demand task delete uses `TaskSvc.DeleteTaskAsync` (same service call as recurring tab delete)
- `IsOnDemand` in `TaskEditModal` skips cron validation entirely and sets `CronExpression = null`
