# Deploy Report — ADO#3285 / #3286 / #3287 / #3288 / #3289

**Date:** 2026-05-11  
**Deployed by:** Rhodey (DevOps subagent)  
**Commit:** `6f612ed3`  
**Target:** fred-dev:190 / harness:28  
**Previous:** fred-dev:189 / harness:27  

---

## Fixes Deployed

| ADO | Description |
|-----|-------------|
| #3285 | `_wasColdStart` set on cross-chat navigation to trigger resumption brief |
| #3286 | MCP token userId normalization + Brave proxy internal URL fix |
| #3287 | KB chip SSE visibility + TeamIds membership intersection |
| #3288 | Structured logging on internal token auth + getUserTokens response body |
| #3289 | CC spawn comprehensive logging — stdout/stderr/exit code/startup check |

---

## Build Artifacts

### Blazor (fred-chat / fip-fait-build)
- **CodeBuild ID:** `fip-fait-build:b0578cd0-5609-4d4c-8ddd-648657827678`
- **Status:** SUCCEEDED
- **ECR Tag:** `fred-chat:kb-latest`
- **ECR Digest:** `sha256:3134c8b776f66f15af6c5744753722e873affa0657df479bb8e7af68985ab571`

### Harness (fait-v2-agent-harness)
- **Built from:** commit `6f612ed3` (local Docker build, `--no-cache`)
- **ECR Tag:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:6f612ed3`
- **ECR Digest:** `sha256:ed2821a025e475065097c6361b358c551989ea5f141868bb15f83ca00b149f37`

---

## Task Definitions

| Resource | ARN |
|----------|-----|
| harness:28 | `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:28` |
| fred-dev:190 | `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:190` |

**Changes:**
- harness:28: image updated to `fait-v2-agent-harness:6f612ed3` (all env vars preserved: INTERNAL_API_TOKEN, FAIT_BASE_URL, BRAVE_SEARCH_API_KEY)
- fred-dev:190: `Fargate__TaskDefinition` updated to `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:28`

---

## ECS Deployment

- **Service:** `fortress-tools-cluster / fred-dev`
- **Update:** `fred-dev:190` with `--force-new-deployment`
- **Pre-existing harness tasks stopped:** 1 task (`ce78d4dee4fb417480fde93ffb3ad02d`)
- **`aws ecs wait services-stable`:** ✅ STABLE
- **Final verification:**
  - Task ARN: `de0192c6ed204386bbb5adfc09258765`
  - Task Def: `fred-dev:190` ✅
  - Status: `RUNNING` ✅
  - Health: `HEALTHY` ✅

---

## Rollback

If needed:
```bash
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:189 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```
