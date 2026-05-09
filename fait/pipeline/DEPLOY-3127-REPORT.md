# Deploy Report: ADO#3127 — Cold Start UX

**Deployed by:** Rhodey (devops subagent)  
**Date:** 2026-05-09  
**Deployment Type:** ECS Fargate — New Task Definition

---

## Summary

Deployed Cold Start UX feature (ADO#3127) to `fred-dev` ECS service.  
Includes `AssistantLoadingState` component, `EnsureRunningAsync` pre-flight in `ChatView`, and polling-based readiness detection.

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Previous task def | `fred-dev:128` |
| Previous image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:59d01db4` |
| Previous commit | `59d01db4` |
| Health baseline | `HTTP 403` (auth required — expected) |

---

## Steps Completed

1. ✅ Pre-deploy snapshot captured
2. ✅ Commit `8bf9078b` verified at tip of `fip/fait`
3. ✅ Docker build — `fred-chat:8bf9078b` — used `Dockerfile.debian` (MCR-free, WSL2 compatible)
4. ✅ ECR login — `fortress-tools-deployer`
5. ✅ Image tagged and pushed — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:8bf9078b`
6. ✅ Task definition registered — `fred-dev:129` (37 env vars preserved, `taskRoleArn` preserved)
7. ✅ ECS service updated — `fred-dev` → `fred-dev:129` with `--force-new-deployment`
8. ✅ ECS stable — `RUNNING=1`, `PENDING=0`
9. ✅ Health check — `HTTP 403` on `/api/agent/status` (auth required = healthy)
10. ✅ CloudWatch logs — clean startup, no errors
11. ✅ ADO#3127 → Resolved

---

## Deployment Details

| Field | Value |
|-------|-------|
| Commit | `8bf9078b` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:8bf9078b` |
| Image digest | `sha256:e98d6e76d6b8927555c43e56ae5cc725d2c5ad5ab93b58567ef8a567fe17adc9` |
| Task def | `fred-dev:129` |
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/a6cdaf45233b40348a88f04acf70dcab` |
| Started at | `2026-05-09T14:58:14.478-04:00` |
| Health status | `HEALTHY` |

---

## CloudWatch Logs Summary

Startup sequence clean:
- Database initialization complete
- MCP servers (devops, brave, m365) loaded OK
- `Now listening on: http://[::]:8080`
- `Application started`

No errors or warnings at startup.

---

## Rollback Plan

### Pre-Deploy State
- Previous task def: `fred-dev:128`
- Previous image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:59d01db4`

### Rollback Commands
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:128 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer

aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

### Rollback Verification
- [ ] Health check passes after rollback
- [ ] HTTP 403 on `/api/agent/status`
- [ ] No error spike in CloudWatch

### Rollback SLA
**< 5 minutes** (ECS Fargate rolling deploy)

---

## What Was Shipped (ADO#3127)

- `AssistantLoadingState.razor` — loading screen with spinner, status sequence, 60s timeout + retry
- `ChatView.razor` — calls `EnsureRunningAsync` before `GetSessionAsync` on `OnInitializedAsync`; cold-start polling triggers Fargate task launch
- Timer fix from `16151abe` — `StopPolling` nulls `_timer`, recreated on retry
- Existing `/chat` flow for users with a running session: **unchanged**
