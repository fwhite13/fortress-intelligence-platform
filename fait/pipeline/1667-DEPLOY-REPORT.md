# Deploy Report — FAIT WI #1667 — KB Notes S3 Fix

**Date:** 2026-04-08  
**Deployer:** War Machine (Rhodey / devops)  
**ECS Service:** `fred-dev` on `fortress-tools-cluster`

---

## Summary

Deployed KB Notes S3 sync fix (`ForgeService.cs`) to `fred-dev`. Notes are now written to S3 (`kb-docs/{tier}/{userId}/note-{id}.txt`) with metadata JSON, and Bedrock KB ingestion is triggered on create/update/delete.

---

## Pre-Deploy Snapshot

| Field | Value |
|-------|-------|
| Task Def (before) | `fred-dev:123` |
| Image (before) | `fred-chat:ab122c76970647c14f561a8466718cb099ef6d00` |
| Image Digest (before) | `sha256:db697cd5ce38f5f6b95c2059df4d02ab8f6e7e9b8e84f363815e93b8a5b09f5f` |

---

## Deploy Steps

### 1. ✅ CodeBuild Started
- **Build ID:** `fip-fait-build:86bc846c-98f6-4901-b506-2381bc287b8e`
- **Started:** 2026-04-08 14:18:45 EDT

### 2. ✅ CodeBuild SUCCEEDED
- **Completed:** ~14:20:41 EDT (~2 min)
- **Commit deployed:** `163f4c3` — `feat(fait#1667): sync KB notes to S3 on create/update/delete to enable Bedrock retrieval`
- **Image pushed to ECR:** `fred-chat:kb-latest` + `fred-chat:163f4c3...`

### 3. ✅ Task Def Updated
- **Note:** Prior task def `fred-dev:123` used commit-SHA image tag. Registered new task def with `fred-chat:kb-latest`.
- **New Task Def:** `fred-dev:124`
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:kb-latest`

### 4. ✅ ECS Service Updated
- `fred-dev` set to `fred-dev:124` with `--force-new-deployment`
- Service reached **STABLE** state

### 5. ✅ Health Check
- **Endpoint:** `https://fait.dev.fortressam.ai`
- **Response:** `403` (auth-gated — expected; confirms service is up and routing correctly)
- **New Image Digest:** `sha256:e9f852dc69ae3157c99d8e0de4c4eb95a5a12bf4cea2f3af8779773309748a6c`

---

## Post-Deploy State

| Field | Value |
|-------|-------|
| Task Def (after) | `fred-dev:124` |
| Image Tag | `fred-chat:kb-latest` |
| Image Digest | `sha256:e9f852dc69ae3157c99d8e0de4c4eb95a5a12bf4cea2f3af8779773309748a6c` |
| Service Status | ACTIVE, 1/1 running |

---

## Rollback Instructions

If rollback needed, revert to `fred-dev:123`:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:123 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

---

_War Machine — 2026-04-08_
