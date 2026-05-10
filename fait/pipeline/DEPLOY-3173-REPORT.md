# Deploy Report: ADO#3173

## Task
3.3-B: On-Demand Tab, History Tab, Failed-Task Banner (fait-v2 feature)

## Deployment Type
AWS ECS Fargate — new task definition revision

---

## Pre-Deploy Snapshot
- **Previous task def:** `fred-dev:159`
- **Previous image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:2c1c894a`
- **Previous commit:** `2c1c894a` (ADO#3172)
- **Service state:** ACTIVE, 1/1 running

---

## Steps Completed

1. ✅ **Commit verified** — `e13a800b` confirmed at HEAD on `main`; matches `origin/main`
2. ✅ **Pre-deploy check passed** — `scripts/pre-deploy-check.sh` passed; `fortress-tools-deployer` credentials confirmed
3. ✅ **Docker build** — `docker build --no-cache -f fait/Dockerfile.debian -t fred-chat:e13a800b .` from monorepo root — SUCCESS
   - Build image digest: `sha256:934685a05f45aafb852b09daf13b9fbbb60ac90e42ab17463c26651084d98060`
4. ✅ **ECR push** — Tagged and pushed to ECR
   - Image: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:e13a800b`
   - ECR digest: `sha256:934685a05f45aafb852b09daf13b9fbbb60ac90e42ab17463c26651084d98060`
5. ✅ **Task def registered** — `fred-dev:160`
   - Container image updated to `fred-chat:e13a800b`
   - Registered via `scripts/ecs-register-task-def.sh`
6. ✅ **ECS service updated** — `fred-dev` → `fred-dev:160`
7. ✅ **Stability wait** — `aws ecs wait services-stable` returned clean (code 0)

---

## Final Service Verification

```json
{
  "status": "ACTIVE",
  "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:160",
  "desired": 1,
  "running": 1
}
```

---

## Summary

| Field | Value |
|-------|-------|
| Commit SHA | `e13a800b` |
| Docker image | `fred-chat:e13a800b` |
| ECR digest | `sha256:934685a05f45aafb852b09daf13b9fbbb60ac90e42ab17463c26651084d98060` |
| Previous task def | `fred-dev:159` |
| New task def | `fred-dev:160` |
| Service status | ACTIVE, 1/1 running |

---

## Rollback Plan

If the service is unhealthy, roll back immediately to `fred-dev:159`:

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:159 \
  --profile fortress-tools-deployer \
  --region us-east-1

aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --profile fortress-tools-deployer \
  --region us-east-1
```

Rollback SLA: < 5 minutes.

---

_Deployed by Rhodey (War Machine) — 2026-05-10_
