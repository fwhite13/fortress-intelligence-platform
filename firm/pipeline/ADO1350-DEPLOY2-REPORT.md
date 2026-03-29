# ADO#1350 DEPLOY 2 — firm-web:57 Deployment Report

**Date:** 2026-03-29 15:29–15:50 EDT  
**Deployer:** Rhodey (Subagent)  
**Status:** ⚠️ **DEPLOYED BUT ISSUE PERSISTS**

---

## What Was Deployed

### Build
- **Commit:** `2bac7aa` (fix: remove HasColumnType(JSON) from FirmMeetingSummary in FirmDbContext)
- **Image:** `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:57`
- **Digest:** `sha256:b66a35789395014a0dd9a0715737a304a3bed0de425c10dd7372090db68901e4`
- **Build context:** `/home/fredw/projects/fip` (monorepo root)
- **Dockerfile:** `firm/Dockerfile.debian` (with `--no-cache`)

### ECS Deployment
- **Task definition:** `firm-web:59` (registered new)
- **Image tag in task def:** `:57`
- **Service:** `firm-web` on `fortress-tools-cluster`
- **Desired count:** 1
- **Status:** ✅ **STABLE** (confirmed via `aws ecs wait services-stable`)

### Verification
- **Running image:** ✅ Confirmed `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:57`
- **Task ARN:** `arn:aws:ecs:us-east-1:742932328420:task/fortress-tools-cluster/cfcfd27bfe75409ba9a2a74b991f18a9`
- **Started at:** `2026-03-29T15:36:05.323000-04:00`

---

## Issue: ElementMappingConvention Error Persists

### Observation
The commit `2bac7aa` removed `HasColumnType("JSON")` annotations from `FirmMeetingSummary` properties:
```csharp
// BEFORE:
entity.Property(e => e.ActionItemsJson).HasColumnName("action_items_json").HasColumnType("JSON");

// AFTER:
entity.Property(e => e.ActionItemsJson).HasColumnName("action_items_json");
```

**Expected outcome:** Pomelo's `ElementMappingConvention` NullRef error should be eliminated on startup.

**Actual outcome:** ❌ Error continues to occur in post-deploy logs.

### Error Details
```
System.NullReferenceException: Object reference not set to an instance of an object.
  at Microsoft.EntityFrameworkCore.Storage.RelationalTypeMappingSource.FindCollectionMapping(...)
  at Microsoft.EntityFrameworkCore.Metadata.Conventions.ElementMappingConvention.ProcessModelFinalizing(...)
  at Microsoft.EntityFrameworkCore.Metadata.Internal.Model.FinalizeModel()
```

**First occurrence:** `19:36:36 UTC` (31 seconds after container started)  
**Frequency:** Repeating on every poll cycle in `TranscriptPollingService`  
**Total occurrences (first 2 min):** 26 events  
**As of latest check:** Still hitting in recent logs

### Where It Happens
- **Trigger:** `TranscriptPollingService.PollCoreAsync()` → `db.Database.SqlQueryRaw<TranscriptPollRow>(...)`
- **Root cause:** EF model finalization fails when `DbContext` is first created
- **Symptom:** Service catches exception, logs warning, retries on next interval
- **Impact:** Non-blocking (service continues), but indicates unresolved EF configuration issue

### Why The Fix Didn't Work

The commit removed `HasColumnType("JSON")` from `FirmMeetingSummary`, but:

1. **Scope:** The error may originate from a *different* entity, not `FirmMeetingSummary`
2. **Type hint:** Other entities like `FirmUser`, `UserMicrosoftToken`, `FirmMeeting` may have collection properties or complex type mappings that Pomelo doesn't handle
3. **Root cause:** The underlying Pomelo/MySQL EF Core interaction is broken for collection-typed or complex properties, not just JSON types

### Investigated But Not Found
- ✅ Checked `FirmMeetingSummary` — JSON type hints removed
- ✅ Checked `FirmDbContext` — no remaining `HasColumnType("JSON")`
- ✅ Scanned all entities for collection properties — only standard FK collections (fine)
- ✅ No `[Column(TypeName="json")]` attributes in entity models

---

## Recommendation

**This deploy is NOT a regression** — the `:57` image is running and service-stable. However, the underlying bug (ADO#1350 root cause) is **not fully resolved**.

### Next Steps (for Fred/QA)
1. **Verify app functionality** — Is the HTTP API responding? Can users access FIRM?
2. **Investigate root cause** — The commit message references only `FirmMeetingSummary`, but the error may be broader
3. **Check EF Core version / Pomelo compatibility** — This could be a known issue in the version combination
4. **Consider:** If this is a "known cosmetic error" (doesn't block the service), it may be acceptable to leave as-is
5. **Or:** Debug the actual property causing `FindCollectionMapping` to return null

---

## CloudWatch Logs

**Log group:** `/ecs/firm-web`  
**Post-deploy error filter:** `ElementMappingConvention` hits detected at:
- `19:36:36 UTC` (+31s from startup)
- `19:36:50 UTC` (+45s from startup)
- `19:37:57 UTC` (+112s from startup)
- ... (repeating)

**Sample error:** 26+ occurrences in first 2 minutes post-deploy, continuing in recent logs.

---

## Deployment Summary

| Metric | Value |
|--------|-------|
| Build status | ✅ Success |
| ECR push | ✅ Success |
| Task definition registration | ✅ Success (`:59`) |
| Service update | ✅ Success |
| Service stability | ✅ Stable (1 task running) |
| Running image verified | ✅ `:57` confirmed |
| **Expected fix (ElementMappingConvention)** | ❌ **NOT resolved** |

---

## Files

- Dockerfile: `firm/Dockerfile.debian`
- Config: `firm/src/FortressIntelligenceRM.Web/Data/FirmDbContext.cs` (lines 125–130)
- Service: `firm/src/FortressIntelligenceRM.Web/Services/TranscriptPollingService.cs` (line 63)

---

**Report generated:** 2026-03-29 15:50 EDT  
**Deployer:** Rhodey  
**Action:** Posted to ADO#1350
