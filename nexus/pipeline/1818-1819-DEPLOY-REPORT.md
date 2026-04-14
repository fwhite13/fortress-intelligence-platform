# Deploy Report — NEXUS ADO #1818 + #1819
**Date:** 2026-04-14  
**Deployer:** War Machine (devops subagent)  
**Build:** `fip-nexus-build`

---

## Work Items

| WI | Title | Commit |
|----|-------|--------|
| #1818 | DetectFileType extension fallback | `95a8aec` |
| #1819 | Discovery image vision | `7de0146` |

Both commits deployed as a combined build from nexus repo HEAD.

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild Project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:1825e959-8b42-4505-8d83-2e63ec72abff` |
| Build Status | **SUCCEEDED** |
| Build Start | 2026-04-14 00:05:07 EDT |
| Build Duration | ~2 minutes |

---

## Task Definition

| Field | Value |
|-------|-------|
| Previous revision | `nexus-web:31` |
| New revision | `nexus-web:32` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |
| Registration method | Manual (buildspec gap — does not auto-register) |

---

## ECS Deployment

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `nexus-web` |
| Task Def | `nexus-web:32` |
| Running / Desired | 1 / 1 |
| Status | **HEALTHY** |

---

## Rollback

```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:31 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## ADO Comments

- **#1818 start** — comment id 743880 ✅
- **#1819 start** — comment id 743881 ✅
- **#1818 complete** — comment id 743884 ✅
- **#1819 complete** — comment id 743885 ✅

---

## Notes

- `fip-nexus-build` buildspec does **not** auto-register a new ECS task definition revision. New task def was manually registered as `nexus-web:32` pointing to `:latest` ECR image.
- ECS stabilized immediately (1/1 healthy) upon service update.
