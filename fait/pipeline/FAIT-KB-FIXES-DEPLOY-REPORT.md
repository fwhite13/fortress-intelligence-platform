# Deploy Report — FAIT KB Fixes
**Date:** 2026-03-11  
**Environment:** fred-dev  
**Deployed by:** War Machine (Rhodey) / devops agent  
**Commit:** `bb3838e` on `main`

---

## What Shipped

1. `IBrowserFile` bytes-first fix in `UploadPersonalDocument` + `UploadTeamDocument` (SignalR pipe issue)
2. PPTX auto-conversion to Markdown via OpenXml before S3 upload
3. `ConvertPptxToMarkdown` wrapped in try/catch for corrupted files
4. `ListDocumentsAsync` userId guard + Snackbar error surfacing

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/d5ded807d030426cb8d307f94a848208` |
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:62` |
| Image Digest (before) | `sha256:3de5a413fe97c3fa38a5a1939410dbc9f23e7c1790f04a809eb347a2d3c9028b` |
| ECR Tag | `fred-chat:kb-latest` |

---

## Build

| Field | Value |
|-------|-------|
| Build ID | `fip-fait-build:a7dc9782-8c40-4c64-be01-ad24dec4fa17` |
| Final Status | **SUCCEEDED** |
| Duration | ~1.5 minutes (11:33:55 → 11:35:26) |
| New Image Tag | `bb3838ee540a4cff88f69dd5e5005e448fff2101` + `kb-latest` |
| New Image Digest | `sha256:83b67bb89167e327d0c0ea41726bd844ec318357e2cfad60c4fda092f569be20` |
| Image Pushed At | 2026-03-11T11:35:10 EDT |

---

## ECS Deployment

| Field | Value |
|-------|-------|
| Deployment method | `force-new-deployment` on `fred-dev:62` (mutable `kb-latest` tag) |
| Task Definition Revision | `fred-dev:62` (unchanged — image tag is mutable `kb-latest`) |
| New Image Digest | `sha256:83b67bb89167e327d0c0ea41726bd844ec318357e2cfad60c4fda092f569be20` |
| Health Status | **HEALTHY** |
| Confirmed at | 11:36:46 EDT |

> **Note:** The service uses a mutable ECR tag (`kb-latest`) pinned to task def `fred-dev:62`. CodeBuild pushes a new image to `kb-latest`; ECS force-new-deployment pulls the updated image. No new task definition revision is created — the revision change is in the image layer only (confirmed via digest comparison).

---

## Health Check Result

✅ **PASS** — Task running on `fred-dev:62` with new digest `sha256:83b67bb8` reporting `HEALTHY` at first poll (20s post-deployment).

---

## Rollback Commands

If rollback is needed, the previous image had digest `sha256:3de5a413`. Since the service uses a mutable tag, rollback requires re-tagging or forcing the previous task definition:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer

# Option A: Force previous task definition revision (if :61 or earlier had a different image)
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:62 \
  --region us-east-1 \
  --force-new-deployment

# Option B: Re-tag the old image digest as kb-latest in ECR, then force-deploy
# First, find the old image:
# aws ecr list-images --repository-name fred-chat --region us-east-1
# Then re-tag it as kb-latest, then run:
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:62 \
  --region us-east-1 \
  --force-new-deployment
```

> **Recommended rollback path:** Contact the pipeline team to re-push the prior image to `kb-latest` tag, then run Option A above.

---

## Summary

| Stage | Result |
|-------|--------|
| Pre-deploy snapshot | ✅ Captured |
| CodeBuild | ✅ SUCCEEDED (`fip-fait-build:a7dc9782`) |
| ECS force-deploy | ✅ Triggered |
| New image on ECS | ✅ `sha256:83b67bb8` (was `sha256:3de5a413`) |
| Health check | ✅ HEALTHY |
| **Overall** | ✅ **DEPLOYED** |
