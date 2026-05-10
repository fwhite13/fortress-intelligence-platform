# Deploy Report: ADO#3189

**Feature:** 4.3-A: /memory page — topic list + markdown viewer/editor  
**Date:** 2026-05-10  
**Agent:** War Machine (Rhodey — DevOps)

---

## Deployment Type

ECS task definition update → force new deployment

---

## Pre-Deploy Snapshot

- **Previous task def:** `fred-dev:163`
- **Previous image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:124d2388`
- **Previous commit:** `124d2388`
- **Rollback task def:** `fred-dev:163` (ready)

---

## Steps Completed

1. ✅ Pre-flight check passed — `fortress-tools-deployer` credentials confirmed, ECR repo `fred-chat` exists
2. ✅ Commit verified — HEAD at `975c2d39` (fix: reserved slug guard in CreateTopicAsync)
3. ✅ Docker build — `docker build --no-cache -f fait/Dockerfile.debian -t fred-chat:975c2d39 .` — SUCCESS
4. ✅ ECR login — successful
5. ✅ Image tagged + pushed — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:975c2d39`
   - Digest: `sha256:3756aff9c7265632d06f2c47fb474e08d9a7f37eecbb17e140f9cd9f8fa7ab46`
6. ✅ Task def cloned from `fred-dev:163`, image updated, registered as `fred-dev:164`
   - taskRoleArn: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` (inherited)
7. ✅ ECS service updated — `aws ecs update-service --task-definition fred-dev:164`
8. ✅ Service stable — `aws ecs wait services-stable` completed
9. ✅ Verification — 1/1 RUNNING on `fred-dev:164`, image digest matches push

---

## Deployment Details

| Field | Value |
|-------|-------|
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `fred-dev` |
| New task def | `fred-dev:164` |
| Image tag | `fred-chat:975c2d39` |
| Image digest | `sha256:3756aff9c7265632d06f2c47fb474e08d9a7f37eecbb17e140f9cd9f8fa7ab46` |
| Running | 1/1 |
| Deploy start | ~12:14 EDT |
| Service stable | 12:18:05 EDT |

---

## Files Deployed

- `src/FortressAI.Web/Components/Pages/Memory.razor` (new — /memory page with topic list + viewer/editor)
- `src/FortressAI.Web/Components/Layout/MainLayout.razor` (modified — /memory nav entry)

No DB migrations. No new env vars. No harness changes.

---

## Rollback Plan

### Pre-Deploy State
- **Task def:** `fred-dev:163`
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:124d2388`

### Rollback Commands
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:163 \
  --region us-east-1

aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1
```

### Rollback SLA
**< 5 minutes**

---

## Status

✅ **DEPLOY COMPLETE** — `fred-dev:164` running `fred-chat:975c2d39` — 1/1 HEALTHY
