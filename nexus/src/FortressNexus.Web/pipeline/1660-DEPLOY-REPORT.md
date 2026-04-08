# Deploy Report — WI #1660 — Skip-Regen Path
**Date:** 2026-04-08  
**Deployer:** War Machine (devops subagent)  
**Service:** nexus-web (ECS / fortress-tools-cluster)

---

## Summary

Deployed WI #1660 (skip-regen path: Draft → AwaitingReview direct when no changes and prior spec exists) to production.

---

## Git

| Field | Value |
|---|---|
| Target commit | `21058b8` (HEAD of main) |
| WI commit | `137ea83` — `feat(nexus): WI #1660 — skip-regen path` |
| Branch | main |

---

## Build

| Field | Value |
|---|---|
| CodeBuild project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:0fb2c5c7-4a34-4f10-8d91-aa8dfc2e0c9a` |
| Build number | 19 |
| Status | **SUCCEEDED** |
| Started | 2026-04-08 14:58:16 EDT |
| Completed | ~2026-04-08 15:00:01 EDT |
| ECR image tag | `d222b7d032812de78c9607f1e386a09cb5622959` |
| ECR repo | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web` |

---

## Task Definition

| Field | Value |
|---|---|
| Previous | `nexus-web:20` |
| New | `nexus-web:21` |
| ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:21` |

---

## Deployment

| Field | Value |
|---|---|
| Cluster | `fortress-tools-cluster` |
| Service | `nexus-web` |
| Strategy | force-new-deployment |
| Rollout state | **COMPLETED** |
| Desired / Running | 1 / 1 |
| Running task ID | `ce3916a537f542b991a6faa9ab8461eb` |

---

## Health Check

| Check | Result |
|---|---|
| `curl https://nexus.fortressam.ai/` | **HTTP 403** ✅ (expected — Cognito auth gate) |

---

## CloudWatch Logs

Startup sequence (log group: `/ecs/nexus-web`):

```
[19:07:14 INF] [NEXUS] Running EF Core migrations on startup...
[19:07:16 INF] [NEXUS] EF Core migrations complete.
[19:07:16 WRN] Overriding HTTP_PORTS '8080' and HTTPS_PORTS '' (non-issue, pre-existing)
```

**No exceptions. No errors. Clean startup.**

---

## Schema Changes

None — no migrations applied beyond existing schema (EF Core ran and completed cleanly).

---

## Rollback Procedure

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:20 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

## Result

✅ **DEPLOY SUCCESSFUL** — nexus-web:21 live and healthy.
