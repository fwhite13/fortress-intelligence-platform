# Build Report — ADO#3168

## What was built
Created `ScheduledTask` and `ScheduledTaskRun` EF Core entities, registered them in `AppDbContext` with full Fluent API configuration, generated EF migration `20260510040449_AddScheduledTasksAndRuns` with ENUM column types manually patched, and extracted idempotent SQL to `pipeline/MIGRATION-3168-SQL.sql`.

---

## Files Changed

### New Files
- `src/FortressAI.Shared/Models/ScheduledTask.cs` — new entity with all specified fields and navigation properties
- `src/FortressAI.Shared/Models/ScheduledTaskRun.cs` — new entity with all specified fields and navigation property
- `src/FortressAI.Web/Migrations/20260510040449_AddScheduledTasksAndRuns.cs` — EF migration (ENUM types patched)
- `src/FortressAI.Web/Migrations/20260510040449_AddScheduledTasksAndRuns.Designer.cs` — auto-generated designer file
- `pipeline/MIGRATION-3168-SQL.sql` — idempotent MySQL SQL (2828 lines, all migrations)

### Modified Files
- `src/FortressAI.Web/Data/AppDbContext.cs` — added `ScheduledTasks` and `ScheduledTaskRuns` DbSets + full `OnModelCreating` configurations for both entities
- `src/FortressAI.Web/Migrations/AppDbContextModelSnapshot.cs` — updated to include new entities with ENUM column types

---

## Parallelization Used
No — single CC session, sequential execution required (entity files → AppDbContext edits → migration generation → ENUM patch → SQL extraction → build verify).

---

## CC Sessions Run
1 CC session (Claude Sonnet). Brief written to `/tmp/brief-3168.md`, piped to CC.

---

## Acceptance Criteria Verification

- [x] `ScheduledTask` entity has all specified fields with correct types — verified by reading `ScheduledTask.cs`
- [x] `ScheduledTaskRun` entity has all specified fields with correct types — verified by reading `ScheduledTaskRun.cs`
- [x] Navigation properties correct: `ScheduledTask.Runs` ↔ `ScheduledTaskRun.Task` — FK configured both sides
- [x] FK: `scheduled_tasks.user_id → users.id` (cascade delete) — confirmed in migration SQL
- [x] FK: `scheduled_tasks.project_id → projects.id` (nullable, set null on delete) — confirmed in migration SQL
- [x] FK: `scheduled_task_runs.task_id → scheduled_tasks.id` (cascade delete) — confirmed in migration SQL
- [x] Migration `.cs` uses ENUM column types for 3 enum columns — patched post-generation:
  - `ScheduleType` → `ENUM('recurring','on_demand')`
  - `LastRunStatus` → `ENUM('success','failed','cancelled')`
  - `Status` (task_runs) → `ENUM('success','failed','cancelled')`
- [x] `AppDbContextModelSnapshot.cs` updated to match ENUM types
- [x] Raw SQL saved to `pipeline/MIGRATION-3168-SQL.sql` (2828 lines)
- [x] Build: **0 errors**, 32 pre-existing MudBlazor warnings
- [x] No changes to any existing entity or table
- [x] Migration NOT run against live DB — SQL only

---

## ENUM Columns in Migration SQL (verified)

```
Line 2679: `ScheduleType` ENUM('recurring','on_demand') CHARACTER SET utf8mb4 NOT NULL,
Line 2683: `LastRunStatus` ENUM('success','failed','cancelled') CHARACTER SET utf8mb4 NULL,
Line 2713: `Status` ENUM('success','failed','cancelled') CHARACTER SET utf8mb4 NOT NULL,
```

---

## Known Edge Cases / Things Clint Should Scrutinize

1. **ENUM patch in snapshot**: The `AppDbContextModelSnapshot.cs` ENUM type strings must exactly match the migration `.cs` file — CC patched both. Verify snapshot consistency.

2. **`ScheduledTask` naming conflict**: The class name `ScheduledTask` could shadow a .NET BCL type if any files have broad `using` directives. Build passed cleanly so this is not an issue, but worth noting.

3. **No `UpdatedAt` trigger**: `UpdatedAt` uses `HasDefaultValueSql("CURRENT_TIMESTAMP(6)")` (set on INSERT only). EF won't auto-update this on UPDATE — callers must set it manually. This matches the existing pattern in the codebase (see `Project`, `Conversation`).

4. **ProjectId → SetNull**: The FK to `projects` uses `OnDelete(DeleteBehavior.SetNull)` with `IsRequired(false)`. This requires `ProjectId` to be `Guid?` (nullable) — confirmed it is in the entity.

---

## How to Test Locally

```bash
# Verify build
cd /home/fredw/projects/fip/fait
dotnet build src/FortressAI.Web/FortressAI.Web.csproj -c Release --no-restore

# Review migration SQL before Clint approves it for DB apply
cat pipeline/MIGRATION-3168-SQL.sql | grep -A 30 "scheduled_tasks"

# When Clint approves, Rhodey runs:
# dotnet ef database update --project src/FortressAI.Web --startup-project src/FortressAI.Web
# (NOT Tony's job — hand off to Rhodey)
```

---

## Commit
`b6b251ae` — `feat(fait#3168): ScheduledTask + ScheduledTaskRun EF entities and migration`

## ADO
Comment posted on #3168 — migration SQL flagged as ready for Clint review.
