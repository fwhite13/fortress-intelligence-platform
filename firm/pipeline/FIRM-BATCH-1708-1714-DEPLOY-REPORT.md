# FIRM Batch Deploy Report — ADOs #1708–#1714

**Date:** 2026-04-13  
**Time:** 11:05–11:10 EDT  
**Deployed by:** War Machine (Rhodey / devops subagent)  
**Requester:** Maria Hill (pipeline-manager)  
**ADOs:** #1708, #1709, #1710, #1711, #1712, #1713, #1714  

---

## Summary

Deployed firm-web to ECS (fortress-tools-cluster) for batch ADOs #1708–#1714. CodeBuild `fip-firm-build` triggered, build succeeded in ~90 seconds, ECS force-new-deployment executed, service stabilized HEALTHY at 1/1.

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| ECS Service | `firm-web` on `fortress-tools-cluster` |
| Task def before deploy | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:79` |
| ECR image tag (pre-deploy) | `f84ff21b72b782b30ffbb16adce7d62330232819` |
| ECR repo | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web` |

---

## Build

| Item | Value |
|------|-------|
| CodeBuild project | `fip-firm-build` |
| Build ID | `fip-firm-build:952da4e6-e40e-44d7-97ce-2e33a55e01b6` |
| Build number | 49 |
| Build triggered | 2026-04-13 11:05:06 EDT |
| Build completed | 2026-04-13 11:06:33 EDT (~90 seconds) |
| Build status | **SUCCEEDED** |
| Source | `github.com/fwhite13/fortress-intelligence-platform` branch `main` |
| Buildspec | `firm/buildspec.yml` |

---

## Deploy Steps

| Step | Time (EDT) | Status | Notes |
|------|-----------|--------|-------|
| Pre-deploy snapshot | 11:04 | ✅ | task def firm-web:79, image tag f84ff21b... |
| Start CodeBuild | 11:05:06 | ✅ | Build ID: fip-firm-build:952da4e6 |
| Post ADO start comments (x7) | 11:05:20 | ✅ | All 7 WIs commented |
| Poll build — BUILD phase | 11:05:36 | ✅ | IN_PROGRESS |
| Poll build — POST_BUILD phase | 11:06:06 | ✅ | IN_PROGRESS |
| Poll build — COMPLETED | 11:06:33 | ✅ | SUCCEEDED |
| ECS force-new-deployment | 11:07 | ✅ | firm-web:79 redeployed with new image |
| ECS stabilization | 11:07–11:10 | ✅ | PRIMARY running=1/desired=1 |
| Health check | 11:10 | ✅ | HEALTHY, 1/1 running |
| Post ADO complete comments (x7) | 11:10:36 | ✅ | All 7 WIs commented |

---

## Post-Deploy State

| Item | Value |
|------|-------|
| ECS service status | ACTIVE |
| Task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:79` |
| Running count | 1 |
| Desired count | 1 |
| Pending count | 0 |
| Health status | **HEALTHY** |
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/02216848de7f4599b9a03701c9b447c4` |
| New image digest | `sha256:a8ee4043a6014a6571e2985f43a6b0bd9741bd1e8e4a2629f413cea2c21121a9` |
| Task started at | 2026-04-13 11:09:34 EDT |

---

## ECS Health Check Result

```json
{
    "running": 1,
    "desired": 1,
    "pending": 0,
    "status": "ACTIVE",
    "healthStatus": "HEALTHY"
}
```

---

## ADO Comments

| WI | Start Comment | Complete Comment |
|----|--------------|-----------------|
| #1708 | ✅ ID: 743048 | ✅ ID: 743065 |
| #1709 | ✅ ID: 743042 | ✅ ID: 743064 |
| #1710 | ✅ ID: 743047 | ✅ ID: 743066 |
| #1711 | ✅ ID: 743044 | ✅ ID: 743067 |
| #1712 | ✅ ID: 743045 | ✅ ID: 743061 |
| #1713 | ✅ ID: 743043 | ✅ ID: 743063 |
| #1714 | ✅ ID: 743046 | ✅ ID: 743062 |

---

## Rollback Plan

If issues arise, rollback to prior image using force-new-deployment on the same task def (firm-web:79 will re-pull from ECR — if needed, retag prior image `f84ff21b72b782b30ffbb16adce7d62330232819` as `latest` in ECR first):

```bash
# Option 1: Force re-deploy (will pull latest tag — if new image is bad, retag first)
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:79 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1

# Option 2: If specific prior image needed, retag in ECR first:
# aws ecr batch-get-image --repository-name firm-web --image-ids imageTag=f84ff21b72b782b30ffbb16adce7d62330232819 ...
# Then re-tag as latest and re-deploy
```

---

_Report generated: 2026-04-13 by War Machine (Rhodey / devops subagent)_
