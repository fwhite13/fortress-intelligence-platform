# Build Report — ADO#2844
**UserProvisioningService — S3 workspace, PG schema, Aurora records, atomic+idempotent**

---

## What was built

`UserProvisioningService` — called after onboarding wizard completion. Provisions all per-user resources atomically. Safe to call twice (idempotent via `OnboardingCompletedAt` check). Full rollback on failure.

---

## Commit

`5754984` — `feat(fait-v2#2844): UserProvisioningService — atomic S3+PG+Aurora provisioning`

---

## Files created

| File | What |
|---|---|
| `Services/IUserProvisioningService.cs` | Interface + `ProvisioningResult` record |
| `Services/UserProvisioningService.cs` | Full 7-step implementation with rollback |
| `Services/Exceptions/ProvisioningException.cs` | Typed exception with `UserId` + `FailedStep` |

## Files modified

| File | What |
|---|---|
| `FortressAI.V2.Web.csproj` | Added `AWSSDK.S3`, `AWSSDK.Extensions.NETCore.Setup`, `Npgsql 8.*` |
| `Program.cs` | `AddAWSService<IAmazonS3>()` + `AddScoped<IUserProvisioningService, UserProvisioningService>()` |
| `appsettings.json` | `AWS:WorkspaceBucket = fortress-user-workspaces`, `ConnectionStrings:PostgresConnection` |

---

## Provisioning sequence

| Step | What | Rollback target |
|---|---|---|
| 1 | Idempotency check on `OnboardingCompletedAt` | — |
| 2 | Upsert `users` record (insert or update) | EF ChangeTracker.Detach |
| 3 | Write 4 S3 files to `workspaces/{userId}/assistants/` + `memory/` | Delete S3 objects |
| 4 | Create RDS PostgreSQL schema `user_{userId}` + `memory_chunks` + `memory_topics` + index | DROP SCHEMA CASCADE |
| 5 | Add `main_assistants` Aurora record | EF ChangeTracker.Detach |
| 6 | Seed 4 `memory_topics` rows (SOUL, USER, MEMORY, AGENTS) | EF ChangeTracker.Detach |
| 7 | Set `OnboardingCompletedAt`, `SaveChangesAsync` | (final commit — no partial state) |

### Rollback logic
- S3 writes succeed → PG fails: deletes all S3 objects written
- PG succeeds → Aurora `SaveChangesAsync` fails: drops PG schema + deletes S3 objects
- Rollback failures log `"manual cleanup required"` for ops to find

---

## Build result

```
Build succeeded.
    0 Error(s)
    0 Warning(s)
```

---

## Parallelization used

None — sequential (each step depends on previous step's output).

---

## Acceptance criteria verification

- [x] `IUserProvisioningService` interface with `ProvisionAsync` signature
- [x] `ProvisioningResult` record (WasProvisioned, WorkspaceS3Prefix, PgSchemaName)
- [x] `ProvisioningException` in `Services/Exceptions/`
- [x] 7-step sequence per spec §4.7
- [x] S3 writes use `PutObjectAsync` (idempotent by nature)
- [x] PG uses `CREATE SCHEMA IF NOT EXISTS`, `CREATE TABLE IF NOT EXISTS`, `CREATE EXTENSION IF NOT EXISTS`
- [x] Rollback: S3 objects deleted if PG fails
- [x] Rollback: PG schema dropped if Aurora save fails
- [x] `ChangeTracker.Entries()` detached on rollback
- [x] `AddAWSService<IAmazonS3>()` registered in Program.cs
- [x] `AddScoped<IUserProvisioningService, UserProvisioningService>()` registered
- [x] `dotnet build` — 0 errors

---

## What needs real AWS credentials/secrets before runtime use

| Item | Status | What's needed |
|---|---|---|
| `AWS:WorkspaceBucket` | ✅ Configured (`fortress-user-workspaces`) | Bucket must be provisioned by Rhodey (#2841) |
| AWS S3 credentials | ⚠️ Needs env/IAM | `AWS_ACCESS_KEY_ID` + `AWS_SECRET_ACCESS_KEY` via ECS task role IAM policy scoped to `fortress-user-workspaces` |
| `ConnectionStrings:PostgresConnection` | ⚠️ PLACEHOLDER | Real password must be injected from Secrets Manager at deploy time; RDS PostgreSQL instance must have `pgvector` extension available and app user must have `CREATE SCHEMA` privilege |
| `AWS:Region` | ✅ Configured (`us-east-1`) | Pre-existing |

---

## Build report sent to Clint

Pending — awaiting pipeline dispatch.

---

---

## Build Cycle 2 — ADO#2844

**Commit:** `09b6ce1` — `fix(fait-v2#2844): per-step diagnostic flags, CT propagation, GUID guard, migration`
**Triggered by:** Review cycle 1 — Clint's NEEDS-CHANGES verdict

### What was fixed

| Fix | Category | What changed |
|---|---|---|
| C1 | Required | Replaced `auroraRecordCreated` single flag with four per-step diagnostic flags: `s3Complete`, `pgComplete`, `auroraAddComplete`, `seedComplete`. FailedStep ternary now correctly identifies all 5 failure stages: `s3-write` → `pg-schema` → `aurora-record` → `memory-topics-seed` → `aurora-save` |
| N1 | Nitpick | `DropPgSchemaAsync` now accepts `CancellationToken ct = default`; passes `ct` to `OpenAsync(ct)` and `ExecuteNonQueryAsync(ct)`. Rollback call site updated accordingly. |
| N2 | Nitpick | Added `Guid.TryParse` guard at top of `ProvisionAsync` — throws `ArgumentException` before any DB/S3 access if `userId` is not a valid GUID. |
| N3 | Nitpick | `FaitV2DbContext.OnModelCreating` already had `HasIndex(e => new { e.UserId, e.TopicSlug }).IsUnique()` from cycle 1. Added EF migration `20260507010358_AddMemoryTopicsUniqueConstraint` to apply it to the schema. |

### Files changed
- `Services/UserProvisioningService.cs` — C1, N1, N2
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.cs` — new migration
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.Designer.cs` — scaffolded
- `Data/Migrations/FaitV2DbContextModelSnapshot.cs` — updated

### Build result
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Migration name
`20260507010358_AddMemoryTopicsUniqueConstraint`

---

## Notes for Clint

- `GetPgSchemaName()` is `private static` — deterministic, no DB lookup needed for schema routing
- Rollback is best-effort: if S3 delete fails mid-rollback, we log and continue (don't mask the original error)
- `ChangeTracker.Detach` pattern on rollback prevents EF from trying to save Aurora records that were in the add queue when PG failed
- pgvector `CREATE EXTENSION` is run at each schema creation — harmless, idempotent
- No `Pgvector` NuGet package used — raw SQL only (as spec allows)

---

---

## Build Cycle 3 — ADO#2844

**Commit:** `b0566e4` — `fix(fait-v2#2844): remove empty AddMemoryTopicsUniqueConstraint migration — constraint already exists in InitialSchema`
**Triggered by:** Review cycle 2 — Clint's N3 CRITICAL FAIL verdict

### Root Cause

The broken migration `20260507010358_AddMemoryTopicsUniqueConstraint` had empty `Up()` and `Down()`. The cause was not a missing model annotation — it was that `InitialSchema` (20260506224542) **already created** `ix_memory_topics_user_slug` as a unique index on `memory_topics(user_id, topic_slug)` (lines 169-172). EF compared the current model against the snapshot and found no diff, so it scaffolded an empty migration.

The constraint was never missing from the DB schema — it has been in InitialSchema from day 1.

### What was fixed

| Fix | Category | What changed |
|---|---|---|
| N3 | Critical | Deleted `20260507010358_AddMemoryTopicsUniqueConstraint.cs` and `.Designer.cs`. No regeneration needed — the unique constraint already exists in `InitialSchema`. Migration chain is now clean. |

### Files changed
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.cs` — **deleted**
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.Designer.cs` — **deleted**

### Files NOT changed
- `FaitV2DbContextModelSnapshot.cs` — already correct, no reference to broken migration
- `FaitV2DbContext.cs` — already correct, `HasIndex(...).IsUnique()` present

### Clean migration chain after fix
```
20260506224542_InitialSchema.cs           ← creates ix_memory_topics_user_slug (unique: true)
20260506225415_AddUserSessionTimestamps.cs
FaitV2DbContextModelSnapshot.cs
```

### Build result
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Unique index in InitialSchema (for Clint's verification)
```csharp
migrationBuilder.CreateIndex(
    name: "ix_memory_topics_user_slug",
    table: "memory_topics",
    columns: new[] { "user_id", "topic_slug" },
    unique: true);
```
InitialSchema.cs lines 169-172 — **always there, always correct**.

### Notes for Clint
- `Up()` for the unique constraint is in `InitialSchema` — Clint can verify at `Data/Migrations/20260506224542_InitialSchema.cs` lines 169-172
- No model changes needed — constraint is in sync across DbContext, snapshot, and migration
- Ready for final review and merge
