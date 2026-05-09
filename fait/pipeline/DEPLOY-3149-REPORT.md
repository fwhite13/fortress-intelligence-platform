# Deploy Report — ADO#3149

**Date:** 2026-05-09  
**Deployer:** War Machine (rhodey-deploy-3149)  
**Branch/Commit:** `1bc3bb3f`  
**Fix:** `fix(fait#3149): collapse _checkingAgent + !_agentReady into single AssistantLoadingState block`

---

## What Was Deployed

AssistantLoadingState spinner fix — the cold start spinner now remains visible for the full duration of the cold start (collapsed `_checkingAgent` and `!_agentReady` checks into a single `AssistantLoadingState` block).

---

## Deployment Summary

| Step | Detail |
|------|--------|
| **Image tag** | `fred-chat:1bc3bb3f` |
| **ECR URI** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:1bc3bb3f` |
| **ECR digest** | `sha256:cdee85e9ee48880dc42776db1d32e7350ad2b3ac36e2abb6272e4202070089ad` |
| **Previous task def** | `fred-dev:140` (image `fred-chat:6ed90f0c`) |
| **New task def** | `fred-dev:141` |
| **ECS cluster** | `fortress-tools-cluster` |
| **ECS service** | `fred-dev` |

---

## Build

- Built from monorepo root: `cd /home/fredw/projects/fip && docker build --no-cache -f fait/Dockerfile -t fred-chat:1bc3bb3f .`
- Build succeeded, image manifest: `sha256:cdee85e9ee48880dc42776db1d32e7350ad2b3ac36e2abb6272e4202070089ad`

---

## Task Definition Changes

Only one change from `fred-dev:140`:
- **Image:** `fred-chat:6ed90f0c` → `fred-chat:1bc3bb3f`

All env vars preserved exactly, including:
- `Fargate__ContainerName = fait-v2-agent-harness` ✅
- `Fargate__TaskDefinition = fait-v2-agent-harness:8` ✅
- `taskRoleArn = arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅

---

## Deployment Status

| Metric | Value |
|--------|-------|
| **Service status** | ACTIVE |
| **Running** | 1 |
| **Pending** | 0 |
| **Desired** | 1 |
| **Primary deployment** | `fred-dev:141` — RUNNING |

---

## CloudWatch Log Verification

Startup log stream: `ecs/fred/05d2d738963e47088a3d07fe4b96dab5`

- ✅ Database initialization complete
- ✅ `Now listening on: http://[::]:8080`
- ✅ `Application started`
- ✅ MCP tools loaded (devops, brave, m365)
- ✅ No errors in startup sequence

---

## ADO Update

ADO#3149 marked **Resolved** with comment:  
> Deployed fred-chat:1bc3bb3f, fred-dev:141. AssistantLoadingState spinner now shown for full cold start duration.

---

## Rollback

If needed:
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:140 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```
