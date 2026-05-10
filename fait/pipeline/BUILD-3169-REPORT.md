# Build Report — ADO#3169

## What was built
`IScheduledTaskService` interface and `ScheduledTaskService` implementation — data layer CRUD for scheduled tasks. Includes DTOs, NCrontab-powered cron calculation, and full userId ownership enforcement on every method.

## Files changed
- `src/FortressAI.Shared/Models/ScheduledTaskDtos.cs` — New: `CreateScheduledTaskDto` + `UpdateScheduledTaskDto`
- `src/FortressAI.Web/Services/IScheduledTaskService.cs` — New: 8-method interface
- `src/FortressAI.Web/Services/ScheduledTaskService.cs` — New: full implementation with IDbContextFactory, NCrontab, ownership enforcement
- `src/FortressAI.Web/Program.cs` — Added `AddScoped<IScheduledTaskService, ScheduledTaskService>()` at line 108
- `src/FortressAI.Web/FortressAI.Web.csproj` — Added `NCrontab 3.4.0` package reference

## Parallelization used
No — single sequential task.

## CC sessions run
1 CC Sonnet session. All files generated in single pass.

## Acceptance criteria verification
- [x] All 8 interface methods implemented — verified in `IScheduledTaskService.cs`
- [x] EVERY method enforces userId ownership — all `FirstOrDefaultAsync` calls include `&& t.UserId == userId`
- [x] CreateTaskAsync: `next_run_at = null` for on_demand, calculated for recurring — verified
- [x] ResumeAsync: `next_run_at = CalculateNextRunAt(task.CronExpression)` from `DateTime.UtcNow` — verified
- [x] GetRunHistoryAsync: ownership check first, then `OrderByDescending(r => r.StartedAt)`, `.Take(limit)` — verified
- [x] NCrontab 3.4.0 added to .csproj — `dotnet add` confirmed
- [x] IDbContextFactory pattern used — `_dbFactory.CreateDbContextAsync()` with `await using` in every method
- [x] Registered as scoped in Program.cs — line 108
- [x] No execution/Fargate logic — data layer only
- [x] Build 0 errors — confirmed (`32 warnings, 0 errors`)

## Known edge cases / things Clint should scrutinize
- `UpdateTaskAsync`: `dto.ProjectId` uses `.HasValue` check — this means you CAN'T set ProjectId to null via Update (it would require explicit nullability handling). Acceptable for now since the DTO is `Guid?`.
- `CalculateNextRunAt` silently swallows cron parse exceptions — returns null on bad cron expression rather than surfacing validation error. This is intentional (graceful degradation) but higher layers should validate cron before calling Create/Update.
- Hard delete on `DeleteTaskAsync` — relies on EF cascade for `ScheduledTaskRun` rows. Verify cascade is configured on the `ScheduledTask → ScheduledTaskRun` FK in `AppDbContext`.

## How to test locally
```bash
cd /home/fredw/projects/fip/fait
dotnet build src/FortressAI.Web/FortressAI.Web.csproj -c Release --no-restore
# Expect: 0 errors
```
Functional testing requires ADO#3170 (ScheduledTaskController) to wire the API layer.

## Commit
`7f2fa379` — `feat(fait#3169): IScheduledTaskService interface + CRUD implementation`
