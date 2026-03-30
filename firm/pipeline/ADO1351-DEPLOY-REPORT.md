# ADO#1351 Deploy Report — firm-web:60
**Agent:** War Machine (James Rhodes)  
**Date:** 2026-03-29  
**Status:** ⚠️ DEPLOYED — CRASH PERSISTS

---

## Deployment Summary

| Item | Value |
|------|-------|
| Image | `firm-web:60` |
| ECR | `742932328420.dkr.ecr.us-east-1.amazonaws.com/firm-web:60` |
| Digest | `sha256:b5fda4206c112e7651dac9402f5a6c819d2755816e977ce508c2be57f1500af4` |
| HEAD Commit | `55eff39` — fix(ADO#1351): fix remaining string/Guid type errors after FirmUser.Id forklift |
| Task Def | `firm-web:62` (registered from prior `firm-web:61`) |
| Rollback Rev | `firm-web:61` |
| ECS Service | `fortress-tools-cluster/firm-web` |
| Service Health | ✅ STABLE — `Application started` present |
| ElementMapping | ❌ CRASH PERSISTS |

---

## Deployment Steps — All Completed

- [x] HEAD verified: `55eff39`
- [x] ECR login
- [x] `docker build --no-cache -f firm/Dockerfile.debian -t firm-web:60 .` — SUCCESS (0 errors)
- [x] `docker push $ECR/firm-web:60` — pushed
- [x] Task definition `firm-web:62` registered
- [x] ECS service updated to `firm-web:62`
- [x] `aws ecs wait services-stable` — STABLE
- [x] Running image verified: `...firm-web:60` ✅

---

## CloudWatch Post-Deploy Analysis

**ElementMapping hits:** 12 events (crash is active)  
**Application started:** ✅ Present (app boots, crash is non-fatal loop)

### Root Cause Identified

The `ElementMappingConvention` crash is a **EF Core type mapping failure**, not a startup crash. The exception is:

```
System.NullReferenceException: Object reference not set to an instance of an object.
   at RelationalTypeMappingSource.FindCollectionMapping(...)
   at RelationalTypeMappingSource.FindMappingWithConversion(...)
   at RelationalTypeMappingSource.FindMapping(IProperty property)
   at ElementMappingConvention.ProcessModelFinalizing(...)
```

**Call chain leads to:** `FirmDbContext.get_Meetings()` → `TranscriptPollingService.PollCoreAsync`

### FK Type Mismatch — the actual bug

In commit `877a9cc`, `FirmUser.Id` was changed from `string` to `Guid`:
```csharp
// Before
public string Id { get; set; } = "";
// After  
public Guid Id { get; set; }
```

But `FirmMeeting.CreatedBy` was **never updated** — it remains `string`:
```csharp
public string CreatedBy { get; set; } = "";
```

`FirmDbContext.OnModelCreating` defines:
```csharp
entity.HasOne(e => e.CreatedByUser)
    .WithMany(u => u.Meetings)
    .HasForeignKey(e => e.CreatedBy)  // ← string FK
    .HasConstraintName("fk_fm_user");  // ← to Guid PK
```

EF Core cannot create a type mapping for a `string` FK → `Guid` PK relationship. When `TranscriptPollingService` creates a `DbContext` to query meetings, EF Core tries to finalize the model and crashes inside `FindCollectionMapping` returning null.

### Why 55eff39 didn't fix it

The fix changed `TranscriptPollingService` from raw SQL to a LINQ Join:
```csharp
.Join(db.Users,
    m => m.CreatedBy,   // ← string
    u => u.Id,          // ← Guid
    ...)
```

This still triggers EF Core model finalization with the same broken FK mapping — it just moved where in the code the crash manifests. The model-level type incompatibility is unchanged.

---

## Required Fix

**File:** `firm/src/FortressIntelligenceRM.Web/Models/FirmMeeting.cs`

Change:
```csharp
public string CreatedBy { get; set; } = "";
```

To:
```csharp
public Guid CreatedBy { get; set; }
```

**And** update all callers that assign `CreatedBy` (likely from string `EntraOid` or `UserId` context) to use `Guid.Parse(...)` or pass `FirmUser.Id` directly.

Also verify `FirmDbContext.OnModelCreating` for `FirmMeeting.CreatedBy` — if `.HasColumnType("char(36)")` is needed (like `FaitUserId`), add it:
```csharp
entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasColumnType("char(36)");
```

---

## Rollback Status

Service is running `firm-web:60` and is STABLE. The app starts and serves requests. The ElementMapping crash is in `TranscriptPollingService` (background service) — **meeting creation/listing UI may still work**, but transcript polling for Mode A meetings is broken.

**Rollback target if needed:** `firm-web:61` (task-def `firm-web:61`)

```bash
source /home/fredw/projects/ai/projects/fortress_tools/.env.deployer
export AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_REGION=us-east-1
aws ecs update-service --cluster fortress-tools-cluster --service firm-web --task-definition firm-web:61
aws ecs wait services-stable --cluster fortress-tools-cluster --services firm-web
```

---

## Next Steps

1. Fix `FirmMeeting.CreatedBy` type from `string` → `Guid` (plus all callers)
2. Rebuild + push as `firm-web:61` (or next available tag)
3. Redeploy

_Reported by War Machine — 2026-03-29_
