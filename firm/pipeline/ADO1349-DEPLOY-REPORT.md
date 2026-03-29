# ADO#1349 — DEPLOY REPORT
**War Machine (James Rhodes) — Deploy Stage**
**Date:** 2026-03-29
**Status:** ✅ COMPLETE — HEALTHY

---

## What Was Deployed

**Commit:** `29748d3` — `fix(firm): remove [Column(TypeName=json)] annotations from FirmMeetingSummary`

**Fix:** Removed `[Column(TypeName = "json")]` attributes from `FirmMeetingSummary.cs`. These annotations caused `FirmDbContext` model poisoning on startup, blocking the service entirely.

**Code Review:** PASS (Hawkeye, 1 cycle)

---

## Pre-Deploy State (Rollback Reference)

| Item | Value |
|------|-------|
| Previous task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:55` |
| Previous running image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:54` |
| Previous running task | `fcd92c88f9914398bcb4e01e1f244ae1` |

> Note: Task def `firm-web:55` was pre-existing (registered in a prior deploy with image `firm-web:54` + CloudWatch config). This deploy registered `firm-web:56` with image `firm-web:55`.

---

## Deploy Steps Executed

### 1. Git Push
```
git push origin main
29748d3 pushed to main ✅
```

### 2. Pre-flight
- Docker build pre-flight: ✅ PASSED
- AWS credentials: `fortress-tools-deployer` confirmed ✅

### 3. Docker Build
- Command: `docker build --no-cache -f firm/Dockerfile.debian -t firm-web:55 .`
- Result: ✅ SUCCESS
- Digest: `sha256:a8ec1381fef5fcd32ce30bdda490e150e4bc4a1bf475bffdf3c290d5328e9489`
- Build warnings: nullable CS8604/CS8669/CS0649/CS0414 (pre-existing, non-blocking)

### 4. ECR Push
- Tag: `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:55`
- Push result: ✅ SUCCESS
- Digest: `sha256:a8ec1381fef5fcd32ce30bdda490e150e4bc4a1bf475bffdf3c290d5328e9489`

### 5. CloudWatch Log Group
- Checked: `/ecs/firm-web` — already exists ✅
- No creation required

### 6. Task Definition Registration
- Base: `firm-web:55` (existing)
- Image updated: `firm-web:55` ECR tag
- logConfiguration added: `awslogs` → `/ecs/firm-web` ✅
- New revision registered: **`firm-web:56`** ✅

### 7. ECS Service Update
- Cluster: `fortress-tools-cluster`
- Service: `firm-web`
- Updated to: `firm-web:56`
- Wait: `aws ecs wait services-stable` → ✅ STABLE

### 8. Post-Deploy Verification
| Item | Expected | Actual |
|------|----------|--------|
| Running image | `firm-web:55` | ✅ `firm-web:55` |
| Task def ARN | `firm-web:56` | ✅ `firm-web:56` |
| Service status | HEALTHY | ✅ HEALTHY |

---

## CloudWatch Logging

logConfiguration applied to `firm-web` container:
```json
{
  "logDriver": "awslogs",
  "options": {
    "awslogs-group": "/ecs/firm-web",
    "awslogs-region": "us-east-1",
    "awslogs-stream-prefix": "ecs"
  }
}
```

---

## Rollback Procedure

If rollback is needed:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:55
aws ecs wait services-stable --cluster fortress-tools-cluster --services firm-web
```

---

## Summary

| Item | Value |
|------|-------|
| ECR Image | `firm-web:55` |
| Task Def | `firm-web:56` |
| Cluster | `fortress-tools-cluster` |
| Service | `firm-web` |
| CloudWatch | `/ecs/firm-web` — awslogs ADDED |
| Health | ✅ HEALTHY |
| Commit | `29748d3` |
