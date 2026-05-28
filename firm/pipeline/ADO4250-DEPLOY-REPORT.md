# ADO#4250 Deploy Report — FIRM: Swap AzureAd__ClientId in ECS Task Definition

**Date:** 2026-05-27
**Deployed by:** devops subagent (rhodey-ado4250)
**Risk level:** Low — config-only, no code change, no image rebuild

---

## Summary

Swapped `AzureAd__ClientId` and updated `AzureAd__ClientSecret` in the FIRM ECS task definition. No image change. Config-only update.

---

## Changes Applied

| Env Var | Old Value | New Value |
|---------|-----------|-----------|
| `AzureAd__ClientId` | `a2de171d-5bb8-4db0-87a6-d07e24b932b3` | `eda4d502-8c93-422e-b7fb-bb922a2a472e` |
| `AzureAd__ClientSecret` | `9V-8Q~FM...` (old) | `jN98Q~8d...` (copied from `Azure__ClientSecret`) |

All other env vars unchanged.

---

## Task Definition

| | ARN |
|--|-----|
| **Rollback (pre-deploy)** | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:132` |
| **Deployed** | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:133` |

---

## ECS Service Status

- **Cluster:** `fortress-tools-cluster`
- **Service:** `firm-web`
- **Task def on service:** `firm-web:133` ✅
- **Running:** 1 / Desired: 1 / Pending: 0 ✅
- **Old task def (`:132`):** fully drained — 0/0/0 ✅

---

## Rollback

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service \
  --cluster fortress-tools-cluster \
  --service firm-web \
  --task-definition firm-web:132 \
  --force-new-deployment \
  --region us-east-1 \
  --profile fortress-tools-deployer
```

---

## Verification

- Old `AzureAd__ClientId` confirmed as `a2de171d-5bb8-4db0-87a6-d07e24b932b3` before deploy ✅
- New `AzureAd__ClientId` = `eda4d502-8c93-422e-b7fb-bb922a2a472e` ✅
- `AzureAd__ClientSecret` updated from `Azure__ClientSecret` source value ✅
- ECS stable on `firm-web:133` with 1 running task ✅
