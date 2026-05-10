# Build Report: ADO#3171

## CC Invocation
```
cat /tmp/tony-brief-3171.md | claude --model sonnet --print --dangerously-skip-permissions
```
(Run from `/home/fredw/projects/fip/fait`)

## Commit
`04d334149e22726bd01d50c04f108bb035c2a2a7`
Message: `feat(fait#3171): Slack DM notifications on scheduled task completion/failure`

## Files Modified

### Created
- `src/FortressAI.Web/Services/ISlackNotificationService.cs` — interface with `SendDmAsync(string userEmail, string message)`
- `src/FortressAI.Web/Services/SlackNotificationService.cs` — Slack API implementation

### Modified
- `src/FortressAI.Web/Services/ScheduledTaskBackgroundService.cs` — notification block after `SaveChangesAsync` + `LoadUserEmailAsync` helper
- `src/FortressAI.Web/Program.cs` — registered `AddScoped<ISlackNotificationService, SlackNotificationService>()` + `AddHttpClient("slack")` with 10s timeout

## Build Result
```
Build succeeded.
    0 Error(s)
    32 Warning(s) — all pre-existing, none related to this change
```

## Acceptance Criteria Verification

- [x] **alert_on_completion=true → DM on success with task name + result summary**
  - Fires when `newStatus == "success" && task.AlertOnCompletion && userEmail != null`
  - Message: `✅ Scheduled task *{task.Name}* completed successfully.\n{summary}` (summary capped at 200 chars)
- [x] **alert_on_completion=false → no DM**
  - `task.AlertOnCompletion` guard prevents the call
- [x] **alert_on_failure=true → DM when failure_count=2 with task name + error + /tasks link**
  - Fires when `newStatus == "failed" && taskToUpdate?.FailureCount >= 2 && task.AlertOnFailure && userEmail != null`
  - Message includes task name, error, and `https://fait.fortressam.ai/tasks`
  - States "stopped retrying after 2 failures"
- [x] **alert_on_failure=false → no DM**
  - `task.AlertOnFailure` guard prevents the call
- [x] **Notification wrapped in try/catch — Slack failure cannot affect last_run_status or failure_count**
  - Double protection: outer `try/catch` in `ProcessTaskAsync` + `SendDmAsync` is internally fully wrapped
  - `SaveChangesAsync` has already committed before notifications are attempted
- [x] **Notification fires AFTER SaveChangesAsync — not before**
  - Notification block is placed after the `await ctx.SaveChangesAsync(ct);` call
- [x] **No new notification infra beyond ISlackNotificationService + SlackNotificationService**
  - Exactly two new service files + DI registration + named HttpClient
- [x] **No email notifications**
  - Only Slack DM via `users.lookupByEmail` + `chat.postMessage`

## Implementation Notes

### Key Design Details

1. **Double try/catch for ironclad safety**: `SendDmAsync` itself never throws (internal try/catch), plus the notification block in `ProcessTaskAsync` is also wrapped in a try/catch. `GetRequiredService<ISlackNotificationService>()` and `LoadUserEmailAsync` are both inside the outer catch.

2. **`LoadUserEmailAsync` uses its own disposable DbContext**: The `ctx` used for `SaveChangesAsync` is already disposed by the time notifications fire. The helper opens a fresh context to look up the user email — keeps concerns separated and avoids any closed-context issues.

3. **`taskToUpdate?.FailureCount >= 2` (post-increment value)**: The failure count was already incremented before `SaveChangesAsync`, so this correctly identifies the permanent-failure condition that also set `IsActive = false`.

4. **`Slack__BotToken` config key**: Reads from configuration using the double-underscore convention (maps to `Slack:BotToken` in ECS task definition env vars). If missing, logs a warning and skips all DMs silently.

5. **Named HttpClient "slack"**: Registered with 10s timeout. `SlackNotificationService` creates the client via `_httpClientFactory.CreateClient("slack")` and sets the `Authorization` header per-request.

### Clint Should Know

- No migration needed — `AlertOnCompletion` and `AlertOnFailure` were already present on the `scheduled_tasks` table from the #3169 migration.
- The Slack API calls happen in-process on the background service thread. They are awaited but fully wrapped — worst case is a 10s timeout that logs a warning and moves on.
- The `/tasks` link in failure notifications is hardcoded to `https://fait.fortressam.ai/tasks` — if the URL changes, this will need updating.
- `Slack__BotToken` must be added to the ECS task definition environment variables for notifications to fire in production.
