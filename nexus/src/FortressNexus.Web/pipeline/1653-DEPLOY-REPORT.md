# Deploy Report — WI #1653 — NewSpecWizard Resume Plumbing

**Date:** 2026-04-08  
**Deployer:** War Machine (Rhodey / devops)  
**App:** nexus-web  
**Cluster:** fortress-tools-cluster  
**Work Item:** [FAIT #1653](https://dev.azure.com/FortressAM/FAIT/_workitems/edit/1653)

---

## Summary

Deployed WI #1653 — `NewSpecWizard` resume plumbing. New route `/nexus/{id:int}/resume` is live with `ResumeSubmissionId` parameter, on-init resume load logic, and `UserContextService.IsAdminAsync()` addition.

---

## Pre-Deploy Snapshot

| Property | Value |
|----------|-------|
| Previous task def | `nexus-web:17` |
| Previous commit | `bcbf62d` |
| Previous image tag | `bcbf62dc379de062d1ef273f660e228589b26d12` |
| Previous image digest | `sha256:0858d90a56ffdc796f6aa728a59c263a1bf2adec640b098385d6471b2475437a` |

---

## Build

| Property | Value |
|----------|-------|
| CodeBuild project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:24f43f7d-b524-489b-8414-43bbd453e12d` |
| Build status | ✅ SUCCEEDED |
| Build started | 2026-04-08T13:38:16 EDT |
| Build ended | 2026-04-08T13:39:35 EDT |
| Duration | ~1m 19s |
| Resolved commit | `ad07edfc4eb59b69ca10a93f13ad548b704ca576` |

---

## New Image

| Property | Value |
|----------|-------|
| ECR repo | `nexus-web` |
| Image tag | `ad07edfc4eb59b69ca10a93f13ad548b704ca576` |
| Image digest | `sha256:72062af78c9570eda7e1e982709b2c344465af03628ced7f4aa86b4d2411e538` |
| Pushed at | 2026-04-08T13:39:32 EDT |

---

## Deployment

| Property | Value |
|----------|-------|
| New task def | `nexus-web:18` |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:18` |
| ECS service update | ✅ Force new deployment |
| Steady state | ✅ Reached |
| Task started at | 2026-04-08T13:42:34 EDT |
| Task status | RUNNING |
| Health status | HEALTHY |

---

## Health Check

| Check | Result |
|-------|--------|
| `curl https://nexus.fortressam.ai/` | ✅ HTTP 403 (expected — auth protected) |
| CloudWatch exceptions | ✅ None |
| EF Core migrations | ✅ Completed cleanly on startup |
| Startup warnings | ⚠️ HTTP_PORTS override (benign, pre-existing) |

---

## What Deployed

- **New route:** `GET /nexus/{id:int}/resume` on `NewSpecWizard.razor`
- **Parameter:** `ResumeSubmissionId` with on-init resume load logic
- **Service addition:** `UserContextService.IsAdminAsync()`
- **Schema changes:** None — no migration

---

## Rollback

If needed, roll back to `nexus-web:17`:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:17 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```

---

_War Machine out. nexus-web:18 is live and healthy._
