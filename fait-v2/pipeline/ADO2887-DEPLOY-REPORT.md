# Deploy Report — ADO#2887 — FORGE KB Integration Service
**Sprint 3 | FAIT v2 | Rhodey (War Machine) — DEPLOY**  
**Date:** 2026-05-07 09:33–10:45 EDT  
**Status:** ❌ BLOCKED — Two sequential root causes found and partially resolved; second blocker requires Maria/Fred action

---

## Summary

Deployed against three sequential blockers. Two were identified and fixed. Service is halted at desired-count=0. A third issue (DB connectivity / `fait_v2_dev` table existence) needs verification before the service can go live.

---

## Root Cause Chain

### Bug 1 — Task def :1 had `fait-v2/postgres-master` without the `-VhGYDn` suffix ✅ FIXED
**Error:** `ResourceNotFoundException: Secrets Manager can't find the specified secret`  
**Fix:** Registered task def :2 with correct full ARN `fait-v2/postgres-master-VhGYDn`  
**Note:** IAM policy on execution role correctly uses `fait-v2/*` wildcard — this was never the problem.

### Bug 2 — `ConnectionStrings__DefaultConnection` injected a Postgres DSN into a MySQL context ✅ FIXED
**Error:** `Format of the initialization string does not conform to specification starting at index 0`  
**Root cause:** Task def was mapping `fait-v2/postgres-master` (Postgres connection string) to `ConnectionStrings__DefaultConnection`, which `FaitV2DbContext` consumed via `UseMySql()`. MySqlConnector cannot parse a Postgres DSN.  
**Fix:** Updated `Program.cs` to build `FaitV2DbContext` connection string from `FORTRESS_DB_*` env vars (same pattern as `keyRingCsb`). Removed the `ConnectionStrings__DefaultConnection` secret injection from the task def entirely.  
**Commit:** `555b283` — `fix(fait-v2#2887): build FaitV2DbContext from FORTRESS_DB env vars`  
**Task def :4** registered with corrected image + no postgres secret injection.

### Bug 3 — MySQL connectivity failure on task def :4 ❌ STILL BLOCKING
**Error:** `Unable to connect to any of the specified MySQL hosts` — targeting `localhost`  
**Likely cause:** The app is failing at `Program.cs:140` which is the `forge-kb` mcp_servers seeder. The seeder calls `FaitV2DbContext` against `fait_v2_dev`. Two possible causes:
1. `fait_v2_dev` database/tables don't exist yet in Aurora (migrations never ran)
2. Security group on Aurora doesn't allow the ECS task's subnet/SG to connect on port 3306

**Evidence:** The error occurs at startup seeding (line 140 = `await seedDb.McpServers.AnyAsync(...)`) — this suggests the app DID start successfully (no crash at DI registration), but fails when it first tries to execute a query. The "localhost" in the error message is suspicious but may be a red herring in the stack trace formatting.

---

## What Was Done

| Step | Action | Result |
|------|--------|--------|
| 1 | Identified task def :1 secret ARN mismatch | ✅ |
| 2 | Registered task def :2 (correct secret ARN) | ✅ |
| 3 | Verified IAM `fait-v2-secrets-access` uses `fait-v2/*` wildcard — fine | ✅ |
| 4 | Pushed `bda1964` to origin/main (was 1 commit ahead) | ✅ |
| 5 | Found second bug: Postgres DSN → MySQL context crash | ✅ |
| 6 | Fixed `Program.cs` — `faitV2Csb` from env vars, not secret | ✅ |
| 7 | Committed `555b283`, pushed to origin/main | ✅ |
| 8 | Built Docker image `fait-v2:555b283`, pushed to ECR | ✅ |
| 9 | Registered task def :4 (`555b283` image, no postgres secret) | ✅ |
| 10 | Updated ECS service to task def :4, desired-count=1 | ✅ |
| 11 | Task starts, secrets inject OK, app partially boots | ✅ |
| 12 | App crashes at DB seeder — MySQL connection failure | ❌ |
| 13 | Halted service at desired-count=0 | ✅ |

---

## Current ECS State

| Field | Value |
|-------|-------|
| Service | `fait-v2` on `fortress-tools-cluster` |
| Task def | `fait-v2:4` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:555b283` |
| Desired | 0 (halted) |
| Running | 0 |

---

## Action Required Before Re-Deploy

### Option A — Verify `fait_v2_dev` DB exists and is accessible
1. Confirm Aurora MySQL cluster has `fait_v2_dev` database created
2. Confirm `fortress_mysql` user has access to `fait_v2_dev`
3. Confirm ECS task security group `sg-0fb53615b1eb4a175` can reach Aurora on port 3306

### Option B — If DB exists, run EF migrations first
The `McpServers` table (`mcp_servers`) and other tables may not exist. The seeder will fail even if connectivity works if the table doesn't exist. Migrations must have run first (WI#2843 Aurora MySQL schema task).

### Resume Command (after DB verified)
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 \
  --desired-count 1 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## ECR Images

| Tag | Digest | Commit | Notes |
|-----|--------|--------|-------|
| `bda1964` | `sha256:48d2788c...` | bda1964 | Built from Sprint 3 code (postgres DSN bug) |
| `555b283` | `sha256:3988ec7f...` | 555b283 | Fix applied — env-var DB connection |
| `latest` | `sha256:3988ec7f...` | 555b283 | Same as 555b283 |

---

## Task Definitions

| Revision | Image | Secret for DefaultConnection | Issue |
|----------|-------|------------------------------|-------|
| :1 | bootstrap | `fait-v2/postgres-master` (no suffix) | ResourceNotFound |
| :2 | bootstrap | `fait-v2/postgres-master-VhGYDn` | Postgres DSN → MySQL crash |
| :3 | bda1964 | `fait-v2/postgres-master-VhGYDn` | Same Postgres DSN crash |
| :4 | **555b283** | **none** (env vars only) | **DB connectivity — current** |

---

## CloudWatch Errors (task def :4)
```
Unable to connect to any of the specified MySQL hosts.
RetryLimitExceededException (3 retries) at Program.cs:140
Database: fait_v2_dev, Server: localhost
```

---

_Report written by Rhodey | 2026-05-07 10:45 EDT_
