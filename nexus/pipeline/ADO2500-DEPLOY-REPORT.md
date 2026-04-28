# ADO#2500 Deploy Report — NexusArtifacts UI + WorkItemRecord.Description

**Deployed by:** War Machine (James Rhodes)  
**Date:** 2026-04-28  
**Time:** 14:38–14:44 EDT  
**Status:** ✅ COMPLETE — HEALTHY

---

## Pre-Deploy Snapshot

| Field | Value |
|---|---|
| Service | `nexus-web` on `fortress-tools-cluster` |
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/nexus-web:46` |
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web:latest` |
| Pre-deploy Image Digest | `sha256:12c75134564c4ee526811e15fb17f228c7c4370ea0e7dbf026a71d7be948ca84` |
| Running / Desired | 1 / 1 |
| Rollout State | COMPLETED |
| Deploy ID | `ecs-svc/8070489144643507256` |

---

## Build

| Field | Value |
|---|---|
| CodeBuild Project | `fip-nexus-build` |
| Build ID | `fip-nexus-build:ecec38c0-aa93-40cd-b2a8-8fb8fa94f89d` |
| Build Status | **SUCCEEDED** |
| Build Start | 2026-04-28T14:38:49 EDT |
| Build End | 2026-04-28T14:40:16 EDT |
| Source Commit | `ee768dece053eb513e7f1f1ebea698a8671ae534` |
| New Image Tag | `ee768dece053eb513e7f1f1ebea698a8671ae534` |
| New Image Digest | `sha256:e93c0b9f929f7fe8cb1da04d72aec3f583c6aa4d0065b9307ee55f34a1d309e5` |

> **Note:** CodeBuild resolved commit `ee768de` (the merge/HEAD of `eb0d1da` fix cycle 2 into main).

---

## ECS Deployment

| Field | Value |
|---|---|
| Deploy ID | `ecs-svc/9657650594539771386` |
| Task Definition | `nexus-web:46` (force-new-deployment pulled fresh image via `latest` tag) |
| AzureAd env vars | ✅ Present (`AzureAd__ClientId`, `AzureAd__ClientSecret`, `AzureAd__TenantId`) |
| Rollout State | **COMPLETED** |
| Running Task | `3afc59219ebc46e0a1457061109fcae6` |
| Task Start | 2026-04-28T14:42:42 EDT |

---

## CloudWatch Migration Confirmation

```
[18:42:31 INF] [NEXUS] Running EF Core migrations on startup...
[18:42:32 INF] [NEXUS] EF Core migrations complete.
```

✅ `description` column migration applied to `work_item_records` on startup.

---

## Post-Deploy Health Check

| Check | Result |
|---|---|
| Service running count | 1/1 ✅ |
| Task status | RUNNING ✅ |
| Task health | HEALTHY ✅ |
| Rollout state | COMPLETED ✅ |
| Image digest matches ECR push | `sha256:e93c0b9f...` ✅ |
| EF Core migrations | APPLIED ✅ |
| New ERR entries | None ✅ |
| Warnings | 1x HTTP_PORTS WRN (pre-existing, benign) |

---

## Rollback Commands

If rollback is needed, revert to previous task definition `nexus-web:46` (same task def — the force-new-deployment pulled a new image under the same TD revision). To roll back to the pre-deploy image:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer

# Step 1: Register a new task def revision pinned to the pre-deploy image digest
aws ecs describe-task-definition \
  --task-definition nexus-web:46 \
  --region us-east-1 \
  --query 'taskDefinition' \
  --output json > /tmp/nexus-web-46-td.json

# Edit /tmp/nexus-web-46-td.json to pin image to pre-deploy digest:
# "image": "742932328420.dkr.ecr.us-east-1.amazonaws.com/nexus-web@sha256:12c75134564c4ee526811e15fb17f228c7c4370ea0e7dbf026a71d7be948ca84"

# Step 2: Register pinned task def
aws ecs register-task-definition \
  --cli-input-json file:///tmp/nexus-web-46-td.json \
  --region us-east-1

# Step 3: Update service to pinned revision (replace :47 with registered revision)
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service nexus-web \
  --task-definition nexus-web:47 \
  --force-new-deployment \
  --region us-east-1

# Note: EF migration for description column is additive (nullable column).
# Rolling back the app does NOT require reversing the DB migration —
# the old code will simply ignore the new column.
```

---

## What Was Deployed

- **NexusArtifacts UI** — new page (`/nexus-artifacts`) with Test Case grouping, WI template badges, predecessor badges, external dependency panel
- **WorkItemRecord.Description** — new EF migration adds `description` column to `work_item_records`
- **New files:** `Components/Pages/NexusArtifacts.razor`, `Controllers/NexusArtifactsController.cs`
- **Modified:** `Components/Pages/SubmissionDetail.razor`, `Models/Entities/WorkItemRecord.cs`, `Data/NexusDbContext.cs`
- **Review:** Hawkeye cycle 2 PASS — all 9 issues confirmed fixed

---

_War Machine out._
