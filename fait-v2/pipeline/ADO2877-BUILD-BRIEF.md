# ADO#2877 — FAIT v2 Scheduled Tasks: DB Schema + Cron Service — BUILD Brief

## Spec
`memory/projects/fait-v2-spec-2026-04-27.md §8.2`
Feature: Epic F (Scheduled Tasks)
Sprint: FAIT v2 Sprint 5

## Context
Current HEAD: `7dbe42b` on `main`. fait-v2 repo: `/home/fredw/projects/fip/fait-v2/`

The `scheduled_tasks` and `scheduled_task_runs` tables do not yet exist. This WI creates them, the EF models, and the background service that claims and dispatches due tasks.

## What to Build

### 1. Aurora DB Models

**`Data/Models/ScheduledTask.cs`**
```csharp
public class ScheduledTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string? ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string ScheduleType { get; set; } = "on_demand"; // "recurring" | "on_demand"
    public string? CronExpression { get; set; }
    public DateTime? NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public string? LastRunStatus { get; set; }  // "success" | "failed" | "cancelled" | "running"
    public int FailureCount { get; set; } = 0;
    public bool AlertOnCompletion { get; set; } = false;
    public bool AlertOnFailure { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

**`Data/Models/ScheduledTaskRun.cs`**
```csharp
public class ScheduledTaskRun
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TaskId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "running";  // "running" | "success" | "failed" | "cancelled"
    public string? ErrorMessage { get; set; }
    public string? ArtifactS3Key { get; set; }
    public string? SandboxId { get; set; }
    // Navigation
    public ScheduledTask? Task { get; set; }
}
```

### 2. FaitV2DbContext updates

Add to `Data/FaitV2DbContext.cs`:
```csharp
public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();
```

`OnModelCreating` config:
- `ScheduledTask`: table name `scheduled_tasks`, all string IDs `HasMaxLength(36)`, `ScheduleType` max 20, `LastRunStatus` max 20, `CronExpression` max 100
- `ScheduledTaskRun`: table name `scheduled_task_runs`, string IDs `HasMaxLength(36)`, `Status` max 20, FK `TaskId` → `ScheduledTask.Id`

### 3. EF Core Migration

Generate migration `AddScheduledTasks` creating both tables.
Migration must use Core API only — no raw SQL.

### 4. IScheduledTaskService + ScheduledTaskService

**`Services/IScheduledTaskService.cs`**
```csharp
public interface IScheduledTaskService
{
    Task<List<ScheduledTask>> GetUserTasksAsync(string userId, CancellationToken ct = default);
    Task<ScheduledTask> CreateTaskAsync(string userId, string name, string prompt,
        string scheduleType, string? cronExpression, CancellationToken ct = default);
    Task<ScheduledTask> UpdateTaskAsync(string taskId, string userId, string name, string prompt,
        string? cronExpression, bool isActive, CancellationToken ct = default);
    Task DeleteTaskAsync(string taskId, string userId, CancellationToken ct = default);
    Task TriggerNowAsync(string taskId, string userId, CancellationToken ct = default);
    Task<List<ScheduledTaskRun>> GetRunHistoryAsync(string taskId, string userId,
        int limit = 20, CancellationToken ct = default);
}
```

**`Services/ScheduledTaskService.cs`** — standard CRUD implementation using `FaitV2DbContext`. Filter all queries by `userId` (security — never return other users' tasks). `TriggerNowAsync` sets `NextRunAt = DateTime.UtcNow` to force immediate pickup by background service.

### 5. ScheduledTaskBackgroundService

**`Services/ScheduledTaskBackgroundService.cs`** — `BackgroundService` (singleton hosted service):

Poll interval: every 60 seconds.

For each poll cycle:
1. Query `ScheduledTasks` where `IsActive = true AND NextRunAt <= UTC_NOW AND (LastRunStatus != 'running' OR LastRunAt < UTC_NOW - 30min)`
2. For each due task — **distributed lock via compare-and-swap UPDATE** (spec §8.2):
   ```csharp
   // Claim the task atomically — if 0 rows affected, skip (another instance claimed it)
   var claimed = await _db.Database.ExecuteSqlRawAsync(
       @"UPDATE scheduled_tasks 
         SET last_run_status = 'running', last_run_at = UTC_TIMESTAMP(6)
         WHERE id = {0} AND (last_run_status != 'running' OR last_run_at < DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 30 MINUTE))",
       task.Id);
   if (claimed == 0) continue;
   ```
3. Create a `ScheduledTaskRun` record with `Status = "running"`
4. Dispatch to `ICCExecutionService.DispatchTaskAsync()` using a basic envelope (userId, task prompt as task instructions)
5. On success: update `ScheduledTaskRun.Status = "success"`, update `ScheduledTask.LastRunStatus = "success"`, `FailureCount = 0`, compute and set next `NextRunAt` from cron expression (use Cronos package)
6. On failure: update run to `"failed"`, increment `FailureCount`, set `LastRunStatus = "failed"`. If `FailureCount == 1`, schedule retry in 5 minutes (set `NextRunAt = UTC_NOW + 5min`). If `FailureCount >= 2`, set `IsActive = false` (stop retrying). Log failure.
7. Compute `NextRunAt` for recurring tasks using Cronos: `CrontabSchedule.Parse(cronExpression).GetNextOccurrence(DateTime.UtcNow)`

**NuGet package needed:** Add `Cronos` (>= 0.8) to the project for cron expression parsing.

### 6. Registration in Program.cs
```csharp
builder.Services.AddScoped<IScheduledTaskService, ScheduledTaskService>();
builder.Services.AddHostedService<ScheduledTaskBackgroundService>();
```

## Acceptance Criteria
- [ ] `ScheduledTask` and `ScheduledTaskRun` models exist with correct column types
- [ ] EF migration `AddScheduledTasks` creates both tables
- [ ] `IScheduledTaskService` with CRUD + trigger + history
- [ ] `ScheduledTaskService` filters all queries by userId
- [ ] `ScheduledTaskBackgroundService` polls every 60s
- [ ] Distributed lock via compare-and-swap UPDATE before claiming task
- [ ] On failure: retry once after 5 min; after 2 failures, deactivate
- [ ] Cronos used for next-run calculation
- [ ] Services registered in Program.cs
- [ ] dotnet build 0 errors

## Rules
- string IDs (Guid.NewGuid().ToString()) — NOT Guid type
- GuidFormat=None on all Aurora connections (already set in connection string)
- No Cognito references
- No hardcoded user IDs
- CSS variable rule N/A (no UI)

## MANDATORY: Use Claude Code CLI
```bash
CLAUDE_CODE_ENTRYPOINT=ado-pipeline \
CLAUDE_CODE_DISABLE_AUTO_MEMORY=1 \
CLAUDE_BASH_MAINTAIN_PROJECT_WORKING_DIR=1 \
CLAUDE_CODE_GLOB_TIMEOUT_SECONDS=30 \
cat /home/fredw/projects/fip/fait-v2/pipeline/ADO2877-BUILD-BRIEF.md | \
claude --model sonnet --print --dangerously-skip-permissions
```

## ADO Comment (add after build)
Project: Fortress, ID: 2877
```
**[Tony Stark — BUILD cycle 1]**
Commit {hash}: ScheduledTask + ScheduledTaskRun models, EF migration AddScheduledTasks, IScheduledTaskService + impl, ScheduledTaskBackgroundService with distributed lock + Cronos. Build: SUCCEEDED.
```
