# FAIT v2 — Sprint 5 Deploy Report

**Date:** 2026-05-07
**Deployer:** War Machine (James Rhodes) — DevOps subagent
**Commit:** `987a94f` on `main`

---

## Pre-Deploy Snapshot

| Property | Value |
|---|---|
| Previous task def | `fait-v2:6` |
| Previous image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:7dbe42b` |
| ECS cluster | `fortress-tools-cluster` |
| ECS service | `fait-v2` |

---

## Build

- **Dockerfile:** `fait-v2/Dockerfile.debian` (monorepo root build context `/home/fredw/projects/fip/`)
- **Image digest:** `sha256:4d681b45c6e5f2973cbb1c86f98a1fce9dd198061dc426d68ae09daa8bcf7bb1`
- **Tags pushed to ECR:**
  - `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:987a94f`
  - `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:sprint5`

---

## Migration Results

### Method
`dotnet ef migrations script --idempotent` failed due to `AddScheduledTasks` containing stale `User`→`users` rename steps that were already applied to the DB in a prior sprint. Applied targeted idempotent SQL directly via `mysql` client instead.

### Migrations Applied

| Migration ID | Status | Notes |
|---|---|---|
| `20260507180721_AddScheduledTasks` | ✅ Applied | Created `scheduled_tasks`, `scheduled_task_runs` tables + indexes |
| `20260507180752_AddAgentPlugins` | ✅ Applied | Created `agent_plugins` table + indexes |
| `20260507210000_SeedInitialAgentPlugins` | ✅ Applied | Seeded 3 rows (Marketing, Finance, Legal) |

### New Tables Verified

| Table | Row Count |
|---|---|
| `scheduled_tasks` | 0 (empty, correct) |
| `scheduled_task_runs` | 0 (empty, correct) |
| `agent_plugins` | 3 (Marketing, Finance, Legal) |

### Seed Data (agent_plugins)

| ID | Name | Active |
|---|---|---|
| `00000000-0000-0000-0000-000000000001` | Marketing | ✅ |
| `00000000-0000-0000-0000-000000000002` | Finance | ✅ |
| `00000000-0000-0000-0000-000000000003` | Legal | ✅ |

---

## ECS Deployment

| Property | Value |
|---|---|
| New task def | `fait-v2:7` |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:7` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:987a94f` |
| Deployment status | PRIMARY — running=1, pending=0 |
| Old revision | `fait-v2:6` — DRAINING |

---

## Health Check

```
curl -sk -H "Host: fait-v2.dev.fortressam.ai" https://fortress-tools-alb-487057611.us-east-1.elb.amazonaws.com/health
```

**Result: `200 OK`** ✅

> Note: ALB redirects HTTP→HTTPS (301). Health check must use HTTPS.

---

## Rollback Plan

If issues arise, roll back to `fait-v2:6`:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --task-definition fait-v2:6 \
  --force-new-deployment \
  --region us-east-1
```

Database rollback (if needed — removes Sprint 5 tables):
```sql
DROP TABLE IF EXISTS scheduled_task_runs;
DROP TABLE IF EXISTS scheduled_tasks;
DROP TABLE IF EXISTS agent_plugins;
DELETE FROM __EFMigrationsHistory WHERE MigrationId IN (
  '20260507180721_AddScheduledTasks',
  '20260507180752_AddAgentPlugins',
  '20260507210000_SeedInitialAgentPlugins'
);
```

---

## Issues / Notes

1. **`Dockerfile.debian` location:** Brief specified `src/FortressAI.V2.Web/Dockerfile.debian` but it lives at the monorepo root `fait-v2/Dockerfile.debian`. Build was run from `/home/fredw/projects/fip/` with `-f fait-v2/Dockerfile.debian`.

2. **`ecs-register-task-def.sh` location:** Brief specified `./scripts/ecs-register-task-def.sh` relative to fait-v2 dir, but wrapper lives at `/home/fredw/projects/fip/scripts/ecs-register-task-def.sh`. Also takes `--task-def-json`, not `--image`. Task def JSON prepared manually from current revision shape.

3. **Migration script conflict:** `AddScheduledTasks` migration contains `User`→`users` table rename steps already applied in a prior sprint. This caused `mysql < sprint5-migrations.sql` to fail on FK drop. Resolved by writing a targeted idempotent script that only creates the new tables.

4. **`SeedInitialAgentPlugins` missing Designer.cs:** Migration file existed but no `.Designer.cs` companion, so it wasn't included in the EF-generated SQL. Seed was applied directly in the targeted script.

5. **Password special char `^`:** `dotnet ef database update --connection` fails when password contains `^`. Used mysql client path (Option B) as brief anticipated.

---

## Sprint 5 Changes Deployed

- **#2877:** ScheduledTask + ScheduledTaskRun models, EF migration, IScheduledTaskService, ScheduledTaskBackgroundService (60s poll, distributed CAS lock, Cronos)
- **#2878:** Tasks.razor (/tasks route, 3-tab), TaskEditDialog, ConfirmDialog, Dashboard widget, sidebar nav
- **#2879:** AgentPlugin model, IPluginAgentService, plugin-aware ContextEnvelopeService, plugin selector in ChatView
- **#2880:** marketing.md + finance.md + legal.md skills files, SeedInitialAgentPlugins migration, PluginAgentService wwwroot reader
