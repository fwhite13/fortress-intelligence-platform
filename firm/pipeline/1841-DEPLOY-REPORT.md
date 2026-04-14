# Deploy Report — ADO #1841 + #1844
**Date:** 2026-04-14  
**Engineer:** War Machine (Rhodey)  
**Session:** Mid-deploy recovery after previous session dropped

---

## Pre-Deploy State

| Service | Task Definition | Running | Desired |
|---------|----------------|---------|---------|
| meetings-vpbot-dev | firm-vpbot:9 | 1 | 1 |
| firm-web | firm-web:91 | 1 | 1 |

---

## CodeBuild Status

| Field | Value |
|-------|-------|
| Build ID | fip-firm-build:2bd9ca86-5dba-4541-bc10-f714c5482889 |
| Status | **SUCCEEDED** |
| End Time | 2026-04-14T15:57:05 EDT |
| Commit (resolvedSourceVersion) | 1b0d98b300089113370059992fdd00fb4bd01ccf (HEAD of main) |
| Source | branch: main |

> Note: Brief specified commit `620ea24` but CodeBuild resolved to `1b0d98b` (latest HEAD of main at build time). Build SUCCEEDED and pushed firm-web:latest.

---

## vpbot — firm-vpbot:10

### Changes from previous revision (firm-vpbot:9)
- **cpu:** 2048 → **1024** (1 vCPU — downsized per ADO#1841)
- **memory:** 4096 → **2048** (2 GB — downsized per ADO#1841)
- **image:** updated to `:latest`

### ECR Push
- **Repository:** firm-vpbot
- **Tag:** latest
- **Digest:** sha256:abd8182affdefc282949ca1d6c4afe1c6e64eb14e8239a35e53cb9d12438fc00
- **Pushed:** 2026-04-14 ~20:51 EDT (fresh build this session)

### Task Definition
- **ARN:** arn:aws:ecs:us-east-1:742932328420:task-definition/firm-vpbot:10
- **CPU:** 1024 ✅
- **Memory:** 2048 ✅
- **Env vars preserved:** HF_TOKEN, HUGGINGFACE_API_KEY, LOG_LEVEL, QUIET, PYTHONUNBUFFERED ✅

### Deployment Result
- **Service:** meetings-vpbot-dev
- **rolloutState:** COMPLETED ✅
- **runningCount:** 1 / desiredCount: 1 ✅
- **failedTasks:** 0 ✅
- Stabilized at poll [13] (~3m 15s)

---

## firm-web — firm-web:92

### ECR :latest
- **Repository:** firm-web
- **Tags:** latest, 1b0d98b300089113370059992fdd00fb4bd01ccf
- **Digest:** sha256:439eaa82e41e2710c1d13b14c0828ed422c344261d252594e509b6d68ae78906
- **Pushed:** 2026-04-14T15:57:03 EDT (by CodeBuild)

### Task Definition
- **ARN:** arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:92
- **CPU:** 512 (unchanged)
- **Memory:** 1024 (unchanged)
- **Image:** 742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:latest ✅

### Deployment Result
- **Service:** firm-web
- **rolloutState:** COMPLETED ✅
- **runningCount:** 1 / desiredCount: 1 ✅
- **failedTasks:** 0 ✅
- Stabilized at poll [9] (~2m 15s)

---

## Stale Target Deregistration

| Action | Detail |
|--------|--------|
| New task IP | 172.31.77.10 |
| Stale target deregistered | 172.31.32.87:8080 |
| TG after deregistration | 172.31.77.10:8080 — **healthy** |
| Old target status | draining (normal ALB drain behavior) |

---

## Health Checks

| Check | Result | Notes |
|-------|--------|-------|
| firm.dev.fortressam.ai root | 403 → CF Access redirect | Expected — Cloudflare Access protecting the endpoint |
| FipShared CSS | 302 → CF Access | Expected — same Cloudflare Access auth wall; curl -L returns 200 from CF login page |

> The 302 on FipShared is **not** a missing FipShared — it's Cloudflare Access intercepting the unauthenticated request. Same behavior as pre-deploy baseline. Site is serving correctly.

---

## IAM Updates (completed prior session)
- `VpbotBatchSubmit` policy → attached to `fortress-tools-ecs-task-role` ✅
- `FirmWebBatchSubmit` policy → attached to `fortress-tools-ecs-task-role` ✅

---

## Rollback Plan

If rollback is needed:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

# Rollback vpbot
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service meetings-vpbot-dev \
  --task-definition firm-vpbot:9 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer

# Rollback firm-web
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition firm-web:91 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

---

## Summary

| Item | Status |
|------|--------|
| CodeBuild fip-firm-build | SUCCEEDED ✅ |
| vpbot Docker build + ECR push | SUCCEEDED ✅ |
| firm-vpbot:10 registered (1vCPU/2GB) | ✅ |
| firm-web:92 registered | ✅ |
| meetings-vpbot-dev deploy | STABLE ✅ |
| firm-web deploy | STABLE ✅ |
| Stale TG target deregistered | ✅ |
| ADO #1841 updated | ✅ |
| ADO #1844 updated | ✅ |
