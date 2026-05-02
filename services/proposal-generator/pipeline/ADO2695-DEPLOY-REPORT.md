# ADO#2695 Deploy Report — proposal-generator-dev

**Date:** 2026-05-01 (20:08–20:15 EDT)  
**Deployed by:** War Machine (Rhodey)  
**WI:** ADO#2695 — Proposal Generator: NBAIS WC template — final polish (header alignment, cell vertical align, column widths)

---

## Summary

| Field | Value |
|---|---|
| Commit | `64e2dcd` |
| Branch | HEAD (main) |
| ECR Image | `fip-proposal-generator:64e2dcd` |
| ECR Digest | `sha256:7056b6e39494d74552325dfc5834533f5b2daaf829d8af37793bdf0687ff9f21` |
| Task Definition | `proposal-generator-dev:28` |
| Previous Task Def | `proposal-generator-dev:27` (rollback target) |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `proposal-generator-dev` |
| Health Check | `200 {"status":"ok","version":"1.0.0"}` |
| WI State | Closed |

---

## Deploy Steps

### 1. Pre-Deploy Snapshot
- Previous task def: `arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:27`
- ADO comment #1 posted (comment id: 769346)

### 2. Docker Build
- Command: `docker build --no-cache -f services/proposal-generator/Dockerfile .`
- Build context: monorepo root `/home/fredw/projects/fip`
- Tags: `:64e2dcd`, `:latest`
- Result: **SUCCESS**

### 3. ECR Push
- Both tags pushed successfully
- Digest: `sha256:7056b6e39494d74552325dfc5834533f5b2daaf829d8af37793bdf0687ff9f21`

### 4. Task Definition Registration
- New revision: `proposal-generator-dev:28`
- Image pinned to commit SHA `64e2dcd`

### 5. ECS Service Update
- `aws ecs update-service --force-new-deployment`
- Waited for `services-stable`: **RUNNING 1/1** ✓

### 6. Health Check
- `GET /health` via ALB → **200 OK**
- Response: `{"status":"ok","version":"1.0.0"}`

### 7. ADO Close
- Post-deploy comment posted (comment id: 769347)
- WI #2695 → **Closed**

---

## Rollback

If needed, revert to previous revision:
```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:27 \
  --profile fortress-tools-deployer --region us-east-1
```

---

_Deploy complete. All systems nominal._
