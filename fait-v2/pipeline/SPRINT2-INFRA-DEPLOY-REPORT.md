# FAIT v2 Sprint 2 — Infrastructure Deploy Report
**Date:** 2026-05-07 09:13–09:30 EDT  
**Executor:** Rhodey (War Machine) — DevOps subagent  
**Status:** ❌ BLOCKED — Secret `fait-v2/postgres-master` missing

---

## Summary

Steps 1–2 were completed in a prior session. Steps 3–5 completed successfully in this session. Step 6 (health) is blocked: the ECS task cannot start because the secret `fait-v2/postgres-master` does not exist in Secrets Manager. Service has been halted at desired-count=0 to stop thrashing.

**Fred action required:** Create secret `fait-v2/postgres-master` (see below), then signal to resume.

---

## Step-by-Step Results

| Step | Action | Result |
|------|--------|--------|
| 1 | Route53 CNAME `fait-v2.dev.fortressam.ai` → ALB | ✅ Done (prior session) |
| 2 | ALB listener rule + target group health check `/health` | ✅ Done (prior session) |
| 3 | Register ECS task definition `fait-v2` | ✅ |
| 4 | Create CloudWatch log group `/ecs/fait-v2` | ✅ Created |
| 5 | Create ECS service `fait-v2` | ✅ Created |
| 5a | Add IAM policy `fait-v2-secrets-access` to execution role | ✅ Added (was missing) |
| 6 | Wait for stability / health check | ❌ BLOCKED — secret not found |

---

## Resources Created

### Task Definition
- **ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2:1`
- **Revision:** 1
- **CPU/Memory:** 512 vCPU / 1024 MB
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2:bootstrap`
- **Registered at:** 2026-05-07T09:13:31 EDT

### CloudWatch Log Group
- **Name:** `/ecs/fait-v2`
- **Region:** us-east-1

### ECS Service
- **ARN:** `arn:aws:ecs:us-east-1:742932328420:service/fortress-tools-cluster/fait-v2`
- **Cluster:** `fortress-tools-cluster`
- **Launch type:** FARGATE
- **Desired count:** 0 (halted — see blocker below)
- **Health check grace period:** 120s
- **Network:** subnets `subnet-08e1d4f1b5530f39e`, `subnet-051bfcf5b07661809` / SG `sg-0fb53615b1eb4a175`
- **Target group:** `fait-v2-dev-tg` (b81255eae56c643c)

### IAM Policy Added
- **Role:** `fortress-tools-ecs-execution-role`
- **Policy name:** `fait-v2-secrets-access` (inline)
- **Grants:** `secretsmanager:GetSecretValue` on `arn:aws:secretsmanager:us-east-1:742932328420:secret:fait-v2/*`
- **Reason:** Execution role only had `fortress-tools/*` and `mcp-memory/*` access; `fait-v2/*` was not covered.

---

## Blocker: Secret Does Not Exist

**Error from ECS events:**
```
ResourceNotFoundException: Secrets Manager can't find the specified secret.
Secret: arn:aws:secretsmanager:us-east-1:742932328420:secret:fait-v2/postgres-master
```

The secret `fait-v2/postgres-master` referenced in the task definition does **not exist** in Secrets Manager. This was listed as a pre-deploy blocker (Fred action required) in the Sprint 2 state doc.

### Fred — Action Required

Create the secret with the PostgreSQL connection string for `fait-v2-dev`:

```bash
# Option A: AWS Console
# Go to Secrets Manager → Store a new secret → Other type
# Name: fait-v2/postgres-master
# Value: the PostgreSQL connection string (e.g. Host=...;Database=...;Username=...;Password=...)

# Option B: AWS CLI (substitute real values)
aws secretsmanager create-secret \
  --name "fait-v2/postgres-master" \
  --region us-east-1 \
  --secret-string "Host=<HOST>;Port=5432;Database=<DB>;Username=<USER>;Password=<PASS>" \
  --profile fortress-tools-deployer
```

The secret value should be the `ConnectionStrings__DefaultConnection` value for the FAIT v2 Postgres database.

Once the secret is created, resume deploy with:

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fait-v2 \
  --desired-count 1 \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Target Health Status
- **State:** N/A — no healthy targets (service at desired-count=0, tasks never reached running state)
- **Health check HTTP code:** N/A — no tasks running

---

## Rollback Plan

If service needs to be fully torn down:

```bash
# Stop all tasks
aws ecs update-service --cluster fortress-tools-cluster --service fait-v2 --desired-count 0 \
  --profile fortress-tools-deployer --region us-east-1

# Delete service (after tasks stop)
aws ecs delete-service --cluster fortress-tools-cluster --service fait-v2 --force \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Next Steps (after secret created)

1. Fred creates `fait-v2/postgres-master` in Secrets Manager ← **BLOCKING**
2. Set desired-count back to 1 (command above)
3. Monitor ECS events for task startup
4. Verify target group health → `healthy`
5. Curl `/health` endpoint via ALB with `Host: fait-v2.dev.fortressam.ai`
6. If healthy: mark deploy complete, update WI

---

_Report written by Rhodey | 2026-05-07 09:30 EDT_
