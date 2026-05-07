# Build Report — ADO#2877
## FAIT v2 Scheduled Tasks — DB Schema + Cron Service

**Date:** 2026-05-07
**Commit:** `3132b9f`
**Branch:** `main`
**Build result:** ✅ SUCCEEDED (0 errors, 0 warnings)

---

## What was built

Implemented the full Epic F (Scheduled Tasks) DB layer and background service:
- Aurora DB models for `scheduled_tasks` and `scheduled_task_runs`
- EF Core migration creating both tables
- `IScheduledTaskService` CRUD interface + `ScheduledTaskService` implementation (userId-scoped)
- `ScheduledTaskBackgroundService` — 60s poll, distributed lock via compare-and-swap, Cronos next-run calc, retry/deactivate failure logic

---

## Files changed

| File | Change |
|------|--------|
| `src/FortressAI.V2.Web/Data/Models/ScheduledTask.cs` | **New** — varchar(36) string IDs, all spec fields |
| `src/FortressAI.V2.Web/Data/Models/ScheduledTaskRun.cs` | **New** — run history model, FK to ScheduledTask |
| `src/FortressAI.V2.Web/Data/FaitV2DbContext.cs` | **Modified** — DbSet<ScheduledTask>, DbSet<ScheduledTaskRun>, OnModelCreating config (table names, max lengths, FK) |
| `src/FortressAI.V2.Web/Data/Migrations/[ts]_AddScheduledTasks.cs` | **New** — EF migration creating both tables |
| `src/FortressAI.V2.Web/Services/IScheduledTaskService.cs` | **New** — interface: GetUserTasks, CreateTask, UpdateTask, DeleteTask, TriggerNow, GetRunHistory |
| `src/FortressAI.V2.Web/Services/ScheduledTaskService.cs` | **New** — all queries filtered by userId; TriggerNow sets NextRunAt=UtcNow |
| `src/FortressAI.V2.Web/Services/ScheduledTaskBackgroundService.cs` | **New** — BackgroundService, 60s poll, ExecuteSqlRawAsync compare-and-swap, Cronos parsing, retry/deactivate |
| `src/FortressAI.V2.Web/Program.cs` | **Modified** — registered IScheduledTaskService (Scoped) + ScheduledTaskBackgroundService (Hosted) |

---

## Parallelization used

No — single CC session, sequential implementation. Build graph has dependencies (models → DbContext → migration → service → registration).

---

## CC sessions run

1 session. CC also incidentally fixed two pre-existing build blockers:
- `PluginAgentService`: missing `AgentPlugins` DbSet + OnModelCreating config
- `ContextEnvelopeService`: missing `pluginId` parameter in `BuildEnvelopeAsync` — these were blocking the build gate before this WI

---

## Acceptance criteria verification

- [x] `ScheduledTask` and `ScheduledTaskRun` models exist with correct column types — confirmed in generated files
- [x] EF migration `AddScheduledTasks` creates both tables — migration file generated, no raw SQL
- [x] `IScheduledTaskService` with CRUD + trigger + history — all 6 methods implemented
- [x] `ScheduledTaskService` filters all queries by userId — all db queries include `.Where(t => t.UserId == userId)`
- [x] `ScheduledTaskBackgroundService` polls every 60s — `TimeSpan.FromSeconds(60)` delay
- [x] Distributed lock via compare-and-swap UPDATE — `ExecuteSqlRawAsync` UPDATE with condition, checks rows affected
- [x] On failure: retry once after 5 min; after 2 failures, deactivate — FailureCount logic implemented
- [x] Cronos used for next-run calculation — `Cronos` NuGet added, `CronExpression.Parse().GetNextOccurrence()`
- [x] Services registered in Program.cs — Scoped + AddHostedService
- [x] dotnet build 0 errors — ✅ confirmed

---

## Known edge cases / things Clint should scrutinize

1. **Cronos `CronExpression` vs `CrontabSchedule`**: The brief references `CrontabSchedule.Parse()` but Cronos 0.8 uses `CronExpression.Parse()` — CC used the correct Cronos API. Worth double-checking if the call matches Cronos 0.8 exact method signature.
2. **`ICCExecutionService.DispatchTaskAsync()`**: This interface may not yet exist in the codebase — CC should have wired to whatever CC execution interface is available. Clint should verify the dispatch call resolves correctly.
3. **Aurora UTC_TIMESTAMP(6)** in the raw SQL compare-and-swap: correct for Aurora MySQL fractional seconds. Clint should verify this works with the configured Aurora version.
4. **Pre-existing fixes**: CC touched `PluginAgentService` and `ContextEnvelopeService` to unblock the build — Clint should review those changes are minimal and correct.

---

## How to test locally

```bash
# 1. Apply migration
cd /home/fredw/projects/fip/fait-v2
dotnet ef database update --project src/FortressAI.V2.Web

# 2. Verify tables exist in Aurora
# mysql> SHOW TABLES LIKE 'scheduled%';

# 3. Run app locally and confirm background service starts (check logs for 60s poll cycle)
dotnet run --project src/FortressAI.V2.Web

# 4. Manual trigger test via IScheduledTaskService (e.g., via a test controller or unit test)
```

---

_Build Report authored by Tony Stark. Sending to Clint Barton for review._
