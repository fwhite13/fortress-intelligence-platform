# Deploy Report — ADO#3157
**Date:** 2026-05-09 21:02 EDT  
**Deployed by:** War Machine (Rhodey, devops subagent)  
**Session:** agent:devops:subagent:1128b0b1-efe2-4b99-ba63-286074906df7

---

## Summary

Deployed the one-line fix for ADO#3157: removed premature `OnboardingCompletedAt` assignment from `AssistantSetup.razor`. `ProvisionAsync` now owns this flag exclusively, restoring the idempotency guard so S3 workspace seeding runs correctly on first-time user provisioning.

---

## What Changed

**Commit:** `6de82e9e`  
**Fix:** `fix(fait#3157): remove premature OnboardingCompletedAt set — ProvisionAsync owns this after S3 writes`

Lines 348-349 removed from `AssistantSetup.razor OnValidSubmit`:
- `user.OnboardingCompletedAt = DateTime.UtcNow;`  
- `user.OnboardingStep = 0;`

`ProvisionAsync` sets `OnboardingCompletedAt` as its final step after S3 writes succeed — the Razor page should never set it.

---

## Deploy Steps

### 1. ✅ Docker Build
- Built from monorepo root: `cd /home/fredw/projects/fip && docker build --no-cache -f fait/Dockerfile -t fred-chat:6de82e9e .`
- Build successful, image digest: `sha256:8b214b84d0abd5f7212711d5c1ee1865e786356c747455924a475e4f06b3c2db`

### 2. ✅ ECR Push
- Tagged: `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:6de82e9e`
- Pushed successfully to ECR

### 3. ✅ Task Definition Registered
- Cloned `fred-dev:147`, updated image to `fred-chat:6de82e9e`
- All env vars preserved — **`Fargate__ContainerName = fait-v2-agent-harness` ✅**
- `taskRoleArn`: `arn:aws:iam::742932328420:role/fortress-tools-ecs-task-role` ✅
- Registered as: **`arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:148`**

### 4. ✅ ECS Service Updated
- Command: `aws ecs update-service --cluster fortress-tools-cluster --service fred-dev --task-definition fred-dev:148 --force-new-deployment`
- Stabilized: RUNNING=1, PENDING=0, DESIRED=1

### 5. ✅ CloudWatch Verified
- Log stream: `ecs/fred/d433beb6d9ce4ba6a5be4b84bc714f53`
- Clean startup: `Application started. Press Ctrl+C to shut down.`
- DB initialization complete, no errors
- All MCP servers healthy (devops, brave, m365)
- `Fargate__ContainerName = fait-v2-agent-harness` preserved and active

### 6. ✅ ADO#3157 Resolved
- State set to **Resolved**
- Comment added with deploy details

---

## Resources

| Resource | Value |
|----------|-------|
| Image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fred-chat:6de82e9e` |
| Image Digest | `sha256:8b214b84d0abd5f7212711d5c1ee1865e786356c747455924a475e4f06b3c2db` |
| Task Definition | `fred-dev:148` |
| Previous Task Def | `fred-dev:147` (image `fred-chat:ba30f846`) |
| ECS Cluster | `fortress-tools-cluster` |
| ECS Service | `fred-dev` |

---

## Cost Impact

No change — same Fargate CPU/memory allocation (1024 CPU / 2048 MB).

---

## Rollback

If rollback needed:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service fred-dev \
  --task-definition fred-dev:147 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```
