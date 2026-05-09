# Deploy Report: ADO#3126 Hotfix — onboarding_* ALTER TABLE Fix

**Date:** 2026-05-09  
**Deployer:** Rhodey (War Machine)  
**WI:** ADO#3126  
**Service:** `fred-dev` (fortress-tools-cluster)  
**Previous revision:** `fred-dev:127` — image `fred-chat:f243ad5a`  
**New revision:** `fred-dev:128` — image `fred-chat:59d01db4`

---

## Problem

The previous deploy (`fred-dev:127`, image `fred-chat:f243ad5a`) failed to apply two schema columns because the `ALTER TABLE` statements used `IF NOT EXISTS` syntax:

```sql
ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_completed_at DATETIME(6) NULL
ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_step INT NULL
```

`IF NOT EXISTS` is not supported in Aurora MySQL 5.7-compatible mode. The statements threw a syntax error and were silently skipped by the migration loop — leaving the columns absent from the DB.

---

## Fix

**Commit:** `59d01db4`  
**Change:** Removed `IF NOT EXISTS` from both ALTER TABLE statements.

The existing `catch` block in the `alterStatements` loop already catches MySQL error 1060 (`Duplicate column name`) and marks the migration as "already applied (idempotent)". This handles the case where the column already exists — making `IF NOT EXISTS` unnecessary.

---

## What Was Deployed

### 1. Docker Build
- Built from monorepo root: `cd /home/fredw/projects/fip && docker build --no-cache -f fait/Dockerfile -t fred-chat:59d01db4 .`
- Image digest: `sha256:9ae4e61a3576b45a75e55033e7964c7039459a30ee27f66cebc123efec7aa097`

### 2. ECR Push
- Repository: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat`
- Tag: `59d01db4`
- Digest: `sha256:9ae4e61a3576b45a75e55033e7964c7039459a30ee27f66cebc123efec7aa097`

### 3. Task Definition
- Previous: `fred-dev:127`
- New: `fred-dev:128`
- Registered via: `scripts/ecs-register-task-def.sh` (taskRoleArn preserved: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role`)
- No env var changes — hotfix only

### 4. ECS Deploy
- `aws ecs update-service --cluster fortress-tools-cluster --service fred-dev --task-definition fred-dev:128 --force-new-deployment`
- Stabilized: RUNNING=1, PENDING=0 ✅

---

## Verification

### CloudWatch Logs (`/ecs/fred-dev`)
Migration loop output for the hotfix columns:
```
fail: ... ALTER TABLE users ADD COLUMN onboarding_completed_at DATETIME(6) NULL
info: Schema migration already applied (idempotent): ALTER TABLE users ADD COLUMN onboarding_completed_at DATETIME(6) NULL

fail: ... ALTER TABLE users ADD COLUMN onboarding_step INT NULL
info: Schema migration already applied (idempotent): ALTER TABLE users ADD COLUMN onboarding_step INT NULL
```
The `fail` entries are expected — they're the 1060 catch handling the case where the columns already existed from a prior partial migration attempt. The `info: already applied (idempotent)` confirms the 1060 catch path executed correctly.

**Application started successfully:** `Now listening on: http://[::]:8080`

### Direct MySQL Verification
```
mysql> SHOW COLUMNS FROM users LIKE 'onboarding%';

+-----------------------+-------------+------+-----+---------+-------+
| Field                 | Type        | Null | Key | Default | Extra |
+-----------------------+-------------+------+-----+---------+-------+
| onboarding_completed_at | datetime(6) | YES  |     | NULL    |       |
| onboarding_step       | int         | YES  |     | NULL    |       |
+-----------------------+-------------+------+-----+---------+-------+
```
Both columns confirmed present ✅

---

## ADO Update
- Comment added to ADO#3126 (comment ID: 784289)

---

## Rollback Procedure
If needed:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:127 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```
Note: rollback to `:127` would re-expose the schema bug; only roll back if a new regression is found unrelated to the onboarding columns.

---

## Cost Impact
None — same instance type, same desired count. This is a hotfix only.

---

## Lessons Learned
- `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` is MySQL 8.0+ syntax — not available on Aurora MySQL 5.7-compat.
- The existing `alterStatements` loop already handles idempotency via error-1060 catch. `IF NOT EXISTS` was redundant and actually broke compatibility.
- Always validate syntax against the target Aurora version before using dialect-specific DDL extensions.
