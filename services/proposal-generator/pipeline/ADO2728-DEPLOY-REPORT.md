# ADO#2728 — Deploy Report

**Date:** 2026-05-04  
**Deployer:** War Machine (Rhodey)  
**Service:** `proposal-generator-dev` on `fortress-tools-cluster`  
**Commit:** `fc62a2e`  
**Changes:** pg5 classification schedule row height fix, pg7-9 outline level suppression  

---

## Steps Completed

1. ✅ **Pre-deploy snapshot** — Previous task def: `proposal-generator-dev:32` (rollback target)
2. ✅ **ADO pre-flight comment** — Posted (comment id 773669)
3. ✅ **ECR login** — `fortress-tools-deployer` credentials confirmed
4. ✅ **Docker build** — `--no-cache`, built from `services/proposal-generator/Dockerfile` (monorepo root context)
5. ✅ **ECR push** — `fip-proposal-generator:fc62a2e` + `fip-proposal-generator:latest`
   - Digest: `sha256:fcfb06d62afc7c903c98d8f26c0170403a987d5b16d9b6aabff820d3cc479ee4`
6. ✅ **Task definition registered** — `proposal-generator-dev:33`
   - Image pinned to `fip-proposal-generator:fc62a2e`
7. ✅ **ECS service updated** — `--force-new-deployment` with task def `:33`
8. ✅ **Health check** — RUNNING 1/1, `/health` → **200**
9. ✅ **ADO post-deploy comment** — Posted (comment id 773729)

---

## Summary

| Field | Value |
|---|---|
| ECR Image | `fip-proposal-generator:fc62a2e` |
| Digest | `sha256:fcfb06d62afc7c903c98d8f26c0170403a987d5b16d9b6aabff820d3cc479ee4` |
| Task Definition | `proposal-generator-dev:33` |
| ECS Status | RUNNING 1/1 |
| Health | 200 OK |
| Rollback Target | `proposal-generator-dev:32` |

---

## Rollback Procedure

```bash
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service proposal-generator-dev \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/proposal-generator-dev:32 \
  --force-new-deployment \
  --profile fortress-tools-deployer \
  --region us-east-1
```
