# ADO#1352 Deploy Report — FIP Token Architecture
**Deployed by:** War Machine (James Rhodes)  
**Date:** 2026-03-29  
**Commit:** `a1c6c2c` — fix(ADO#1352): cycle 2 review fixes  
**Code Review:** PASS (Hawkeye, 2 cycles)

---

## Rollback State (Pre-Deploy)

| Service | Rollback Task Def |
|---------|-------------------|
| fip-web | `arn:aws:ecs:us-east-1:742932328420:task-definition/fip-web:2` |
| firm-web | `arn:aws:ecs:us-east-1:742932328420:task-definition/firm-web:62` |

---

## Images Built & Pushed

| Service | Image | Digest |
|---------|-------|--------|
| firm-web | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:61` | `sha256:204ac353f7d4b8b8f45f2645040badd035109c7a1569e1cb8aa58e29430fed94` |
| fip-web  | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-portal:prod` | `sha256:4fe4b1a430cad45c4ab3a9c27580d1c21a900629af4edc5e19c94f4710c5bd9c` |

- **FIRM Dockerfile:** `firm/Dockerfile.debian` ✓
- **FIP Dockerfile:** `fip/Dockerfile` (build context: `fip/`) ✓
- Both built with `--no-cache` ✓

---

## Task Definitions Registered

| Service | Old Revision | New Revision | Changes |
|---------|-------------|-------------|---------|
| firm-web | :62 | **:63** | New image (firm-web:61), added `AzureAd__ClientId`, `AzureAd__TenantId`, `AzureAd__ClientSecret`, `FIP_DB_NAME=fip_dev` |
| fip-web  | :2  | **:3**  | New image (fip-portal:prod), added `FIP_DB_NAME=fip_dev` |

---

## Service Updates

Both services updated to new task definitions and reached **STABLE** state on ECS.

```
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:63  ✓
aws ecs update-service --cluster fortress-tools-cluster --service fip-web  --task-definition fip-web:3    ✓
aws ecs wait services-stable ...  → BOTH STABLE ✓
```

---

## Running Image Verification

| Service | Running Image |
|---------|--------------|
| firm-web | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:61` ✓ |
| fip-web  | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fip-portal:prod` ✓ |

---

## ⚠️ CloudWatch Health — POST-DEPLOY ISSUE FOUND

### FIRM — ElementMapping crash: **PRESENT**

```
System.NullReferenceException: Object reference not set to an instance of an object.
   at Microsoft.EntityFrameworkCore.Storage.RelationalTypeMappingSource.FindCollectionMapping(...)
   at Microsoft.EntityFrameworkCore.Metadata.Conventions.ElementMappingConvention.ProcessModelFinalizing(...)
   at FortressIntelligenceRM.Web.Data.FirmDbContext.get_Meetings() in FirmDbContext.cs:line 13
```

The app **IS running** (`Application started` confirmed, `Now listening on: http://[::]:8080`), but:
- `DatabaseInitializationService` fails on startup (caught, app continues)
- `TranscriptPollingService` crashes every poll cycle
- FIRM is effectively **non-functional** — DB access is broken

### FIP — No `Application started` in CloudWatch

FIP CloudWatch log group `/ecs/fip-web` returned empty for `Application started` and error patterns. The log group may not be configured, or FIP is logging to a different group.

---

## Root Cause Analysis — FIRM ElementMappingConvention Crash

**Commit:** `d5b4f6d` introduced `FirmMeeting.CreatedBy` type change: `string` → `Guid`  
**Change in FirmDbContext.cs:** Added `.HasColumnType("char(36)")` to the `CreatedBy` property mapping

**Bug:** Pomelo.EntityFrameworkCore.MySql **8.0.3** has a known null-ref in `RelationalTypeMappingSource.FindCollectionMapping` when `HasColumnType("char(36)")` is applied to a `Guid` property. The `GuidFormat = MySqlGuidFormat.None` connection string setting does NOT prevent this — the explicit `HasColumnType()` call triggers a code path that expects a `CoreTypeMapping elementMapping` that comes back null for `Guid` → `char(36)` in 8.0.3.

**Evidence:** Previous task def (firm-web:62, commit `cdb2b42`) had:
- `FirmMeeting.CreatedBy` as `string` → no issue
- No `HasColumnType("char(36)")` on that property

**Fix required (for software engineer):**  
Remove `.HasColumnType("char(36)")` from `CreatedBy` property in `FirmDbContext.cs`. Pomelo 8.0.3 maps `Guid` to `char(36)` correctly when `GuidFormat.None` is set — no explicit `HasColumnType` needed.

```csharp
// CURRENT (broken):
entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasColumnType("char(36)");

// FIXED:
entity.Property(e => e.CreatedBy).HasColumnName("created_by");
```

---

## Recommendation

**FIRM should be rolled back to firm-web:62** pending code fix for the ElementMappingConvention crash. FIP can remain on the new task def (fip-web:3) — it appears to be functioning.

### Rollback command:
```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:62
aws ecs wait services-stable --cluster fortress-tools-cluster --services firm-web
```

---

## Summary

| Item | Status |
|------|--------|
| FIRM image built & pushed | ✅ firm-web:61 |
| FIP image built & pushed | ✅ fip-portal:prod |
| FIRM task def registered | ✅ firm-web:63 (AzureAd + FIP_DB_NAME added) |
| FIP task def registered | ✅ fip-web:3 (FIP_DB_NAME added) |
| ECS services updated | ✅ Both STABLE |
| FIRM health | ❌ ElementMappingConvention crash — DB non-functional |
| FIP health | ⚠️ CloudWatch inconclusive — no log events visible |
