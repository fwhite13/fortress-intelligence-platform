# DEPLOY-47-REPORT.md — fait-v2:47 Schema Consolidation Deploy

**Date:** 2026-05-09  
**Deployed by:** Rhodey (War Machine) — DevOps subagent  
**Approved by:** Clint (Hawkeye)  
**ADO:** [#3123](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/3123)

---

## What Was Deployed

Schema consolidation — fait-v2 now points at `fait_dev` (shared v1 database) instead of the standalone `fait_v2_dev`.

**Commit:** `c4660a65` — "chore: resolve ADO3123-SCHEMA-REPORT.md merge conflict"  
**Branch:** `origin/main`

---

## Step-by-Step Results

### Step 1 — Docker Build ✅
- Built `fait-v2:c4660a65` using `Dockerfile.debian` from monorepo root
- `--no-cache` flag used
- Build: **SUCCESS** (warnings only, no errors)

### Step 2 — ECR Push ✅
- Image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:c4660a65`
- Digest: `sha256:78cf251704541fa605fd364fa2e8754dc670eea07d6e6705c4d6d95f160274df`
- Push: **SUCCESS**

### Step 3 — EF Migration (fait_dev) ✅

**Pre-migration state:**
- `__EFMigrationsHistory` was empty (schema was synced from fait_v2_dev, not migrated)
- All v1 columns already present on `users` table

**Migration run:** `dotnet ef database update --context FaitV2DbContext`  
**Result:** `20260509000000_FaitDevConsolidation` recorded in `__EFMigrationsHistory`

**Critical checks PASSED:**
| Column | Present? |
|--------|----------|
| `PasswordHash` | ✅ |
| `Role` | ✅ |
| `is_active` | ✅ |
| `is_entra_user` | ✅ |
| `CreatedAt` | ✅ |

- **User count:** 13 users intact
- **New tables added:** agent_plugins, conversation_tasks, design_agent_artifacts, design_agent_sessions, feedback_submissions, main_assistants, memory_topics, pushed_messages, scheduled_task_approvals, scheduled_task_runs, scheduled_tasks, user_sessions

### Step 4 — Task Definition Registration ✅
- Cloned from `fait-v2:45`
- Changed `FORTRESS_DB_NAME`: `fait_v2_dev` → `fait_dev`
- Updated image to `c4660a65`
- Removed ECS-managed fields
- `taskRoleArn` preserved: `arn:aws:iam::742932328420:role/fait-v2-task-role`
- **Registered as:** `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:47`

### Step 5 — ECS Deploy ✅
- `aws ecs update-service --task-definition fait-v2:47 --force-new-deployment`
- `aws ecs wait services-stable` — **STABLE**

### Step 6 — Startup Log Verification ✅
```
[17:39:17 INF] Running EF Core migrations...
[17:39:20 INF] EF Core migrations complete.
```
- No migration errors
- No crash loops
- No column errors
- Services seeded and running

### Step 7 — v1 Verification ✅
- Checked `/ecs/fred-dev` CloudWatch logs
- Errors found: `HttpErrorResponseException` (AWS SDK — unrelated to DB, pre-existing)
- **No column errors from our migration**
- v1 clean ✅

### Step 8 — ADO Update ✅
- ADO #3123 state: **Closed**
- Comment added with full deployment details

---

## Summary

| Item | Result |
|------|--------|
| Migration applied cleanly | ✅ Yes |
| v1 users table columns intact | ✅ Yes (`PasswordHash`, `Role`, `CreatedAt`, `is_active`, `is_entra_user` all present) |
| ECS health | ✅ STABLE |
| "EF Core migrations complete" in logs | ✅ Yes |
| v1 (fred-dev) unaffected | ✅ Yes |
| ADO #3123 | ✅ Closed |

---

## Rollback Plan (Not Needed)
- If migration had failed: restore `fait_dev` from `fait_dev_v1_archive`
- If ECS had failed to start: revert to `fait-v2:45`

---

_Deployed clean. fait-v2 is now running against fait_dev. The fait_v2_dev database is obsolete._
