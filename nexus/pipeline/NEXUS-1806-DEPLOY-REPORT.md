# NEXUS-1806 Deploy Report
**ADO Work Item:** #1806 — Vision timeout + model ID fix  
**Service:** `nexus-web`  
**Cluster:** `fortress-tools-cluster`  
**Date:** 2026-04-13  
**Deployed by:** War Machine (Rhodey / devops subagent)

---

## Summary

✅ **DEPLOY SUCCESSFUL** — nexus-web is running 1/1 with latest image.

---

## Pre-Deploy State

| Field | Value |
|---|---|
| Rollback task def | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:29` |
| Running count | 1 |
| Desired count | 1 |

---

## Build

| Field | Value |
|---|---|
| CodeBuild project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:6edfaf0a-d3f2-435b-833e-366faf8f89cc` |
| Build result | **SUCCEEDED** |
| Duration | ~1.5 min |

---

## ECS Deployment

| Field | Value |
|---|---|
| Force new deployment | ✅ |
| Task def (post-deploy) | `nexus-web:29` (same revision — CodeBuild pushes new image to ECR; forced deployment re-pulls) |
| Deployment stabilized | ✅ |
| Running / Desired | 1 / 1 |
| Pending | 0 |
| Active deployments | 1 (PRIMARY only) |

---

## ADO Comments

| Comment | ID | Timestamp |
|---|---|---|
| DEPLOY start | 743790 | 2026-04-14T01:07:25Z |
| DEPLOY complete | 743792 | 2026-04-14T01:12:01Z |

---

## Rollback Procedure

> ⚠️ Since task def revision did not change (CodeBuild pushes to ECR `:latest` tag without re-registering task def), a rollback would require:
> 1. Identifying the prior image digest in ECR
> 2. Re-tagging it as `:latest` (or the tag used by the task def)
> 3. Forcing a new ECS deployment

If a task def revision was bumped in the build, use:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:29 \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Timeline

| Time (EDT) | Event |
|---|---|
| 21:07 | Pre-deploy task def captured (`nexus-web:29`) |
| 21:07 | CodeBuild `fip-nexus-build` triggered |
| 21:07 | ADO #1806 start comment posted |
| 21:09 | Build SUCCEEDED |
| 21:09 | ECS `update-service --force-new-deployment` issued |
| 21:11 | ECS stable — 1/1 running, 1 deployment (PRIMARY) |
| 21:12 | ADO #1806 complete comment posted |
| 21:12 | Deploy report written |

---

_War Machine out._
