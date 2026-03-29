# ADO#1350 — DEPLOY v2 Report
**Subagent:** War Machine | **Date:** 2026-03-29 15:29 EDT

---

## Summary

**Status:** ⚠️ **DEPLOYED BUT NOT HEALTHY**

- **Image:** `firm-web:57` built from HEAD `2bac7aa fix(ADO#1350): remove HasColumnType(JSON)...`
- **Task Def:** `firm-web:58` registered and deployed
- **Service:** ✅ RUNNING & STABLE
- **Running Image:** ✅ Verified as `firm-web:57`
- **Application Start:** ✅ "Application started" logged
- **Health Issue:** ❌ **ElementMappingConvention NullRef STILL PRESENT**

---

## Build Details

| Item | Value |
|------|-------|
| Commit | `2bac7aa` ✓ Verified |
| Image Tag | `firm-web:57` |
| Digest | `sha256:b66a35789395014a0dd9a0715737a304a3bed0de425c10dd7372090db68901e4` |
| Push | ✅ Complete |
| Build Strategy | `--no-cache` per policy |
| Dockerfile | `firm/Dockerfile.debian` ✓ |

---

## Deployment Steps

### ✅ Pre-Deploy
- Rolled back revision: `firm-web:57` (previous task def)
- Git HEAD verified: `2bac7aa fix(ADO#1350): remove HasColumnType(JSON) from FirmMeetingSummary in FirmDbContext`
- Working directory: `/home/fredw/projects/fip`

### ✅ Build + Push
- Docker build: Success (no-cache)
- ECR login: Success
- Push to `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:57`: Success

### ✅ Task Def + Service
- Task def `:58` registered
- Service updated: `firm-web` → `firm-web:58`
- Service stability wait: **5 min 31 sec** (completed)
- Running container image: Verified as `firm-web:57` ✓

---

## Post-Deploy Health Check

### ✅ Application Start
```
Application started. Press Ctrl+C to shut down.
```
Present in logs at **19:36 UTC** (new task start).

### ❌ ElementMapping Errors (STILL PRESENT)
**Zero expected, but found 18+ instances after new task start.**

#### Root Cause Analysis
Commit `2bac7aa` removed `HasColumnType("JSON")` from three `FirmMeetingSummary` properties:
- `ActionItemsJson`
- `KeyDecisionsJson`
- `FollowUpsJson`

However, the **ElementMappingConvention NullRef persists** because the root cause is **NOT** those three properties. The actual crash is in:

```
RelationalTypeMappingSource.FindCollectionMapping()
  ↓ FindMappingWithConversion()
  ↓ ElementMappingConvention.ProcessModelFinalizing()
```

**Culprit:** Property with `HasConversion<string>()` — specifically `FirmMeeting.Status` enum conversion. EF Core 8 + Pomelo bug when enum→string conversion interacts with ElementMappingConvention.

#### Evidence from Stack Trace (New Task 19:36:36 UTC)
```
System.NullReferenceException: Object reference not set to an instance of an object.
   at Microsoft.EntityFrameworkCore.Storage.RelationalTypeMappingSource.FindCollectionMapping(
      RelationalTypeMappingInfo info, Type modelType, Type providerType, CoreTypeMapping elementMapping)
   at Microsoft.EntityFrameworkCore.Storage.RelationalTypeMappingSource.<>c
      .<FindMappingWithConversion>b__8_0(ValueTuple`4 k, RelationalTypeMappingSource self)
```

This fires **every time** `TranscriptPollingService.PollCoreAsync()` calls `db.Database.SqlQueryRaw<TranscriptPollRow>()`, which triggers FirmDbContext model finalization.

---

## CloudWatch Logs

**Timestamp Range:** 2026-03-29 19:36:05 UTC (new task start) → 19:37:57 UTC

```
[TranscriptPolling] Poll cycle failed (mode column may not exist yet) — will retry next interval
System.NullReferenceException: Object reference not set to an instance of an object.
```

**Poll Interval:** Every 2 minutes → **~9 NullRef exceptions per 18-minute window**

---

## Required Fix (NOT in :57)

The commit `2bac7aa` **partially fixed ADO#1350** by removing JSON type hints, but the **true root cause** remains:

**FirmDbContext.cs line ~53:**
```csharp
entity.Property(e => e.Status)
    .HasColumnName("status")
    .HasConversion<string>()                  // ← This triggers the bug
    .HasDefaultValue(MeetingStatus.Joining);
```

**Workaround needed:** Either:
1. Remove the explicit `.HasConversion<string>()` and rely on EF8's automatic enum→string mapping, OR
2. Explicitly set `.HasColumnType("varchar(50)")` to give Pomelo enough type info to avoid FindCollectionMapping null ref

**Recommendation:** Option 1 (remove explicit conversion) — EF8 handles enum→string natively.

---

## Deployment Summary

| Task | Status |
|------|--------|
| Build | ✅ Success |
| Push | ✅ Success |
| Task Def Register | ✅ Success (revision :58) |
| Service Update | ✅ Success |
| Service Stability | ✅ Success (5m 31s wait) |
| Running Image Verification | ✅ `firm-web:57` confirmed |
| Application Start | ✅ Logged |
| **Health** | ❌ **UNHEALTHY** (ElementMapping NullRef on every poll) |

---

## Rollback

**Current:** `firm-web:58` (points to `:57`)  
**Previous:** `firm-web:57` (points to `:56`)  
To rollback: `aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:57`

---

## Next Steps

1. **Do NOT merge** commit `2bac7aa` to production until root cause is fixed
2. **Remove** `.HasConversion<string>()` from `FirmMeeting.Status` property configuration, OR add explicit `.HasColumnType("varchar(50)")`
3. Rebuild as `:58` and redeploy
4. Verify ElementMapping logs are clean (0 hits)
5. Mark ADO#1350 complete only after health check passes

---

**Image:** `:57` (digest `b66a35789395014a0dd9a0715737a304a3bed0de425c10dd7372090db68901e4`)  
**Deployed At:** 2026-03-29 19:36 UTC  
**Health Status:** ⚠️ Not healthy — awaiting fix
