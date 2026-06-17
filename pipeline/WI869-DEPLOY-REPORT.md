# Deploy Report: WI869 — ECS Image Tag Fix

**Agent:** War Machine (James Rhodes) — `devops`
**Date:** 2026-03-19
**Time:** 00:01–00:03 EDT
**Status:** ✅ DEPLOYED — HEALTHY

---

## Problem

CodeBuild pushed `famos-web:dev-latest` to ECR, but ECS task definition `famos-dev:1` references `famos-web:latest`. Service failed to pull image, resulting in 0/1 running tasks.

---

## Fix Applied

### Step 0: ADO Comment
Posted comment to WI869 documenting the tag mismatch and fix plan. Comment ID: 725640.

### Step 1: ECR Tag Fix
Tagged `dev-latest` as `latest` in ECR repository `famos-web`.

- **Image digest:** `sha256:be877ccce6548124009489c0b39e3f7f56a8d0a2b2927fd040c4a42d85487859`
- **Registry:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/famos-web`
- **Tags now on this image:** `dev-latest`, `latest`
- **Result:** ✅ Tagged successfully

### Step 2: Force New ECS Deployment
Force-deployed `famos-dev` service on `fortress-tools-cluster`.

- **Pre-deploy state:** running=0, desired=1
- **Post-trigger state:** ACTIVE, desired=1
- **Result:** ✅ Deployment triggered

### Step 3: Stabilization
Polled ECS service every 30s (max 10 min).

| Poll | Time | Running | Desired |
|------|------|---------|---------|
| 1 | 00:02:04 | 0 | 1 |
| 2 | 00:02:35 | 1 | 1 |

- **Time to stabilize:** ~31 seconds
- **Result:** ✅ running=1/1

### Step 4: Health Check

```
GET https://famos.dev.fortressam.ai/health
HTTP: 200
Body: {"status":"healthy","service":"famos","timestamp":"2026-03-19T04:02:54.6450549Z"}
```

- **Result:** ✅ 200 Healthy

### Step 5: Active Task Definition

- **ARN:** `arn:aws:ecs:us-east-1:742932328420:task-definition/famos-dev:1`
- **Revision:** `famos-dev:1`

---

## Summary

| Step | Status |
|------|--------|
| ECR tag (`dev-latest` → `latest`) | ✅ Done |
| ECS force redeploy | ✅ Done |
| Service stabilization (1/1) | ✅ Done (~31s) |
| Health check (200) | ✅ Pass |

---

## Rollback Plan

If rollback is required:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service famos-dev \
  --desired-count 0 \
  --region us-east-1
```

This scales the service to 0. To restore, set `--desired-count 1`.

---

## Notes

- Root cause: CodeBuild pipeline tags as `dev-latest` but task def expects `latest`. Long-term fix: update task def to reference `dev-latest` directly, or update CodeBuild to also push `latest` tag.
- No task definition revision was required — tag fix alone resolved the pull failure.
- Natasha (QA) to verify functional state.
