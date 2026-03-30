# ADO#1352 — FIRM Hotfix Deploy Report (v2)

**Deployed by:** War Machine (James Rhodes)  
**Date:** 2026-03-29  
**Engineer session:** devops/subagent  

---

## Summary

Deployed `firm-web:62` image built from HEAD `ab5e9bf` to ECS service `firm-web`.

- **Task definition registered:** `firm-web:64`  
- **Image deployed:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:62`  
- **ECS service status:** HEALTHY / RUNNING  
- **Application started:** ✅ PRESENT in CloudWatch  

---

## Deploy Steps Executed

| Step | Result |
|------|--------|
| `git pull` — confirmed HEAD `ab5e9bf` | ✅ |
| ECR login | ✅ |
| `docker build --no-cache -f firm/Dockerfile.debian -t firm-web:62 .` | ✅ 0 errors |
| `docker push $ECR/firm-web:62` | ✅ digest `sha256:56d74862370042f11d0b7b205f3c0cb1b534965f93e9c73bada8e4f6e2cfa2ee` |
| Register task def with AzureAd + FIP_DB_NAME env vars | ✅ `firm-web:64` |
| `aws ecs update-service` → `firm-web:64` | ✅ |
| `aws ecs wait services-stable` | ✅ stable |
| Image verification on running task | ✅ `firm-web:62` confirmed |

---

## Env Vars Set in Task Def `firm-web:64`

| Variable | Value |
|----------|-------|
| `AzureAd__ClientId` | `a2de171d-5bb8-4db0-87a6-d07e24b932b3` |
| `AzureAd__TenantId` | `7152ea12-c930-44b0-bb52-069152161c5b` |
| `AzureAd__ClientSecret` | `9V-8Q~...` (set) |
| `FIP_DB_NAME` | `fip_dev` |

---

## CloudWatch Post-Deploy Check

| Check | Result |
|-------|--------|
| `Application started` | ✅ PRESENT |
| `ElementMapping` hits | ⚠️ PRESENT — see note below |

### ⚠️ Residual ElementMapping Issue

**`ab5e9bf` fixed:** `FirmMeeting.CreatedBy` — removed `HasColumnType("char(36)")`  
**Still failing:** `FirmMeeting.FaitUserId` — line 40 of `FirmDbContext.cs` still has `HasColumnType("char(36)")`

**Full exception:**
```
System.NullReferenceException: Object reference not set to an instance of an object.
   at Microsoft.EntityFrameworkCore.Storage.RelationalTypeMappingSource.FindCollectionMapping(...)
   at Microsoft.EntityFrameworkCore.Metadata.Conventions.ElementMappingConvention.ProcessModelFinalizing(...)
```

**Impact:** `DatabaseInitializationService` fails on startup (`CanConnectAsync` throws), but app continues — service is HEALTHY and running. The `FaitUserId` column's `HasColumnType("char(36)")` annotation causes a `NullReferenceException` in EFCore's type mapping pipeline.

**Recommended follow-up:** ADO#1352 needs an additional commit removing `HasColumnType("char(36)")` from `FaitUserId` in `FirmDbContext.cs` (same fix pattern as `ab5e9bf`).

---

## Task Definition History

| Revision | Image | Purpose |
|----------|-------|---------|
| `firm-web:62` | `firm-web:60` | Emergency rollback from broken :61 |
| `firm-web:64` | `firm-web:62` | **THIS DEPLOY** — hotfix `ab5e9bf` |

---

## Verification

```
Task ARN: arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/e6b57c603a664334ac2ee8ccf9c542ca
Image: 742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:62
Health: HEALTHY
Last status: RUNNING
```
