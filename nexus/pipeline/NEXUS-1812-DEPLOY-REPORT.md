# NEXUS-1812 Deploy Report — CancellationToken Vision Fix

**Date:** 2026-04-13  
**Engineer:** War Machine (devops)  
**ADO Work Item:** [#1812](https://dev.azure.com/refugegroup/FAIT/_workitems/edit/1812)  
**Commit:** `210a529` (on top of `210da62`)  

---

## Summary

Deployed the CancellationToken vision fix to `nexus-web` on ECS. Build succeeded in under 60 seconds; ECS rolled to stable in ~90 seconds.

---

## Build

| Field | Value |
|---|---|
| **CodeBuild Project** | `fip-nexus-build` |
| **Build ID** | `fip-nexus-build:197a115a-ea12-46d5-9a75-31e1c1edb625` |
| **Build #** | 32 |
| **Result** | ✅ SUCCEEDED |
| **Triggered** | 2026-04-13 22:25:59 EDT |
| **Completed** | ~2026-04-13 22:27:14 EDT |

---

## ECR Image

```
742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:8716afb197cb7660b45eabe416bbeb0a27c59134
```

---

## ECS Deployment

| Field | Value |
|---|---|
| **Cluster** | `fortress-tools-cluster` |
| **Service** | `nexus-web` |
| **Task Definition** | `nexus-web:31` |
| **Running/Desired** | 1/1 |
| **Task ARN** | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/70b56c845c764b2f907f260483b59d5a` |
| **Task Started** | 2026-04-13 22:28:16 EDT |
| **Service Stable** | 2026-04-13 22:29:45 EDT |

---

## Timeline

| Time (EDT) | Event |
|---|---|
| 22:25:59 | CodeBuild triggered |
| 22:26:44 | Build phase started |
| 22:27:14 | Build SUCCEEDED |
| 22:27:15 | ECS `update-service --force-new-deployment` issued |
| 22:28:16 | New task started (image: `8716afb1`) |
| 22:29:45 | Service STABLE — 1/1 running |

---

## Rollback

If rollback is needed: previous task definition was also `nexus-web:31`. The prior image tag in ECR is still available. To rollback to the exact prior container:

```bash
# Re-trigger with prior commit or force-update to previous image
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## ADO Comments

- **Start comment:** ID 743840 — posted 2026-04-14T02:26:05Z
- **Complete comment:** ID 743843 — posted 2026-04-14T02:30:06Z

---

_Deploy complete. No issues. ✅_
