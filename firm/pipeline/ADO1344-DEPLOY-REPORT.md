# Deploy Report: ADO#1344

**Commit:** `b205f2e fix(ADO#1344): point FirmMicrosoftTokenService to fait_dev for token lookup`  
**Deployer:** War Machine (James Rhodes)  
**Date:** 2026-03-29

---

## Pre-Deploy Snapshot

- **Previous image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:3a749eb0e82ce679e1cf77961a062def3ea1b786`
- **Previous task def revision:** `firm-web:52`

---

## Rollback Plan

If deploy fails or service unhealthy, roll back to the previous revision:

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1

# Roll back to firm-web:52
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:52
aws ecs wait services-stable --cluster fortress-tools-cluster --services firm-web
```

---

## Steps Completed

| Step | Status | Notes |
|------|--------|-------|
| Source deployer creds | ✅ PASS | `fortress-tools-deployer` confirmed |
| Pre-deploy snapshot | ✅ PASS | Previous: `firm-web:52` (digest `3a749eb...`) |
| Git push to origin/main | ✅ PASS | `3a749eb..b205f2e main -> main` |
| ECR login | ✅ PASS | Login Succeeded |
| Docker build (no-cache) | ✅ PASS | `firm/Dockerfile.debian` from monorepo root; image `firm-web:52` |
| Docker tag + ECR push | ✅ PASS | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:52`; digest `sha256:721cddb...` |
| Register task def | ✅ PASS | New revision: `firm-web:53` (image updated to `:52` tag) |
| Update ECS service | ✅ PASS | `firm-web` service updated to `firm-web:53` |
| Wait for stable | ✅ PASS | Stabilized at 08:36:14 EDT (~2m16s) |
| Verify running image | ✅ PASS | `firm-web:52` confirmed on task `f7bc76bb...` |
| DB fix: fait_user_id | ✅ PASS | `firm_users` row `9bdd8169...` updated; email: `fwhite@fortressinsurance.com` |

---

## Post-Deploy Verification

- **Running image confirmed:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:52`
- **Active task def:** `firm-web:53`
- **Service health:** HEALTHY (services-stable within ~2m)
- **DB fix applied:** YES — `fait_user_id = 1f89fc34-9b8c-42fc-b674-aa4562a4f57d` for user `9bdd8169-9e88-44aa-b80a-ddbaae33662d` (`fwhite@fortressinsurance.com`)

---

## Deployment Time

- **Start:** 2026-03-29 08:32:37 EDT (docker build start)
- **End:** 2026-03-29 08:36:14 EDT (service stable)
- **Total duration:** ~3m37s

---

## Notes

- Docker build pulled from cached layers; final stage rebuilt from scratch as expected with `--no-cache`
- Task def `:52` previously held the prior commit's digest image (`3a749eb…`); new task def `:53` holds the `:52` ECR tag pointing to `b205f2e` build
- Warnings during build are pre-existing nullable reference type annotations (non-breaking)
