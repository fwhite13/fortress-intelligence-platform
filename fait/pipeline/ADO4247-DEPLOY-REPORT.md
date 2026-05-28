# ADO#4247 Deploy Report — fait-v2-task-role pgvector Secret Access

**Date:** 2026-05-27  
**Engineer:** devops subagent (rhodey-ado4247)  
**WI:** [ADO#4247](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/4247)  
**Risk Level:** Low (no code change, no image rebuild)

---

## Summary

Force-new-deployment on `fred-dev` ECS service to propagate the `FaitPgvectorSecretAccess` IAM inline policy that Fred had already applied to `fait-v2-task-role`.

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Service | `fred-dev` |
| Cluster | `fortress-tools-cluster` |
| Task Definition (before) | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:290` |
| Desired Count | 1 |

---

## IAM Policy Applied (by Fred, prior to deploy)

- **Policy Name:** `FaitPgvectorSecretAccess`
- **Role:** `fait-v2-task-role`
- **Action:** `secretsmanager:GetSecretValue`
- **Resource:** `arn:aws:secretsmanager:us-east-1:742932328420:secret:fortress-tools/pgvector-connection-wx0f9F`

---

## Deployment Steps

### Step 0 — Pre-Deploy Snapshot
- Captured current task definition: `fred-dev:290` ✅

### Step 1 — Force-New-Deployment
- `aws ecs update-service --force-new-deployment` executed ✅
- Task definition unchanged: `fred-dev:290` (no new revision needed)
- `aws ecs wait services-stable` completed successfully ✅

---

## Post-Deploy State

| Item | Value |
|------|-------|
| Task Definition (after) | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:290` (unchanged) |
| ECS Service Status | STABLE |
| New Task Revision | None — same task def, new tasks spawned with updated role |

---

## Result

New ECS tasks are running under `fait-v2-task-role` with the `FaitPgvectorSecretAccess` policy active. All running harness tasks now have `secretsmanager:GetSecretValue` access to the pgvector connection secret.

---

## Next Step

Returning to Maria for QA validation.
