# Deploy Report: WI#912 — FAM OS UAT Fixes

**Agent:** War Machine (Rhodey)  
**Date:** 2026-03-20  
**Deploy Time:** 10:29 – 10:35 EDT (~6 minutes)  
**Target:** famos-dev (https://famos.dev.fortressam.ai)

---

## Pre-Deploy Snapshot

| Item | Value |
|------|-------|
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/famos-dev:3` |
| Running Tasks | 1 |
| Baseline Revision | `famos-dev:3` |
| Commit Deployed | `a4ffa2f` |

---

## What Was Deployed

3-file surgical UAT fix in `famos/src/FamOs.Web/` (commit `a4ffa2f`):
- **CSS:** `.famos-btn-primary-sm` bg/color/hover corrected
- **OpportunityCreateDialog:** `InitialCompanyName` parameter added
- **Accounts.razor:** Smart routing logic (0/1/2+ opportunities)

---

## Deploy Steps

| Step | Status | Details |
|------|--------|---------|
| ADO pre-deploy comment | ✅ | Comment ID 726797 posted |
| Pre-deploy snapshot | ✅ | `famos-dev:3`, 1 task running |
| CodeBuild triggered | ✅ | `fip-famos-build:7e5bf971-8138-4b5c-a845-9d1893a4fa9f` |
| CodeBuild result | ✅ SUCCEEDED | ~2 min build time (10:29–10:31) |
| ECS force-new-deployment | ✅ | PRIMARY deployment triggered |
| Service stable | ✅ | Stable at 10:35:03 EDT |
| Health check `/health` | ✅ 200 | Pass |
| Health check `/` | ✅ 302 | Pass |
| ADO post-deploy comment | ✅ | Comment ID 726802 posted |

---

## Post-Deploy State

| Item | Value |
|------|-------|
| Task Definition | `arn:aws:ecs:us-east-1:742932328420:task-definition/famos-dev:3` |
| Running Tasks | 1 |
| Image | New build from commit `a4ffa2f` |
| Health `/health` | 200 OK |
| Health `/` | 302 Redirect |

> **Note:** Task definition revision remained at `:3`. CodeBuild pushed a new container image to ECR; ECS pulled the latest image on force-new-deployment. Revision only increments when the task definition JSON itself changes.

---

## Rollback Plan

If Natasha finds issues, execute immediately:

```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service famos-dev \
  --task-definition famos-dev:3 \
  --force-new-deployment \
  --region us-east-1
```

> Pre-deploy baseline was `famos-dev:3` with the previous image. To restore the exact prior image, identify the previous ECR image tag from the CodeBuild history and update the task definition to pin it before rolling back.

---

## Outcome

**✅ DEPLOY COMPLETE — Handing to Natasha for QA**

- CodeBuild: SUCCEEDED
- ECS: Stable, 1 task running
- Health checks: All pass
- ADO WI#912: Updated with deploy start + complete comments

---

## Fix Deploy

**Triggered by:** WI#912 fix — `Accounts.razor` async onclick correction  
**Commit:** `c3c922f` (builds on `a4ffa2f`)  
**Date:** 2026-03-20  
**Deploy Time:** 10:50 – 10:56 EDT (~6 minutes)

### Pre-Deploy Baseline
| Item | Value |
|------|-------|
| Baseline Task Definition | `famos-dev:3` |
| Reason | Re-deploy after async onclick fix in `Accounts.razor` |

### Fix Deploy Steps

| Step | Status | Details |
|------|--------|---------|
| ADO pre-deploy comment | ✅ | Comment ID 726819 posted |
| CodeBuild triggered | ✅ | `fip-famos-build:af51cf9c-9470-4b28-986d-9b63d5e46ddb` |
| CodeBuild result | ✅ SUCCEEDED | ~2 min (10:50–10:52 EDT) |
| ECS force-new-deployment | ✅ | PRIMARY deployment triggered |
| Service stable | ✅ | Stable ~10:56 EDT |
| Health check `/health` | ✅ 200 | Pass |
| ADO post-deploy comment | ✅ | Comment ID 726824 posted |

### Rollback Plan
```bash
source ~/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service famos-dev \
  --task-definition famos-dev:3 --force-new-deployment --region us-east-1
```

### Outcome
**✅ FIX DEPLOY COMPLETE — Handing to Natasha for re-check**
- Commit `c3c922f` live on famos-dev
- Health: 200 OK
- ADO WI#912: Updated
