# Deploy Report — ADO#1345: FIRM FirmUser.Id HasColumnName fix

**[War Machine — DEPLOY]**
**Date:** 2026-03-29
**Deployer:** War Machine (James Rhodes)
**Status:** ✅ DEPLOYED — HEALTHY

---

## Summary

Deployed `firm-web:54` to ECS cluster `fortress-tools-cluster`, service `firm-web`.
Fix: `HasColumnName("id")` + `HasColumnType("char(36)")` for `FirmUser.Id` in `FirmDbContext.cs`.
Resolves `NullReferenceException` on all `db.Users` queries (`GetOrCreateUserAsync`).

---

## Commit

- **SHA:** `7c9bbe3`
- **Message:** `fix(firm): add HasColumnName/HasColumnType for FirmUser.Id — fixes GetOrCreateUserAsync NullRef`
- **Code review:** PASS (Hawkeye, 1 cycle)

---

## Pre-Deploy State (Rollback Baseline)

| Item | Value |
|------|-------|
| Running task def (before) | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:54` |
| Running ECR image (before) | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:53` |
| Running task ARN (before) | `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/5d12bc79bed04297a96c75a47706e6d6` |

---

## Docker Build

- **Dockerfile:** `firm/Dockerfile.debian`
- **Build context:** `/home/fredw/projects/fip` (monorepo root)
- **Flag:** `--no-cache`
- **Result:** ✅ SUCCEEDED — 0 errors, 12 pre-existing warnings
- **Local tag:** `firm-web:54`
- **ECR tag:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:54`
- **ECR digest:** `sha256:51c9d2c26d6887767c052acbe542f9ea9315b2228768c4c459e1c8f4c31b0cfa`

---

## ECS Deploy

| Item | Value |
|------|-------|
| New task definition | `firm-web:55` |
| New task def ARN | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:55` |
| ECR image deployed | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:54` |
| Service | `fortress-tools-cluster/firm-web` |
| `services-stable` wait | ✅ PASSED |
| Post-deploy running image | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:54` ✅ |
| Post-deploy task status | `RUNNING` ✅ |

---

## Verification

Image confirmed via:
```
aws ecs describe-tasks --cluster fortress-tools-cluster \
  --tasks $(aws ecs list-tasks --cluster fortress-tools-cluster --service-name firm-web --query 'taskArns[0]' --output text) \
  --query 'tasks[0].containers[0].image' --output text
→ 742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:54 ✅
```

---

## Rollback Command

If rollback required:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:54
aws ecs wait services-stable --cluster fortress-tools-cluster --services firm-web
```

*(Rolls back to ECR image `firm-web:53`)*

---

## Notes

- Task def revision numbering (ECS) and ECR image tag numbering are independent — ECS revision `:55` maps to ECR image `:54`. This is consistent with prior pattern (ECS `:54` → ECR `:53`, ECS `:53` → ECR `:52`).
- Build used `--no-cache` per SOUL.md policy for UI-facing services.
- Deployed from monorepo root with `firm/Dockerfile.debian` per SOUL.md constraint (MCR blocked on WSL2).
