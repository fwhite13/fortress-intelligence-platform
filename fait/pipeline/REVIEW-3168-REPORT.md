# Review Report — ADO#3168

### Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

**Task:** ADO#3168 — `scheduled_tasks` + `scheduled_task_runs` EF migration SQL  
**Commit:** `b6b251ae`  
**Migration:** `20260510040449_AddScheduledTasksAndRuns`

---

## SQL Acceptance Criteria — Explicit Verdicts

### AC 1 — ENUM values ✅ PASS
- `ScheduleType`: `ENUM('recurring','on_demand')` ✅ exact match
- `LastRunStatus`: `ENUM('success','failed','cancelled')` ✅ exact match
- `scheduled_task_runs.Status`: `ENUM('success','failed','cancelled')` ✅ exact match

### AC 2 — Nullability ✅ PASS
- `LastRunStatus`: `NULL` ✅ (nullable — correct for never-run state)
- `ProjectId`: `NULL` ✅
- `CronExpression`: `NULL` ✅
- `CompletedAt`: `NULL` ✅
- All other columns correctly non-null per spec ✅

### AC 3 — Defaults ✅ PASS
- `FailureCount DEFAULT 0` ✅
- `AlertOnCompletion DEFAULT FALSE` ✅
- `AlertOnFailure DEFAULT TRUE` ✅
- `IsActive DEFAULT TRUE` ✅
- `TaskMode DEFAULT FALSE` ✅
- `CreatedAt DEFAULT CURRENT_TIMESTAMP(6)` ✅
- `UpdatedAt DEFAULT CURRENT_TIMESTAMP(6)` ✅

### AC 4 — FK: scheduled_task_runs.TaskId → scheduled_tasks.Id (CASCADE DELETE) ✅ PASS
```sql
CONSTRAINT `FK_scheduled_task_runs_scheduled_tasks_TaskId` 
  FOREIGN KEY (`TaskId`) REFERENCES `scheduled_tasks` (`Id`) ON DELETE CASCADE
```
✅ Correct table, correct column, CASCADE confirmed.

### AC 5 — FK: scheduled_tasks.UserId → users.Id (CASCADE DELETE) ✅ PASS
```sql
CONSTRAINT `FK_scheduled_tasks_users_UserId` 
  FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE
```
✅ Correct.

### AC 6 — FK: scheduled_tasks.ProjectId → projects.Id (nullable, SET NULL on delete) ✅ PASS
```sql
CONSTRAINT `FK_scheduled_tasks_projects_ProjectId` 
  FOREIGN KEY (`ProjectId`) REFERENCES `projects` (`Id`) ON DELETE SET NULL
```
✅ SET NULL confirmed. `ProjectId` is nullable (`NULL` in DDL). Correct.

### AC 7 — No existing tables modified ✅ PASS
The migration block for `20260510040449_AddScheduledTasksAndRuns` contains only:
- `CREATE TABLE scheduled_tasks`
- `CREATE TABLE scheduled_task_runs`
- 6 `CREATE INDEX` statements on the new tables
- `INSERT INTO __EFMigrationsHistory`

No `ALTER TABLE`, no modifications to any pre-existing table. ✅

### AC 8 — Idempotent guards ✅ PASS
Every statement in the new migration block is wrapped in a `MigrationsScript()` procedure with:
```sql
IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20260510040449_AddScheduledTasksAndRuns') THEN
```
All 9 procedures (table creates + 6 indexes + history insert) are guarded. ✅

### AC 9 — Column types match spec ✅ PASS
- `Id`, `UserId`, `ProjectId`, `TaskId`: `char(36) COLLATE ascii_general_ci` ✅
- `Name`: `varchar(200) CHARACTER SET utf8mb4` ✅
- `Prompt`, `Error`: `TEXT CHARACTER SET utf8mb4` ✅
- `CronExpression`: `varchar(100)` ✅
- `ResultSummary`, `ArtifactBlobPath`: `varchar(500)` ✅
- `SandboxId`: `varchar(200)` ✅
- `FailureCount`: `int` ✅
- `AlertOnCompletion`, `AlertOnFailure`, `IsActive`, `TaskMode`: `tinyint(1)` ✅
- `CreatedAt`, `UpdatedAt`, `StartedAt`, `CompletedAt`, `NextRunAt`, `LastRunAt`: `datetime(6)` ✅

---

## SQL Verdict: ✅ ALL 9 ACs PASS — SQL is correct and safe to run

---

## EF Entity / Code Review (CC Analysis)

### Issues Found

| Severity | File | Location | Issue | Fix |
|----------|------|----------|-------|-----|
| **Important** | `AppDbContext.cs` | OnModelCreating, ScheduledTask config | `ScheduleType` uses `.HasConversion<string>()` with no `HasColumnType(...)`. EF will model this as `varchar`, not `ENUM`. | Replace with `.HasColumnType("ENUM('recurring','on_demand')")` |
| **Important** | `AppDbContext.cs` | OnModelCreating, ScheduledTask config | `LastRunStatus` uses `.HasMaxLength(20)` with no `HasColumnType(...)`. EF models as `varchar(20)`. | Add `.HasColumnType("ENUM('success','failed','cancelled')")` |
| **Important** | `AppDbContext.cs` | OnModelCreating, ScheduledTaskRun config | `Status` uses `.HasMaxLength(20).IsRequired()` with no `HasColumnType(...)`. EF models as `varchar(20)`. | Add `.HasColumnType("ENUM('success','failed','cancelled')")` |

### Why This Matters

The migration `.cs` file and snapshot both have the correct `ENUM(...)` column types (Tony manually patched them). However, EF builds its "current model" from `OnModelCreating` — not from the snapshot or the `.cs` file. When EF diffs the current model against the snapshot for the **next** `dotnet ef migrations add`, it will see `varchar` in the current model and `ENUM(...)` in the snapshot, and generate spurious `ALTER TABLE` statements reverting all three ENUM columns to varchar.

**The SQL migration itself is safe to run today.** This bug will surface when the next migration is added.

### Remaining Entity Checks ✅

| Check | Result |
|-------|--------|
| ScheduledTask has all 17 required fields | ✅ Pass |
| ScheduledTaskRun has all 9 required fields | ✅ Pass |
| Navigation properties correct (Runs: List<ScheduledTaskRun>, Task: ScheduledTask) | ✅ Pass |
| AppDbContext has both DbSets | ✅ Pass |
| Snapshot updated with both entities | ✅ Pass |
| No changes to existing entities/tables | ✅ Pass |
| Build 0 errors | ✅ Pass |

---

## Consistency Audit

- SQL ENUM values verified against EF entity model: all match ✅
- FK cascade behaviors in SQL match EF `OnDelete()` config in snapshot ✅
- `char(36)` PK/FK types consistent across both tables and the parent tables (`users`, `projects`) ✅

---

## What Tony Needs to Fix

**In `AppDbContext.cs`, `OnModelCreating`:**

```csharp
// ScheduledTask — replace the current ScheduleType and LastRunStatus config:
entity.Property(e => e.ScheduleType)
    .HasMaxLength(20)
    .IsRequired()
    .HasColumnType("ENUM('recurring','on_demand')");
// Remove: .HasConversion<string>() — ScheduleType is already string, no-op conversion

entity.Property(e => e.LastRunStatus)
    .HasMaxLength(20)
    .HasColumnType("ENUM('success','failed','cancelled')");

// ScheduledTaskRun — add HasColumnType to Status:
entity.Property(e => e.Status)
    .HasMaxLength(20)
    .IsRequired()
    .HasColumnType("ENUM('success','failed','cancelled')");
```

**After fixing**, Tony should run `dotnet ef migrations add TestEnumSync --no-build` (or dry-run equivalent) to confirm no spurious ENUM→varchar alters are generated, then discard the test migration.

---

## Migration SQL: APPROVED TO RUN

The SQL file is correct on all 9 ACs. **Rhodey can run the migration against `fait_dev` now.** The `OnModelCreating` fix is a follow-up for Tony before the next migration is added — it does not block SQL execution.

---

_Reviewed by Clint Barton (Hawkeye) — 2026-05-10_
