# Deploy Report: WI #1654 — Pre-populate narrative + existing files

**Date:** 2026-04-08  
**Time:** 13:59–14:04 EDT  
**Deployed by:** War Machine (Rhodey / devops)  
**App:** nexus-web  
**Environment:** Production (fortress-tools-cluster)

---

## What's Deploying

- Step 1 existing files list in `NewSpecWizard.razor` (resume mode)
- `_filesToDelete` HashSet + `RemoveExistingFile` method
- `TotalFileCount` computed property

---

## Pre-Deploy Snapshot

- **Previous task def:** `nexus-web:18`
- **Service state:** running=1, desired=1, ACTIVE
- **Resolved commit (HEAD):** `b044cec4e7e90f9eaedbd8afca1260ad21f8274c`

---

## Steps Completed

1. ✅ **Pre-deploy snapshot** — `nexus-web:18`, running=1/desired=1
2. ✅ **CodeBuild started** — `fip-nexus-build:5db49f09-e1d0-4c5f-a7fb-95bf4674ae85` (build #16)
3. ✅ **CodeBuild SUCCEEDED** — resolved `b044cec4e7e90f9eaedbd8afca1260ad21f8274c`, duration ~75s
4. ✅ **Force-new-deployment triggered** — `nexus-web:18` redeployed with fresh image
5. ✅ **ECS service stabilized** — running=1, desired=1, PRIMARY deployment only
6. ✅ **Health check** — `https://nexus.fortressam.ai/` → HTTP 403 ✅
7. ✅ **CloudWatch** — clean startup: EF migrations ran + completed, no exceptions

---

## Deployment Timing

| Event | Time (EDT) |
|-------|-----------|
| Build started | 13:59:52 |
| Build completed | 14:01:08 |
| Force-new-deployment | ~14:01:15 |
| Service stable | ~14:04 |
| Health check | ~14:04 |

**Total duration:** ~5 minutes

---

## Final State

- **Task def:** `nexus-web:18`
- **Resolved commit:** `b044cec4e7e90f9eaedbd8afca1260ad21f8274c` (HEAD of main)
- **Build ID:** `fip-nexus-build:5db49f09-e1d0-4c5f-a7fb-95bf4674ae85` (#16)
- **Health:** HTTP 403 (expected — auth-protected)
- **ECS:** running=1/desired=1, ACTIVE

---

## Rollback Plan

### Pre-Deploy State
- Task def: `nexus-web:18`

### Rollback Command
```bash
aws ecs update-service --cluster fortress-tools-cluster --service nexus-web \
  --task-definition nexus-web:18 --force-new-deployment \
  --profile fortress-tools-deployer --region us-east-1
```

> Note: Since this deployment reused `nexus-web:18` with a force-new-deployment, rollback would require re-deploying from the prior image tag. The previous stable state is the same task definition revision.

---

_War Machine out. 🤖_
