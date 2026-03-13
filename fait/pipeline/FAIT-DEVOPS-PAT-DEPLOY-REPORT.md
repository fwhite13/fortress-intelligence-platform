# Deploy Report: FAIT Azure DevOps PAT Connection
**Task:** FAIT-DEVOPS-PAT  
**Commit:** `ef2db00` — DevOps PAT connection replacing OAuth  
**Agent:** War Machine (Rhodey) — devops  
**Date:** 2026-03-12  
**Pipeline Stage:** DEPLOY  

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Previous task definition | `fred-dev:67` |
| Previous image digest | `sha256:1fb58abc…` |
| Cluster | `fortress-tools-cluster` |
| Service | `fred-dev` |
| ECR Repository | `fred-chat` |
| Image Tag | `kb-latest` |

---

## Rollback Plan

**In the event of rollback, execute:**
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service fred-dev \
  --task-definition fred-dev:67 \
  --region us-east-1 \
  --profile fortress-tools-deployer
```
Then verify: `curl -sf https://fait.dev.fortressam.ai/health`

---

## Deploy Steps

### Stage 1: CodeBuild — `fip-fait-build`

| Item | Value |
|------|-------|
| Build ID | `fip-fait-build:c87afcea-cf46-4021-977a-c49125b9c763` |
| Started | 19:09:45 |
| Completed | 19:11:48 |
| Duration | 124 seconds |
| Result | ✅ **SUCCEEDED** |

### Stage 2: ECS Force-New-Deployment

| Item | Value |
|------|-------|
| Command issued | 19:11:5x |
| Rollout reached COMPLETED | 19:14:41 |
| Desired count | 1 |
| Running count | 1 |
| Rollout state | ✅ **COMPLETED** |

### Stage 3: Digest Verification

| Item | Value |
|------|-------|
| Task ARN | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/29a03ca7caf846d3aee02d08105cb29b` |
| Running task digest | `sha256:936cc4488ef99ae370fc00b7b8355360f95e642e6deec97141f68d7d97aecf07` |
| ECR `kb-latest` digest | `sha256:936cc4488ef99ae370fc00b7b8355360f95e642e6deec97141f68d7d97aecf07` |
| Match | ✅ **DIGEST MATCH** |

### Stage 4: Health Check

| Item | Value |
|------|-------|
| URL | `https://fait.dev.fortressam.ai/health` |
| Response | `{"status":"healthy","service":"fred","timestamp":"2026-03-12T23:18:19.9388357Z"}` |
| Result | ✅ **HEALTHY** |

---

## Summary

| Stage | Status |
|-------|--------|
| CodeBuild | ✅ SUCCEEDED (124s) |
| ECS Rollout | ✅ COMPLETED (running 1/1) |
| Digest Match | ✅ MATCH |
| Health Check | ✅ HEALTHY |
| **Overall** | ✅ **DEPLOYED** |

**New image digest:** `sha256:936cc4488ef99ae370fc00b7b8355360f95e642e6deec97141f68d7d97aecf07`

---

## Notes

- Commit `ef2db00` ships Azure DevOps PAT-based connection replacing previous OAuth flow.
- Review verdict was PASS prior to this deploy.
- No issues encountered. Deployment clean end-to-end.
- Rollback target remains `fred-dev:67` if issues surface post-deploy.
