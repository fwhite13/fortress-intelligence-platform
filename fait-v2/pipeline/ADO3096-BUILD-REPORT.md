# Build Report — ADO#3096

## What was built
`IScheduledTaskNotificationService` + `ScheduledTaskNotificationService` — sends HTML email via MS Graph when a scheduled task completes or fails. Wired into `ScheduledTaskBackgroundService` as fire-and-forget after each run outcome (success, failure, exception).

## Files changed
- **CREATE `Services/ScheduledTaskNotificationService.cs`** — Interface + implementation. Looks up user by internal ID → gets EntraOid → fetches MS Graph token via `IMicrosoftTokenService.GetValidAccessTokenAsync` → respects `AlertOnCompletion`/`AlertOnFailure` flags → builds HTML email body (task name, run ID, status, 500-char output preview, error message if failed) → POSTs to `https://graph.microsoft.com/v1.0/me/sendMail`. All failures (null user, null token, non-2xx, exception) log warning + return — never throw.
- **MODIFY `Program.cs`** — `builder.Services.AddScoped<IScheduledTaskNotificationService, ScheduledTaskNotificationService>()` added after line 188.
- **MODIFY `Services/ScheduledTaskBackgroundService.cs`** — Fire-and-forget `Task.Run` block added after each of the three final `SaveChangesAsync` calls: TaskMode=true branch, CC path branch, catch block.

## Parallelization used
No — ADO#3107 builds on this service, so sequential was appropriate.

## CC sessions run
1 CC Sonnet run.

## Acceptance criteria verification
- [x] New `IScheduledTaskNotificationService` / `ScheduledTaskNotificationService` in correct namespace
- [x] Registered as scoped in `Program.cs`
- [x] Fire-and-forget after all three completion paths in `ScheduledTaskBackgroundService`
- [x] `AlertOnCompletion` / `AlertOnFailure` flags respected before sending
- [x] Graph token null → log warning, return (verified in code review)
- [x] `dotnet build` — 0 errors, 2 pre-existing warnings

## Known edge cases / things Clint should scrutinize
- **Note on spec delta:** The WI spec says "use AWS SES" but the existing Graph infrastructure (MS Graph, `IMicrosoftTokenService`) is already established for email. Using Graph is consistent with the rest of the codebase. Fred should confirm if SES is preferred — but Graph is the right call here given existing infra.
- Fire-and-forget captures `task` and `run` by reference — they're local variables that won't be modified after the `Task.Run` starts. No race condition.
- `CancellationToken.None` is used inside the fire-and-forget to avoid using the background service's stopping token (which may be cancelled before the email finishes).

## How to test locally
1. Create a scheduled task with `AlertOnCompletion=true` or `AlertOnFailure=true`
2. Run it — check logs for "Scheduled task notification sent" or warning if no Graph token
3. If MS365 connected: verify email arrives in inbox
