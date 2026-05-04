# ADO#2696 Deploy Report

**Title:** fix(ADO#2696): add set_table_grid to remaining 5 tables  
**Commit:** `8db3a0a`  
**Date:** 2026-05-01  
**Deployed by:** War Machine (Rhodey)

---

## Deploy Summary

| Field | Value |
|---|---|
| Service | `proposal-generator-dev` |
| Cluster | `fortress-tools-cluster` |
| ECR Repo | `fip-proposal-generator` |
| Image Tag | `8db3a0a` |
| Image Digest | `sha256:0175a32d42527a875d15eaf5de50cb84656a5c7471fd0c13f4f4ea37f7fa100b` |
| Previous Task Def | `proposal-generator-dev:29` |
| New Task Def | `proposal-generator-dev:30` |
| ECS Status | RUNNING 1/1 |
| Health Check | `/health` → 200 `{"status":"ok","version":"1.0.0"}` |

---

## Pre-Deploy State

- Task definition: `proposal-generator-dev:29`
- Service: ACTIVE, 1/1 running

## Build

- Docker build: `--no-cache` from monorepo root
- Dockerfile: `services/proposal-generator/Dockerfile`
- Base image: `node:22-alpine` with LibreOffice
- Both tags pushed: `:8db3a0a` and `:latest`

## Deployment

- New task definition registered: `proposal-generator-dev:30`
- Image pinned to commit SHA `8db3a0a`
- `force-new-deployment` triggered
- ECS stabilized: RUNNING 1/1

## Health Verification

```
GET /health (via ALB)
Host: proposal-generator.dev.fortressam.ai
Response: 200 {"status":"ok","version":"1.0.0"}
```

## Rollback Target

`proposal-generator-dev:29`

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition proposal-generator-dev:29 \
  --profile fortress-tools-deployer \
  --region us-east-1
```
