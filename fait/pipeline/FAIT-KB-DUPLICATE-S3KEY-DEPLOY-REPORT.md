# Deploy Report: FAIT KB Duplicate S3Key Fix

**Date:** 2026-03-12
**Time:** 13:12–13:21 EDT
**Commit:** `43a30f4` — KB duplicate S3Key fix
**Target:** `fred-dev` ECS service on `fortress-tools-cluster`
**Deployer:** War Machine (Rhodey) / devops subagent
**Outcome:** ✅ DEPLOYED SUCCESSFULLY

---

## Pre-Deploy State

| Item | Value |
|------|-------|
| Task Definition Revision | `fred-dev:63` |
| Task Definition ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:63` |
| Running Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/b87ab431c1094a73946f517501a6da11` |
| Image Digest (pre-deploy) | `sha256:be890b217ed03fb0398ae36ad17902ef715f264d81fef902b7dee2cfa877b085` |

---

## Rollback Plan

**Execute immediately if issues arise — reverts to pre-deploy revision:**

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:63 \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

> ⚠️ Pre-deploy revision was `fred-dev:63`. Force-new-deployment used same task def; image rollback requires pinning task def to revision 63 and updating the service.

---

## Deploy Execution

### Step 1: CodeBuild Trigger
- **Build ID:** `fip-fait-build:896c5e6a-ff09-46ee-b7bc-e0e767e969e6`
- **Project:** `fip-fait-build`
- **Triggered at:** ~13:12 EDT

### Step 2: Build Completion
- **Final Status:** `SUCCEEDED`
- **Duration:** ~3 minutes (13:12–13:15 EDT)

### Step 3: ECS Force-New-Deployment
- **Status:** ACTIVE — force-new-deployment triggered at 13:15 EDT
- **Task Def Used:** `fred-dev:63` (same revision, new image via force-new-deployment)

### Step 4: ECS Stability
- **Final State at Stabilization:**

| Metric | Value |
|--------|-------|
| Desired Count | 1 |
| Running Count | 1 |
| Pending Count | 0 |
| Rollout State | COMPLETED |
| Active Deployments | 1 (PRIMARY only) |

- **Stabilized at:** ~13:20 EDT

### Step 5: Image Digest Verification

| Source | Digest |
|--------|--------|
| Running Task (post-deploy) | `sha256:d9a9ec659f9682437a33ae053edda5ec4c38a30431c9af300141c77085577c03` |
| ECR `kb-latest` tag | `sha256:d9a9ec659f9682437a33ae053edda5ec4c38a30431c9af300141c77085577c03` |
| **Match** | ✅ **YES** |

- **New task ARN:** `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/44dd3e84ea624895b8dd8fab1868fa79`

---

## Health Check

```
GET https://fait.dev.fortressam.ai/health
```

**Response:**
```json
{"status":"healthy","service":"fred","timestamp":"2026-03-12T17:21:06.2493994Z"}
```

**Result:** ✅ PASSED

---

## Summary

| Stage | Result |
|-------|--------|
| Pre-deploy capture | ✅ |
| Rollback plan documented | ✅ |
| CodeBuild | ✅ SUCCEEDED |
| ECS force-new-deployment | ✅ |
| ECS stability | ✅ desired=1 running=1 pending=0 |
| Image digest match | ✅ |
| Health check | ✅ healthy |

**Total deploy time:** ~9 minutes (13:12–13:21 EDT)
