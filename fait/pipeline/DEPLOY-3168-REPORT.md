# Deploy Report: ADO#3168 — ScheduledTask + ScheduledTaskRun EF Migration + Image Deploy

**Date:** 2026-05-10  
**Deployer:** Rhodey (devops subagent)  
**Commit:** `f1815a35`  
**WI:** ADO#3168 — 3.1-A scheduled_tasks + scheduled_task_runs migration

---

## Migration Verification

### Tables Present in fait_dev
```
scheduled_task_approvals
scheduled_task_runs
scheduled_tasks
```

### Migration History (top 3)
```
20260510040449_AddScheduledTasksAndRuns  ← applied
20260510014154_AddAvatarUrlToUserAssistantConfig
20260509000000_FaitDevConsolidation
```

### Notes on Migration Application
The tables `scheduled_tasks` and `scheduled_task_runs` already existed in `fait_dev` (created manually prior to this deploy) but with:
- Snake_case column names (EF/Pomelo expects PascalCase without HasColumnName overrides)
- `varchar(20)` type for enum columns (migration specifies ENUM types)
- Different column names in `scheduled_task_runs` (`error_message`→`Error`, `output_text`→`ResultSummary`, `artifact_s3_key`→`ArtifactBlobPath`)

**Reconciliation steps performed:**
1. Converted `ScheduleType` and `LastRunStatus` in `scheduled_tasks` to `ENUM('recurring','on_demand')` and `ENUM('success','failed','cancelled')`
2. Converted `Status` in `scheduled_task_runs` to `ENUM('success','failed','cancelled')`
3. Renamed `scheduled_task_runs` columns: `error_message`→`error`, `output_text`→`result_summary`, `artifact_s3_key`→`artifact_blob_path`
4. Renamed ALL columns in both tables from snake_case to PascalCase to match EF property names (Pomelo uses property name as column name without explicit HasColumnName mapping)
5. Inserted migration history record `20260510040449_AddScheduledTasksAndRuns` into `__EFMigrationsHistory`
6. Ran `dotnet ef database update` — returned "Done" (no migrations pending)

### DESCRIBE scheduled_tasks (final)
```
Id              char(36)                        NO  PRI
UserId          char(36)                        NO  MUL
ProjectId       char(36)                        YES
Name            varchar(200)                    NO
Prompt          text                            NO
ScheduleType    enum('recurring','on_demand')   NO
CronExpression  varchar(100)                    YES
NextRunAt       datetime(6)                     YES MUL
LastRunAt       datetime(6)                     YES
LastRunStatus   enum('success','failed','cancelled') YES
FailureCount    int                             NO  DEFAULT 0
AlertOnCompletion tinyint(1)                   NO  DEFAULT 0
AlertOnFailure  tinyint(1)                      NO  DEFAULT 1
IsActive        tinyint(1)                      NO  DEFAULT 1
TaskMode        tinyint(1)                      NO  DEFAULT 0
CreatedAt       datetime(6)                     NO  CURRENT_TIMESTAMP(6)
UpdatedAt       datetime(6)                     NO  CURRENT_TIMESTAMP(6)
```

### DESCRIBE scheduled_task_runs (final)
```
Id              char(36)                              NO  PRI
TaskId          char(36)                              NO  MUL
StartedAt       datetime(6)                           NO
CompletedAt     datetime(6)                           YES
Status          enum('success','failed','cancelled')  NO
Error           text                                  YES
ArtifactBlobPath varchar(500)                        YES
SandboxId       varchar(200)                          YES
ResultSummary   varchar(500)                          YES
```

---

## Image Build

- **Image tag:** `fred-chat:f1815a35`
- **ECR URI:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:f1815a35`
- **Image digest:** `sha256:047ae4987119568da3ba5d6259f89fd9fbb958a9ef5a842b63f20b252619c11f`
- **Build flags:** `--no-cache`, `Dockerfile.debian`
- **Build status:** ✅ SUCCESS

---

## Task Definition

- **Previous:** `fred-dev:154`
- **New:** `fred-dev:155` (`arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:155`)
- **Container updated:** `fred` → `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:f1815a35`
- **taskRoleArn:** `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅
- **Fargate__ContainerName:** `fait-v2-agent-harness` ✅

---

## Service Health

```json
{
  "running": 1,
  "pending": 0,
  "deployments": [
    {
      "status": "PRIMARY",
      "running": 1,
      "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:155"
    }
  ]
}
```
**Status: HEALTHY** ✅

---

## ADO Update

- WI #3168 → **Resolved**
- Comment: "Migration applied to fait_dev: scheduled_tasks + scheduled_task_runs created with correct ENUM types. Deployed fred-chat:f1815a35, fred-dev:155. Service HEALTHY."

---

## Rollback

### ECS (image only)
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:154 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

### DB
Migration cannot be easily rolled back. The column renames/type changes applied manually. If rollback is required, columns would need manual ALTER back to snake_case + original varchar types. The `scheduled_task_approvals` table was not touched.

---

## Lessons Learned

- The existing `scheduled_tasks`/`scheduled_task_runs` tables were created manually with snake_case column names prior to this deploy. EF/Pomelo 8 uses property names as column names without an explicit `UseSnakeCaseNamingConvention` — entities without `HasColumnName` overrides expect PascalCase columns.
- When tables pre-exist with wrong schema, inserting the migration history record alone is insufficient — the table structure must match what EF expects.
- Future: avoid creating tables manually before the EF migration runs.
