# Deploy Report — ADO#2827
## fip-mcp S3 write fix: add_to_kb S3 content write + metadata sidecar, search_kb filter key alignment

**Deploy Agent:** War Machine (Rhodey)
**Date:** 2026-05-07
**Commit:** `5457c22`
**Build Cycle:** C1 PASS (Clint)

---

## Deploy Result: ✅ SUCCEEDED

---

## Pre-Deploy Snapshot

| Property | Value |
|----------|-------|
| Service | `fip-mcp` |
| Previous task def | `fip-mcp:6` |
| Running count | `1` |
| Desired count | `1` |
| Image (pre-deploy) | `fip-mcp:ee21c6d` |

---

## Steps Completed

### Step 1: Commit verification
- `5457c22` confirmed as HEAD on local `main`
- Branch was **3 commits ahead of origin/main** (commits: 2827, 2889 fixes)
- Pushed to `origin/main` → `3e5612f..5457c22`

### Step 2: Docker Build + ECR Push
- Build context: `services/fip-mcp/` (service subdirectory — Dockerfile does not reference monorepo root files)
- Build command: `docker build --no-cache -t fip-mcp:5457c22 -f Dockerfile .`
- Build: ✅ SUCCEEDED
- ECR digest: `sha256:924eea320c32a12ba1d33ebeef25bae66d6bbe887aed7ce43d60f5c07a064ee2`
- Tags pushed: `fip-mcp:5457c22`, `fip-mcp:latest`

**Note on build context:** The task instructions specified building from the monorepo root (`/home/fredw/projects/fip`) with `-f services/fip-mcp/Dockerfile`, but this fails because the Dockerfile's `COPY src/ ./src/` path does not exist at monorepo root. Correct approach is to build from the service directory. This matches the prior ADO#2834 deploy pattern.

### Step 3: Task Definition Registration
- Helper script: `scripts/ecs-register-task-def.sh`
- `taskRoleArn` preserved: `arn:aws:iam::742932328420:role/fip-mcp-task-role`
  - (**Critical** — fip-mcp:7 adds S3 writes; task role required for S3 access)
- New task def: **`fip-mcp:7`**

### Step 4: ECS Service Update
- Service updated to `fip-mcp:7` with `--force-new-deployment`
- Update accepted ✅

### Step 5: Stabilization
- `aws ecs wait services-stable` → STABLE ✅
- Final state: `rolloutState: COMPLETED`, `runningCount: 1`, `desiredCount: 1`

### Step 6: Health Check
- Container logs confirm healthy startup:
  ```
  [fip-mcp] FORGE KB MCP Server v1.0.0 listening on port 3000
  [fip-mcp] Entra tenant: 7152ea12-c930-44b0-bb52-069152161c5b
  [fip-mcp] Entra client: eda4d502-8c93-422e-b7fb-bb922a2a472e
  [fip-mcp] Bedrock region: us-east-1
  [fip-mcp] Entitlements config: /app/src/config/entitlements.json
  ```
- No errors in startup logs ✅

---

## Deployment Summary

| Property | Value |
|----------|-------|
| Docker image | `fip-mcp:5457c22` |
| ECR digest | `sha256:924eea320c32a12ba1d33ebeef25bae66d6bbe887aed7ce43d60f5c07a064ee2` |
| New task def | `fip-mcp:7` |
| Previous task def | `fip-mcp:6` |
| taskRoleArn | `arn:aws:iam::742932328420:role/fip-mcp-task-role` ✅ preserved |
| Service status | ACTIVE, 1/1, rolloutState COMPLETED |

---

## What's Deployed

**ADO#2827 changes in `5457c22`:**
- `add_to_kb`: S3 content write + metadata sidecar (`.meta.json`) alongside Bedrock KB ingestion
- `search_kb`: Fixed filter key alignment

---

## Rollback Plan

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fip-mcp \
  --task-definition fip-mcp:6 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

_Deployed by War Machine (Rhodey) — 2026-05-07 ~11:20 EDT_
