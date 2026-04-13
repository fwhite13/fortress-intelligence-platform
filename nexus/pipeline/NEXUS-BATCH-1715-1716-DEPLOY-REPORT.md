# NEXUS Deploy Report — ADOs #1715 + #1716
**Date:** 2026-04-13  
**Time:** 11:10–11:15 EDT  
**Operator:** War Machine (devops subagent)  
**Service:** nexus-web  
**Cluster:** fortress-tools-cluster  
**Region:** us-east-1  
**AWS Profile:** fortress-tools-deployer  

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Task Definition (before) | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:28` |
| Container Image (before) | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:08d33745f68eb0180e2bc264abc4b95d01178c08` |
| Running Count | 1 |
| Desired Count | 1 |

---

## Build

| Item | Value |
|------|-------|
| CodeBuild Project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:b7720837-d067-487b-a8af-8a0227693144` |
| Build Status | ✅ **SUCCEEDED** |
| Build Start | 2026-04-13T11:10:38 EDT |
| Build End | 2026-04-13T11:11:58 EDT |
| Duration | ~80 seconds |
| Logs | https://console.aws.amazon.com/cloudwatch/home?region=us-east-1#logsV2:log-groups/log-group/$252Faws$252Fcodebuild$252Ffip-nexus-build/log-events/b7720837-d067-487b-a8af-8a0227693144 |

---

## ECS Deployment

| Item | Value |
|------|-------|
| Deployment Type | Force new deployment (same task def, fresh image pull) |
| Task Definition (after) | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:28` |
| Container Image (after) | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:08d33745f68eb0180e2bc264abc4b95d01178c08` |
| Running Task | `fc406dad029a4d60937e21a862bcecb1` |
| Running Count | 1 |
| Desired Count | 1 |
| Pending Count | 0 |
| Container Status | RUNNING |
| Health | ✅ **STABLE** |

> Note: Image tag is the same commit SHA (`08d33745...`). This pipeline uses `--force-new-deployment` to pull the freshly-built image from ECR under the same tag. Task definition revision did not increment — expected behavior for this pipeline pattern.

---

## Rollback Commands

**Rollback to pre-deploy task definition (nexus-web:28):**
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:28 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

> Note: Since the task def did not change revision, rollback would require re-triggering a previous CodeBuild run or manually pushing a prior image to ECR under the same tag.

---

## Timeline

| Time (EDT) | Event |
|------------|-------|
| 11:10:38 | CodeBuild `fip-nexus-build` triggered |
| 11:10:48 | ADO start comments posted (#1715, #1716) |
| 11:11:58 | CodeBuild SUCCEEDED |
| 11:12:39 | ECS force-new-deployment triggered |
| 11:14:53 | ECS PRIMARY deployment STABLE (1/1 running) |
| 11:15:xx | Deploy report written, completion comments posted |

---

## ADO Comments

- **#1715:** Start comment (id: 743068) + Complete comment posted ✅
- **#1716:** Start comment (id: 743069) + Complete comment posted ✅

---

## Result

✅ **DEPLOY SUCCESSFUL** — nexus-web is running healthy on ECS (1/1).
