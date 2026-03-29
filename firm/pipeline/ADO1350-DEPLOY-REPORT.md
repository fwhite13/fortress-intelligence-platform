# ADO#1350 — Deploy Report
**Agent:** War Machine (James Rhodes)  
**Date:** 2026-03-29  
**Target:** `firm-web:56` → `fortress-tools-cluster/firm-web`

---

## Summary

Deployed commit `2bac7aa` — removed `HasColumnType("JSON")` from 3 `FirmMeetingSummary` properties in `FirmDbContext.cs`. Fixes `ElementMappingConvention` NullRef that crashes `FirmDbContext` model on startup.

**Result: SUCCESS — HEALTHY**

---

## Pre-Deploy State

| Item | Value |
|------|-------|
| Previous task def | `firm-web:56` (registered but pointed to image `:55`) |
| Running image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:55` |
| Running task def revision | `firm-web:56` |

> **Note:** Task def `firm-web:56` was already registered (likely from a prior aborted deploy attempt) but still referenced image `:55`. The ECR image `:56` had not been built or pushed. Full build + new task def (`firm-web:57`) was required.

---

## Rollback Procedure

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:56
aws ecs wait services-stable --cluster fortress-tools-cluster --services firm-web
```
> Rollback to `firm-web:56` restores image `:55` (last known-good state).

---

## Deploy Steps Executed

### 1. Git Push
```
git push origin main
→ 29748d3..2bac7aa  main -> main
```

### 2. ECR Login
```
Login Succeeded
```

### 3. Docker Build
```
docker build --no-cache -f firm/Dockerfile.debian -t firm-web:56 .
→ Build SUCCESS
→ Image: sha256:9f494ff2bedb039652c61ec0f190cc7fa755589ccd9cad0d6938d9196a63d889
→ Manifest: sha256:98cd85d090377d0dd883ac373629dd11dbba03b434808801ef411b6c0d1b5ee3
```
- Compile: PASS (warnings only, no errors)
- .NET SDK: 8.0.419 | Runtime: 8.0.25

### 4. ECR Push
```
docker push 742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:56
→ digest: sha256:98cd85d090377d0dd883ac373629dd11dbba03b434808801ef411b6c0d1b5ee3
```

### 5. Task Definition Registration
- Verified `awslogs` logConfiguration present in current task def ✅
- logDriver: `awslogs`
- awslogs-group: `/ecs/firm-web`
- awslogs-region: `us-east-1`
- awslogs-stream-prefix: `ecs`
- New task def registered: **`firm-web:57`** (image: `firm-web:56`)

### 6. Service Update
```
aws ecs update-service → firm-web:57
aws ecs wait services-stable → STABLE
```

---

## Post-Deploy Verification

| Check | Result |
|-------|--------|
| Running image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:56` ✅ |
| Container status | `RUNNING` ✅ |
| Health status | `HEALTHY` ✅ |
| Task definition | `firm-web:57` ✅ |
| CloudWatch logConfiguration | Preserved ✅ |

---

## Fix Deployed

- **Commit:** `2bac7aa`
- **Change:** Removed `HasColumnType("JSON")` from `FirmMeetingSummary.ActionItems`, `.KeyDecisions`, `.Summary` in `FirmDbContext.cs`
- **Impact:** Eliminates `ElementMappingConvention` NullReferenceException that crashed `FirmDbContext` on startup

---

## Code Review

- **Reviewer:** Hawkeye
- **Cycles:** 1
- **Result:** PASS
