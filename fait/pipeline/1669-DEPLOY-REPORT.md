# Deploy Report: WI #1669 — Email Fabrication Fix

**Date:** 2026-04-08  
**Deployer:** War Machine (Rhodey / devops)  
**Work Item:** FAIT #1669  
**Status:** ✅ SUCCEEDED

---

## What Deployed

- `AssistantConfigService.cs` — User email injected into system prompt from Entra claims
- `ChatView.razor` — Anti-fabrication block + conditional "email in context" sentence

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Previous task def | `fred-dev:124` |
| Previous ECR digest | `sha256:e9f852dc69ae3157c99d8e0de4c4eb95a5a12bf4cea2f3af8779773309748a6c` |
| ECR image pushed | 2026-04-08T14:20:37 EDT |
| Service status | ACTIVE, 1 running, 0 pending |

---

## Build

| Field | Value |
|-------|-------|
| CodeBuild project | `fip-fait-build` |
| Build number | **#187** |
| Build ID | `fip-fait-build:363dd49c-2feb-4cb9-80d0-2d8d9c8a5687` |
| Source branch | `main` |
| Build start | 2026-04-08T16:44:08 EDT |
| Build end | ~2026-04-08T16:46:21 EDT |
| Duration | ~2m 13s |
| Status | ✅ SUCCEEDED |

---

## Deployment

| Field | Value |
|-------|-------|
| New ECR digest | `sha256:b315504be733b3d824da1cad9637ad42512560a92e55f6ddcd726974837dfd89` |
| ECR pushed | 2026-04-08T16:46:09 EDT |
| New task def | **`fred-dev:125`** |
| Task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:125` |
| ECS cluster | `fortress-tools-cluster` |
| ECS service | `fred-dev` |
| Force new deployment | ✅ Yes |
| Steady state reached | 2026-04-08T16:50:52 EDT (~3m 48s) |

---

## Health Check

| Field | Value |
|-------|-------|
| Task status | RUNNING |
| Container health | ✅ HEALTHY |
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/7808416a821f4993bc4d8086f8d1b2a2` |
| Started at | 2026-04-08T16:49:23 EDT |

---

## CloudWatch

Clean startup. All `fail:` entries are expected idempotent schema checks — all followed by `already applied` confirmations. No real errors.

Key markers:
- `Database initialization complete` ✅
- `Now listening on: http://[::]:8080` ✅
- `Application started` ✅
- MCP servers (devops, brave, m365) all `→ 200` ✅

---

## Rollback Plan

**Rollback task def:** `fred-dev:124`  
**Previous digest:** `sha256:e9f852dc...`

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:124 \
  --force-new-deployment \
  --region us-east-1
aws ecs wait services-stable \
  --cluster fortress-tools-cluster \
  --services fred-dev \
  --region us-east-1
```

**Rollback SLA:** < 5 minutes

---

## Notes

- No schema changes in this deploy
- DB init `fail:` entries are **non-fatal and expected** — all idempotent
- Two startup cycles visible in CloudWatch (normal ECS blue-green replacement behavior)
- `fred-dev:125` is a clone of `:124` — same spec, new image digest pulled at task launch
