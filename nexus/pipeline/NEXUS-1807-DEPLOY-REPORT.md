# NEXUS-1807 Deploy Report — SpecGen IOptions Config Refactor

**Date:** 2026-04-13  
**Deployer:** War Machine (Rhodey / devops subagent)  
**ADO Work Item:** #1807  

---

## Summary

Deployed nexus-web with two new ECS environment variables required for the SpecGen IOptions configuration refactor. Build triggered via CodeBuild, ECS service updated to new task def revision.

---

## Task Definition Changes

| Field | Value |
|-------|-------|
| Previous revision | nexus-web:29 |
| New revision | nexus-web:30 |
| New task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:30` |

### New environment variables added:
| Name | Value |
|------|-------|
| `Bedrock__SpecGen__ModelId` | `us.anthropic.claude-sonnet-4-5-20250929-v1:0` |
| `Bedrock__SpecGen__VisionModelId` | `us.anthropic.claude-sonnet-4-5-20250929-v1:0` |

Total env vars: 15 → 17

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:2f69d4e9-462d-43ec-ace9-b548685f3ff5` |
| Build status | **SUCCEEDED** |
| Build duration | ~75 seconds |
| Start time | 2026-04-13 21:26:31 EDT |

---

## ECS Deploy

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `nexus-web` |
| Task definition | `nexus-web:30` |
| Running | 1/1 |
| Pending | 0 |
| Status | **HEALTHY** |

---

## Rollback Procedure

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:29 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Timeline

| Time (EDT) | Event |
|-----------|-------|
| 21:26:18 | Task def nexus-web:30 registered |
| 21:26:31 | CodeBuild fip-nexus-build triggered |
| 21:27:45 | Build SUCCEEDED |
| 21:27:58 | ECS service updated to nexus-web:30 |
| 21:28:00 | ECS health check: 1/1 HEALTHY |

---

## ADO Comments

- Start comment posted: comment ID 743803
- Complete comment posted on ADO #1807

---

_Deploy completed successfully. No issues encountered._
