# FIRM-1785 Deploy Report — Summary Markdown Renderer

**Date:** 2026-04-13  
**Engineer:** War Machine (Rhodey / devops)  
**ADO Work Item:** [FAIT #1785](https://dev.azure.com/FortressAM/FAIT/_workitems/edit/1785)  
**Deployment Type:** CodeBuild → ECS force-new-deployment  

---

## Summary

Deployed ADO #1785 (Summary markdown renderer) to `firm-web` ECS service via CodeBuild pipeline.

---

## Pre-Deploy State

| Property | Value |
|---|---|
| Previous task def | `firm-web:82` |
| Rollback target | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:82` |
| Service status | 1/1 running, 0 pending |

> **Note:** Pre-flight script flagged a false positive on ECR repo name — script maps `firm` → `meeting-assistant-aws` (stale), actual ECR repo is `firm-web` (confirmed exists). Credentials verified valid. Proceeding was safe.

---

## Deploy Steps

| Step | Action | Result |
|---|---|---|
| 1 | CodeBuild `fip-firm-build` triggered | `IN_PROGRESS` |
| 2 | ADO #1785 start comment posted | ✅ Comment ID 743524 |
| 3 | Build monitored to completion | ✅ `SUCCEEDED` (~90s) |
| 4 | ECS `firm-web` force-new-deployment | ✅ Accepted |
| 5 | ECS health check | ✅ 1/1 stable |
| 6 | ADO #1785 complete comment posted | ✅ |

---

## Build Details

| Property | Value |
|---|---|
| CodeBuild project | `fip-firm-build` |
| Build ID | `fip-firm-build:4327008b-d0a7-4ca3-b328-dbba430e52aa` |
| Build status | `SUCCEEDED` |
| Duration | ~90 seconds |

---

## ECS Post-Deploy State

| Property | Value |
|---|---|
| Cluster | `fortress-tools-cluster` |
| Service | `firm-web` |
| Task def | `firm-web:82` |
| Running | 1 |
| Desired | 1 |
| Pending | 0 |
| Status | ✅ STABLE |

---

## Rollback Procedure

If issues arise, roll back immediately with:

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition firm-web:82 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

> Note: Since ECS stayed on task def `:82` (same revision, new image from ECR `:latest`), rollback requires re-deploying the prior image. If the prior ECR image tag is needed, retrieve from CloudWatch build logs.

---

## Issues / Notes

- Pre-flight script has stale ECR mapping for `firm` → should be `firm-web`, not `meeting-assistant-aws`. Low-priority fix needed in `scripts/preflight/deploy.sh`.
- `.env.deployer` is located at `/home/fredw/projects/ai/projects/fortress_tools/.env.deployer` (not the path documented in TOOLS.md — that path is stale).

---

_War Machine — 2026-04-13_
