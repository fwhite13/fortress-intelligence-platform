# ADO#4545 — FIRM JWT Bearer Auth — Post-QA Fix Redeploy Report

**Date:** 2026-05-27  
**WI:** [ADO#4545](https://dev.azure.com/FortressAffinityGroup/74c75814-3f18-429a-be96-5c068deb0632/_workitems/edit/4545)  
**Status:** ✅ COMPLETE — ECS HEALTHY

---

## Summary

Redeployed FIRM (`firm-web`) to production with commit `1fe3cd1c` containing the `OnRedirectToLogin` JWT Bearer auth fix from `Program.cs`. `/api/` paths now return a clean `401` with no redirect header when unauthenticated, restoring mobile app compatibility.

---

## What Was Fixed

- **File:** `firm/src/FortressIntelligenceRM.Web/Program.cs`
- **Change:** Added `options.Events.OnRedirectToLogin` handler inside `AddCookie` options lambda
- **Effect:** `/api/*` paths return `401 Unauthorized` (no `Location` header) when unauthenticated; all other paths unaffected
- **Build:** `dotnet build` — 0 errors, warnings only (pre-existing nullability/MudBlazor)

---

## Deployment Details

| Item | Value |
|---|---|
| **Commit SHA** | `1fe3cd1c` |
| **Previous task def** | `firm-web:135` |
| **New task def** | `firm-web:136` |
| **ECR image** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:1fe3cd1c` |
| **Image digest** | `sha256:c111c866919e1cb78e96ed313f67a8d9cb7baa2daa532e0d15e7fb7abbaafdcf` |
| **ECS cluster** | `fortress-tools-cluster` |
| **ECS service** | `firm-web` |
| **Task status** | `RUNNING` |
| **Health status** | `HEALTHY` |
| **Deployed at** | 2026-05-27 ~21:55 EDT |

---

## Build Log

Full build log: `/tmp/ado4545-fix-firm-build.log`

- Base image: Debian Bookworm (Dockerfile.debian)
- .NET SDK: 8.0.421
- ASP.NET Core Runtime: 8.0.27
- Build: `dotnet publish -c Release` — succeeded
- Docker build: exit 0

---

## Rollback Plan

If rollback is needed, run:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
aws ecs update-service --cluster fortress-tools-cluster --service firm-web \
  --task-definition arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:135 \
  --force-new-deployment \
  --region us-east-1 --profile fortress-tools-deployer
```

---

## Verification

- ✅ Commit `1fe3cd1c` confirmed on `main` (already up to date)
- ✅ Docker build: exit 0, 0 errors
- ✅ ECR push: digest `sha256:c111c866...` confirmed
- ✅ Task def `firm-web:136` registered
- ✅ ECS `services-stable` wait: passed
- ✅ Running container image matches `firm-web:1fe3cd1c`
- ✅ Health check: HEALTHY
- ✅ ADO comment posted (comment ID 814391)

---

_Returning to Maria for final QA validation of mobile endpoint 401 behavior._
