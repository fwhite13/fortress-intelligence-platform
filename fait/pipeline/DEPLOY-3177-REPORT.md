# Deploy Report: ADO#3177

**Date:** 2026-05-10  
**Deployment Type:** ECS task definition update  
**Feature:** 3.2-C — Scheduled task notifications: toast (in-session) + MS365 email (background)  
**Agent:** Rhodey (devops subagent)

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Previous task def | `fred-dev:160` |
| Previous commit | `e13a800b` (ADO#3173) |
| Previous image | `fred-chat:e13a800b` |
| Service state | ACTIVE, 1/1 running |

---

## Steps Completed

1. ✅ **Git verification** — HEAD at `385f5692`, status clean (no uncommitted changes)
2. ✅ **Pre-flight check** — `docker-build.sh` passed; `deploy.sh` passed credentials check (ECR repo name mismatch in script is cosmetic — `fred-chat` confirmed to exist)
3. ✅ **ECR login** — Authenticated with `fortress-tools-deployer` profile
4. ✅ **Docker build** — `fred-chat:385f5692` built successfully from `fait/Dockerfile.debian` at monorepo root `/home/fredw/projects/fip`
   - Build manifest: `sha256:94aa3b86a72b14402a5c45b25bc264c09cf0dcbeb9c541d92a24a93903b4e222`
   - Compile: 0 errors, warnings only (pre-existing)
5. ✅ **ECR push** — `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:385f5692`
   - Digest: `sha256:94aa3b86a72b14402a5c45b25bc264c09cf0dcbeb9c541d92a24a93903b4e222`
6. ✅ **Task def cloned** — Captured `fred-dev:160`, updated image, stripped read-only fields
7. ✅ **Task def registered** — `fred-dev:161` registered via `ecs-register-task-def.sh`
8. ✅ **ECS service updated** — `fred-dev` updated to `fred-dev:161`
9. ✅ **Stability wait** — `aws ecs wait services-stable` exited 0
10. ✅ **Verification** — Running task confirmed: `fred-dev:161`, image `fred-chat:385f5692`, digest matches ECR push

---

## Final Service State

```json
{
  "status": "ACTIVE",
  "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:161",
  "desired": 1,
  "running": 1,
  "pending": 0
}
```

**Running task:** `fortress-tools-cluster/20bbf69748e747d48c8ed1bf5c33e414`  
**Image digest confirmed:** `sha256:94aa3b86a72b14402a5c45b25bc264c09cf0dcbeb9c541d92a24a93903b4e222`

---

## Files Changed in This Deployment

| File | Change |
|------|--------|
| `src/FortressAI.Web/Services/ITaskNotificationService.cs` | New |
| `src/FortressAI.Web/Services/TaskNotificationService.cs` | New |
| `src/FortressAI.Web/Services/ScheduledTaskBackgroundService.cs` | Modified |
| `src/FortressAI.Web/Program.cs` | Modified |
| `src/FortressAI.Web/Components/Pages/Tasks.razor` | Modified |
| `src/FortressAI.Web/Services/SlackNotificationService.cs` | Deleted |
| `src/FortressAI.Web/Services/ISlackNotificationService.cs` | Deleted |

No DB migrations. No new env vars. No infrastructure changes.

---

## Rollback Plan

**Target:** `fred-dev:160` (commit `e13a800b`)

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:160 \
  --profile fortress-tools-deployer \
  --region us-east-1

aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --profile fortress-tools-deployer \
  --region us-east-1
```

**Rollback SLA:** < 5 minutes

---

## Summary

| Field | Value |
|-------|-------|
| Commit SHA | `385f5692` |
| Docker image | `fred-chat:385f5692` |
| ECR digest | `sha256:94aa3b86a72b14402a5c45b25bc264c09cf0dcbeb9c541d92a24a93903b4e222` |
| Previous task def | `fred-dev:160` |
| New task def | `fred-dev:161` |
| Deploy status | ✅ COMPLETE |
