# Deploy Report — ADO #1876 — SkippedByUser Flag Fix

**Date:** 2026-04-14  
**Deployed by:** War Machine (devops subagent)  
**Commit:** `1b0d98b`  
**Service:** `nexus-web` on `fortress-tools-cluster`  
**Profile:** `fortress-tools-deployer`

---

## Summary

Deployed the SkippedByUser flag fix (ADO #1876) to `nexus-web` via CodeBuild + ECS rolling update.

---

## Build

| Field | Value |
|-------|-------|
| Project | `fip-nexus-build` |
| Build # | 38 |
| Build ID | `fip-nexus-build:2d19eb26-0e4e-48bc-bcc1-60406445886c` |
| Source | `main` |
| Buildspec | `nexus/buildspec.yml` |
| Status | **SUCCEEDED** |
| Duration | ~1.5 min |

---

## Task Definition

| Field | Value |
|-------|-------|
| Previous | `nexus-web:35` |
| New | `nexus-web:36` |
| ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:36` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |

---

## ECS Service Health

| Field | Value |
|-------|-------|
| Cluster | `fortress-tools-cluster` |
| Service | `nexus-web` |
| Desired | 1 |
| Running | 1 |
| Pending | 0 |
| Status | ✅ **STABLE** |

---

## Rollback

If rollback is required:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:35 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

Rollback target: **`nexus-web:35`**

---

## ADO Comments

- **Start:** Comment ID 744637 — build triggered
- **Complete:** Comment ID 744641 — deploy confirmed 1/1

---

## Timeline

| Time (EDT) | Event |
|------------|-------|
| 15:21:06 | ADO start comment posted |
| 15:22:04 | CodeBuild #38 triggered |
| 15:23:45 | Build SUCCEEDED |
| 15:24:10 | `nexus-web:36` registered |
| 15:24:15 | ECS update-service issued |
| 15:24:40 | ECS STABLE — 1/1 running |
| 15:24:47 | ADO complete comment posted |
