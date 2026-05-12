# Deploy Report: fred-dev:191 / harness:29
**ADO Issues:** #3298, #3299, #3300, #3301  
**Date:** 2026-05-12  
**Deployed by:** Rhodey (DevOps subagent)  
**HEAD commit:** `1ed77b5b`

---

## Summary

Full deploy of fred-dev:191 / harness:29 covering four ADO fixes. Service reached STABLE + HEALTHY.

---

## Changes Deployed

| ADO | Title |
|-----|-------|
| #3298 | fix: chown /workspace to harness user — writable for non-root harness process |
| #3299 | fix: getUserTokens log (Blazor) |
| #3300 | fix: KbFlags (Blazor) |
| #3301 | fix: list_files — replace direct DB with Blazor internal API |

---

## Steps Executed

### Pre-flight ✅
- AWS identity verified: `fortress-tools-deployer` (account 742932328420)
- HEAD commit confirmed: `1ed77b5b`
- Git push to origin/main: confirmed (5ce678c3..1ed77b5b)

### Step 2: CodeBuild (Blazor) ✅
- Build ID: `fip-fait-build:3df2cf40-feb7-427c-b03c-2627a8f4a51f`
- Status: **SUCCEEDED**

### Step 3: Harness Docker Build ✅
- Image: `fait-v2-agent-harness:1ed77b5b`
- Build: SUCCEEDED (--no-cache)
- ECR push digest: `sha256:1c6bd410abd9f26bcc9ba42361b5e8378845c4338180f2c85138334910b206f8`
- ECR tag: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fait-v2-agent-harness:1ed77b5b`

### Step 4: Register harness:29 ✅
- Cloned from harness:28
- Updated image → `fait-v2-agent-harness:1ed77b5b`
- Preserved env vars: `INTERNAL_API_TOKEN`, `FAIT_BASE_URL`, `BRAVE_SEARCH_API_KEY`
- ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:29`

### Step 5: Register fred-dev:191 ✅
- Cloned from fred-dev:190
- Updated `Fargate__TaskDefinition` → `arn:aws:ecs:us-east-1:742932328420:task-definition/fait-v2-agent-harness:29`
- ARN: `arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:191`

### Step 6: Deploy ✅
- `aws ecs update-service` with `fred-dev:191 --force-new-deployment`
- Stopped 1 running harness task (a124e5bfc741)
- `aws ecs wait services-stable` → **STABLE**

---

## Verification

```
taskDefArn:   arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:191
lastStatus:   RUNNING
healthStatus: HEALTHY
```

---

## Rollback

```bash
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:190 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

---

## Cost Impact
No change — same Fargate resources, same task sizing.
