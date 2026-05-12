# Deploy Report — ADO#3284 + ADO#3283

**Date:** 2026-05-11  
**Agent:** Rhodey (DevOps)  
**Commit:** `07caad49` — fix(fait#3284+#3283): write_memory HTML sanitization + teamId filter type verification  
**Scope:** Harness-only deploy (no Blazor code changes in this commit)

---

## Summary

Deployed harness image built from commit `07caad49`. Updated task definitions to wire new harness image into the Blazor service. ECS service reached STABLE/HEALTHY.

---

## Steps Executed

### 1. Pre-flight ✅
- Identity confirmed: `fortress-tools-deployer` (account `742932328420`)
- Commit `07caad49` present locally (1 commit ahead of `origin/main` at deploy time)

### 2. Docker Build ✅
- Built `fait-v2-agent-harness:07caad49` from `/home/fredw/projects/fip/fait-v2/agent-harness/`
- Used `--no-cache`
- Image digest: `sha256:ae60cf379fe620ea89edcd5817e1b0b3554c86502437b3d5be9639151963111f`

### 3. ECR Push ✅
- Pushed to `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:07caad49`
- Digest: `sha256:ae60cf379fe620ea89edcd5817e1b0b3554c86502437b3d5be9639151963111f`

### 4. Harness Task Def :26 Registered ✅
- Cloned `fait-v2-agent-harness:25`
- Updated image → `fait-v2-agent-harness:07caad49`
- Preserved env vars: `FAIT_BASE_URL`, `BRAVE_SEARCH_API_KEY`
- Registered: `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:26`

### 5. Blazor Task Def :188 Registered ✅
- Cloned `fred-dev:187`
- Updated `Fargate__TaskDefinition` → `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:26`
- Registered: `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:188`

### 6. ECS Deploy ✅
- Deployed `fred-dev` service with `fred-dev:188`, `--force-new-deployment`
- Stopped 2 running harness tasks (`:87b4b4fa`, `:ab524a8e`) so fresh ones pick up harness:26
- Waited for service stability: **STABLE**

### 7. Verification ✅
- Running task: `fred-dev:188`
- Health: **HEALTHY**

---

## Deployed Artifacts

| Artifact | Value |
|---|---|
| Harness image tag | `fait-v2-agent-harness:07caad49` |
| Harness ECR digest | `sha256:ae60cf379fe620ea89edcd5817e1b0b3554c86502437b3d5be9639151963111f` |
| Harness task def | `fait-v2-agent-harness:26` |
| Blazor task def | `fred-dev:188` |
| ECS service | `fred-dev` (fortress-tools-cluster) |

---

## Rollback

```bash
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:187 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Notes

- Commit was 1 ahead of `origin/main` at deploy time (not yet pushed to remote). Build was from local working tree.
- Blazor image unchanged — `fred-dev:188` is a re-registration of `fred-dev:187` with only `Fargate__TaskDefinition` updated.
