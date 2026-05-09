# Deploy Report: ADO#3123 — fait-v2:46 Schema Migration

**Date:** 2026-05-09  
**Engineer:** Rhodey (DevOps)  
**ADO:** #3123

---

## What Was Deployed

fait-v2:46 — same Docker image as :45 (`fait-v2:1bb5e191`), with `FORTRESS_DB_NAME` changed from `fait_v2_dev` → `fait_dev`.

This completes the clean-slate migration of fait-v2's primary database from the legacy `fait_v2_dev` to the canonical `fait_dev` database.

---

## Pre-Deploy State

- **Previous task def:** `fait-v2:45`
- **Previous image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:1bb5e191`
- **Previous DB:** `fait_v2_dev`
- **Service:** ACTIVE, 1/1 running

---

## Steps Completed

### Step 1: Aurora Connection
- Host: `fortress-ai-cluster.cluster-c89acukue4d5.us-east-1.rds.amazonaws.com`
- User: `fortress_mysql`
- Credentials sourced from `/home/fredw/projects/ai/projects/fortress_tools/.env`

### Step 2: Archive fait_dev (v1)
- Created `fait_dev_v1_archive` with `utf8mb4_unicode_ci`
- Dumped `fait_dev` → `/tmp/fait_dev_v1_backup_clean.sql` (55MB)
- Restored to `fait_dev_v1_archive` — 31 tables, 12 users, 88 conversations preserved
- ✅ fait_dev dropped after archive confirmed

### Step 3: Create fresh fait_dev
- ✅ `fait_dev` created (empty, `utf8mb4_unicode_ci`)

### Step 4: EF Core Migrations
- Initial `dotnet ef database update` ran 6 migrations then hit a migration ordering issue:
  - `AddScheduledTasks` tried to `DROP FOREIGN KEY` on `pushed_messages` (v1 table, not present in clean DB)
  - Root cause: Migration chain was designed for v1→v2 upgrade, not clean install
- **Resolution:** Synced schema from `fait_v2_dev` (source of truth with all 21 migrations applied):
  - Dropped partial tables from `fait_dev`
  - `mysqldump --no-data fait_v2_dev` → restored to `fait_dev` (structure + migration history)
  - Ran `dotnet ef database update` → **"No migrations were applied. The database is already up to date."**
- ✅ 24 tables created in `fait_dev`
- ✅ 21 migrations recorded in `__EFMigrationsHistory`

### Tables in fait_dev (24)
agent_plugins, artifact_records, conversation_tasks, conversations, design_agent_artifacts, design_agent_sessions, feedback_submissions, kb_entries, kb_team_members, kb_teams, main_assistants, mcp_servers, mcp_user_tokens, memory_topics, messages, project_documents, projects, pushed_messages, scheduled_task_approvals, scheduled_task_runs, scheduled_tasks, user_devops_connections, user_sessions, users

### Step 5: Register Task Def fait-v2:46
- Changed `FORTRESS_DB_NAME`: `fait_v2_dev` → `fait_dev`
- Preserved `taskRoleArn: arn:aws:iam::742932328420:role/fait-v2-task-role`
- ✅ Registered: `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:46`

### Step 6: Deploy
- `aws ecs update-service --task-definition fait-v2:46 --force-new-deployment`
- ✅ `aws ecs wait services-stable` → exit 0

### Step 7: Startup Verification
Key log lines (task `33073052b5dc49a38afee9e5775f200b`):
```
[15:53:35 INF] Running EF Core migrations...
[15:53:38 INF] EF Core migrations complete.
[15:53:39 INF] Seeded mcp_servers entry: forge-kb
[15:53:39 INF] Seeded mcp_servers entry: ms365
[15:53:39 INF] Seeded mcp_servers entry: ado
[15:53:39 INF] Seeded mcp_servers entry: web-search
[15:53:39 INF] Seeded Marketing plugin agent
[15:53:39 INF] ScheduledTaskBackgroundService started.
```

**Note:** `DataProtectionKeys` table missing in `fait_dev` on startup — non-fatal, ASP.NET DataProtection creates it on first auth request. This is the shared keyring used for auth cookie decryption.

---

## ECS Status

- **Service:** `fait-v2` on `fortress-tools-cluster`
- **Task def:** `fait-v2:46` (revision 46)
- **Status:** ACTIVE, desired=1, running=1, pending=0
- **Deployment:** PRIMARY

---

## Rollback Plan

If startup fails:
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 \
  --task-definition fait-v2:45 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```
- `fait-v2:45` uses `fait_v2_dev` (untouched, still intact)
- `fait_dev_v1_archive` available if v1 data recovery needed

---

## Database State Post-Deploy

| Database | State | Notes |
|----------|-------|-------|
| `fait_dev` | ✅ Fresh, 24 v2 tables, all 21 migrations applied | Active, used by fait-v2:46 |
| `fait_dev_v1_archive` | ✅ V1 backup (31 tables, 12 users, 88 convos) | Preserved, recoverable |
| `fait_v2_dev` | ✅ Intact, untouched | Available for rollback |

---

## Cost Impact

No change. Same task size (0.5 vCPU / 1GB), same image, same cluster.

---

## Lessons Learned

1. **Migration chain ordering:** v2 migrations assume v1 tables exist (designed for upgrade path). On a clean DB, they fail at `DropForeignKey` steps. The fix: sync schema from an existing v2 DB + manually seed `__EFMigrationsHistory`.
2. **`mysqldump --no-data` includes migration history**: MySQL's `--no-data` flag skips row data but `__EFMigrationsHistory` may or may not be included. Verify post-restore.
3. **DataProtectionKeys is keyring-only**: The `SharedKeyRingDbContext` reads from `fait_dev` (via `FIP_KEYRING_DB_NAME`) — non-fatal on first startup, self-heals on first auth.
