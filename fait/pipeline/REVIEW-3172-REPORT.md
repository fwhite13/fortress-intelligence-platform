# Review Report: ADO#3172 — 3.3-A: /tasks page scaffold + Recurring tab

## CC Invocation
```bash
cat /tmp/clint-review-brief-3172.md | claude --model sonnet --print --dangerously-skip-permissions
```
Working directory: `/home/fredw/projects/fip/fait`

Files read by CC: `MainLayout.razor`, `Tasks.razor`, `TaskEditModal.razor`

---

## Verdict: PASS

All functional acceptance criteria pass. Implementation is correct, safe, and properly layered.

---

## AC Verification

1. **Tasks nav entry: PASS** — `MainLayout.razor:53` `<MudNavLink Href="/tasks" ... Icon="@Icons.Material.Filled.Schedule">Tasks</MudNavLink>`

2. **Three tabs render: PASS** — `Tasks.razor:26` Recurring tab, `Tasks.razor:111` On-Demand stub, `Tasks.razor:115` History stub — all three present

3. **Recurring tab scoped to Session.UserId: PASS** — `Tasks.razor:142`: `var all = await TaskSvc.GetTasksAsync(Session.UserId);` — `Session` is `@inject UserSessionService Session`, sourced from authenticated circuit only. No route/query params on `@page "/tasks"`. No alternate userId injection path exists.

4. **Row shows all required fields: PASS** — Name (`Tasks.razor:55`), human-readable schedule via `FormatCron()` (`Tasks.razor:56`), Last Run (`Tasks.razor:58`), Next Run (`Tasks.razor:61`), Status badge Active/Paused/Failed (`Tasks.razor:64–75`), Last Result badge Success/Failed/Never run (`Tasks.razor:78–89`)

5. **Pause/Resume optimistic update: PASS** — `Tasks.razor:182–199` calls `PauseAsync`/`ResumeAsync` on the service, then `task.IsActive = !task.IsActive` followed by `StateHasChanged()`. Badge updates without reload.

6. **Delete confirmation dialog: PASS** — `Tasks.razor:201–211` calls `DialogService.ShowMessageBox(...)` with Yes/Cancel before any `DeleteTaskAsync` call

7. **Create modal all fields present: PASS** — `IDialogService.ShowAsync<TaskEditModal>()` at `Tasks.razor:158` (create) and `Tasks.razor:173` (edit). `TaskEditModal.razor:6` root is `<MudDialog>` — no `@if (_isOpen)` wrapper. Fields confirmed: Name, Prompt, cron preset selector, custom cron input, Task Mode toggle, Alert on completion toggle, Alert on failure toggle.

8. **Cron input hidden unless Custom: PASS** — `TaskEditModal.razor:27–31`: `@if (_cronPreset == "custom") { <MudTextField ... /> }` — bound to MudSelect including `<MudSelectItem Value="@("custom")">Custom</MudSelectItem>`

9. **Alert toggle defaults: PASS** — `TaskEditModal.razor:60–61`: `private bool _alertOnCompletion = false;` / `private bool _alertOnFailure = true;` — correct

10. **New task in list immediately: PASS** — `Tasks.razor:160–164`: after modal close, calls `await LoadRecurringTasksAsync()` + `StateHasChanged()` — new task appears immediately via service re-fetch, no page reload

11. **Empty state: PASS** — `Tasks.razor:33–38`: "No recurring tasks yet. Create one to get started." shown when `_recurringTasks == null || _recurringTasks.Count == 0`

12. **Service layer only (no raw DB): PASS** — `TaskEditModal.razor` injects only `UserSessionService` and `IScheduledTaskService`. Create: `TaskSvc.CreateTaskAsync(Session.UserId, dto)` at line 128. Update: `TaskSvc.UpdateTaskAsync(ExistingTask.Id, Session.UserId, dto)` at line 142. Zero EF/DbContext symbols in file.

13. **CSS variable rule: FAIL (pre-existing violations in MainLayout.razor; new files are clean)** — `Tasks.razor` and `TaskEditModal.razor` are clean. `MainLayout.razor` has pre-existing violations (not introduced by this commit):
    - Line 39: `color: rgba(248,250,252,0.8)` — hardcoded color
    - Lines 33, 34, 39, 41, 69: hardcoded pixel/numeric values in inline styles

---

## Issues Found

### Nitpick

- **`MainLayout.razor:39` — Hardcoded color (pre-existing):** `rgba(248,250,252,0.8)` should be `var(--color-sidebar-text)` or equivalent CSS variable. *Not introduced by this PR.*

- **`MainLayout.razor:33,34,39,41,69` — Hardcoded px values (pre-existing):** `padding: 12px 16px`, `gap: 8px`, `font-size: 14px`, `margin-top: 2px`, `padding-top: 80px !important` should use CSS custom properties or MudBlazor spacing utilities. *Not introduced by this PR.*

- **`Tasks.razor:143` — String literal ScheduleType comparison:** `ScheduleType == "recurring"` — minor fragility if the value changes. Consider a typed constant or enum if `ScheduleType` is already an enum on the model.

---

## Notes

All critical checks pass:
- **AC#3**: Task list is unambiguously scoped to `Session.UserId` from the authenticated circuit. No URL/query param injection path.
- **AC#5**: Optimistic update is complete — service call, local flip, `StateHasChanged()` all present.
- **AC#7**: Correct MudDialog pattern — `IDialogService.ShowAsync<TaskEditModal>()` with no `@if` wrapper on the dialog component.
- **AC#12**: Service interface only. Zero raw DB access in the modal.

The AC#13 CSS violations are pre-existing in `MainLayout.razor` and were not introduced by commit `2c1c894a`. New files (`Tasks.razor`, `TaskEditModal.razor`) are fully compliant with the CSS variable rule.

**Recommendation:** Ship. AC#13 pre-existing violations can be addressed in a dedicated CSS cleanup ticket.
