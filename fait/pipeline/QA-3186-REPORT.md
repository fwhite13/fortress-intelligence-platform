# QA Report: ADO#3186 — 4.1-A: memory_topics table + IMemoryFileService (S3 read/write)

**Date:** 2026-05-10  
**QA Analyst:** Black Widow (Natasha Romanoff)  
**Task Def:** `fred-dev:162`  
**Target:** https://fred.dev.fortressam.ai  
**Verdict:** ✅ **QA PASS**

---

## Tests Run

### 1. AWS Service Health ✅ PASS

```
{
  "status": "ACTIVE",
  "taskDef": "arn:aws:ecs:us-east-1:742932328420:task-definition/fred-dev:162",
  "desired": 1,
  "running": 1
}
```

- `fred-dev:162` is ACTIVE, desired=1, running=1. ✅

---

### 2. DB Verification ✅ PASS (pre-verified by Maria)

Migration `20260510144114_AddMemoryTopics` confirmed applied:

- Table `memory_topics` created with correct schema:
  - `Id` CHAR(36) ascii NOT NULL — PK
  - `UserId` CHAR(36) ascii NOT NULL — FK → users(Id) ON DELETE CASCADE
  - `Slug` varchar(100) NOT NULL
  - `Title` varchar(200) NOT NULL
  - `CreatedAt` DATETIME(6) NOT NULL
  - `UpdatedAt` DATETIME(6) NOT NULL
  - `UNIQUE KEY IX_memory_topics_UserId_Slug (UserId, Slug)`
- Migration registered in `__EFMigrationsHistory` ✅
- Migration file: `src/FortressAI.Web/Migrations/20260510144114_AddMemoryTopics.cs` — matches spec exactly ✅

---

### 3. CloudWatch Startup Logs ✅ PASS

Log stream: `ecs/fred/dc1f99fb98644e4cb10f96c581229efe`

Key findings:
- ✅ `ScheduledTaskBackgroundService starting, poll interval: 60s` — regression check passed
- ✅ `Database initialization complete`
- ✅ `Now listening on: http://[::]:8080`
- ✅ `Application started`
- ✅ All idempotent `fail:` entries are pre-existing schema migration guards — expected, non-fatal
- ✅ **Zero** `IMemoryFileService` / `MemoryFileService` exceptions
- ✅ **Zero** DI errors or missing service registration errors

---

### 4. Code: IMemoryFileService Registration ✅ PASS

`src/FortressAI.Web/Program.cs` line 111:
```csharp
builder.Services.AddScoped<IMemoryFileService, MemoryFileService>();
```
- Registered as **Scoped** ✅

---

### 5. Code: MemoryFileService Implementation ✅ PASS

Full implementation reviewed:

**Constructor injection:**
```csharp
public MemoryFileService(
    IDbContextFactory<AppDbContext> dbFactory,   // ✅ uses factory, not raw DbContext
    IAmazonS3 s3,
    IConfiguration config,
    ILogger<MemoryFileService> logger)
```
- Uses `IDbContextFactory<AppDbContext>` ✅ (no raw DbContext injection)

**Reserved slug guard in `WriteTopicAsync`:**
```csharp
if (slug.Equals("MEMORY", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("The slug 'MEMORY' is reserved.", nameof(slug));
```
- Guard present, case-insensitive ✅

**DbContext lifetime pattern:** Each method uses `await using (var db = await _dbFactory.CreateDbContextAsync(ct))` — scoped and disposed correctly before downstream calls ✅

**Operations verified present:**
- `GetTopicsAsync` — DB query ordered by title ✅
- `GetTopicContentAsync` — S3 GET with NoSuchKey guard ✅
- `WriteTopicAsync` — S3 PUT → DB upsert → RebuildMemoryIndexAsync ✅
- `DeleteTopicAsync` — S3 DELETE (NoSuchKey safe) → DB remove → RebuildMemoryIndexAsync ✅
- `RebuildMemoryIndexAsync` — Rebuilds `MEMORY.md` index on S3 ✅
- `ExportZipAsync` — Zip all topic files + MEMORY.md index ✅

---

## Pre-Existing Conditions (Not Regressions)

- **Cloudflare managed challenge** on https://fred.dev.fortressam.ai — browser login blocked without Entra creds. Pre-existing. No change in this WI.
- **TestAuth__Secret missing from task def** — not applicable to this WI (no auth feature work).
- **Idempotent `fail:` entries in CloudWatch** — all pre-existing "already applied" schema migration guards. Expected non-fatal pattern.

---

## Summary

| Check | Result |
|---|---|
| `fred-dev:162` running 1/1 | ✅ PASS |
| DB: `memory_topics` table schema correct | ✅ PASS (Maria-verified) |
| DB: Migration in `__EFMigrationsHistory` | ✅ PASS |
| CloudWatch: no DI/service errors | ✅ PASS |
| CloudWatch: `ScheduledTaskBackgroundService starting` present | ✅ PASS |
| Code: `IMemoryFileService` registered as Scoped | ✅ PASS |
| Code: `IDbContextFactory<AppDbContext>` used (not raw DbContext) | ✅ PASS |
| Code: Reserved slug guard in `WriteTopicAsync` | ✅ PASS |

**Verdict: QA PASS** — All acceptance criteria verified. Service is live and healthy.

---

*Trust nothing. Verify everything.*
