# Review Report — ADO#2844

---

## Review Cycle 2 — ADO#2844

**WI:** FAIT v2: User provisioning service
**Commit:** `09b6ce1`
**Review Cycle:** 2 of 2 (reopened)
**Reviewer:** Hawkeye (Clint Barton)
**CC Invocation:** `cat pipeline/review-c2-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Verdict: NEEDS-CHANGES

C1, N1, N2 all verified clean. N3 migration `Up()` is empty — the unique constraint on `memory_topics(user_id, topic_slug)` will **never be applied to the database**.

---

## Cycle 2 Fix Verification

### C1 — Per-step diagnostic flags ✅ PASS

Four flags declared at lines 119–122 of `UserProvisioningService.cs`:
```csharp
var s3Complete = false;
var pgComplete = false;
var auroraAddComplete = false;
var seedComplete = false;
```

All set correctly — each flag set only after its step fully completes:
- `s3Complete = true` — line 174, after all 4 S3 writes complete
- `pgComplete = true` — line 180, after `CreatePgSchemaAsync` returns
- `auroraAddComplete = true` — line 196, after `_db.MainAssistants.Add()`
- `seedComplete = true` — line 218, after memory_topics seeding loop

`FailedStep` ternary (lines 277–281):
```csharp
!s3Complete ? "s3-write"
    : !pgComplete ? "pg-schema"
    : !auroraAddComplete ? "aurora-record"
    : !seedComplete ? "memory-topics-seed"
    : "aurora-save"
```
Exact order correct. No `auroraRecordCreated` flag remaining. ✅

---

### N1 — CancellationToken in DropPgSchemaAsync ✅ PASS

- Signature (line 337): `CancellationToken ct = default` ✅
- `OpenAsync(ct)` — line 340 ✅
- `ExecuteNonQueryAsync(ct)` — line 342 ✅
- Call site (line 240): `await DropPgSchemaAsync(pgConnString, schemaName, ct)` — passes `ct` ✅

---

### N2 — Guid.TryParse guard ✅ PASS

Lines 90–91, first executable lines of `ProvisionAsync`:
```csharp
if (!Guid.TryParse(userId, out _))
    throw new ArgumentException($"userId must be a valid GUID, got: {userId}", nameof(userId));
```
Guard precedes `_db.Users.FirstOrDefaultAsync` (line 96). ✅

---

### N3 — Migration AddMemoryTopicsUniqueConstraint ❌ CRITICAL FAIL

**`Up()` body is empty:**
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{

}
```
**`Down()` body is also empty.**

The Designer.cs snapshot correctly shows `HasIndex("UserId", "TopicSlug").IsUnique()` for `MemoryTopic` — but that only reflects the EF model state. The actual DDL (`CREATE UNIQUE INDEX`) was never generated into `Up()`. Running `dotnet ef database update` against this migration does nothing to the database. The constraint will not be applied.

Additional indicator: `FaitV2DbContextModelSnapshot.cs` is absent from the diff between `5754984` → `09b6ce1`. A proper `dotnet ef migrations add` run would update the global snapshot alongside the Designer.cs. Its absence confirms the migration was not generated correctly by the EF tooling.

---

### Scope Check ✅ PASS

No unexpected files changed. Files changed between `5754984` → `09b6ce1` (excluding pipeline docs):
- `Services/UserProvisioningService.cs` ✅
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.cs` ✅
- `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.Designer.cs` ✅
- `Data/Migrations/FaitV2DbContextModelSnapshot.cs` — **absent** (consistent with N3 defect)

---

## Issues Found

| Severity | File | Issue | Fix |
|----------|------|-------|-----|
| Critical | `Data/Migrations/20260507010358_AddMemoryTopicsUniqueConstraint.cs` | `Up()` is empty — unique constraint on `memory_topics(user_id, topic_slug)` will never be applied to DB | Delete migration, verify `HasIndex(...).IsUnique()` in `OnModelCreating`, re-run `dotnet ef migrations add AddMemoryTopicsUniqueConstraint` |

---

## What Tony needs to fix

**One fix required before this ships:**

Delete `20260507010358_AddMemoryTopicsUniqueConstraint.cs` and `20260507010358_AddMemoryTopicsUniqueConstraint.Designer.cs`. Verify that `FaitV2DbContext.OnModelCreating` has `HasIndex(e => new { e.UserId, e.TopicSlug }).IsUnique()` on `MemoryTopic`. Then run:
```bash
dotnet ef migrations add AddMemoryTopicsUniqueConstraint
```
This will generate `Up()` with the correct `migrationBuilder.CreateIndex(...)` call and update `FaitV2DbContextModelSnapshot.cs`. Both files must be committed.

---

_Reviewed by Hawkeye — cycle 2/2. N3 migration empty — fix and resubmit._

---

# Cycle 1 Report (original below)

---


**WI:** FAIT v2: User provisioning service — S3 prefix, memory files, RDS PostgreSQL schema + pgvector, Aurora records  
**Commit:** `5754984`  
**Review Cycle:** 1 of 2  
**Reviewer:** Hawkeye (Clint Barton)  
**CC Invocation:** `cat pipeline/review-brief-2844.md | claude --model sonnet --print --dangerously-skip-permissions`

---

## Verdict: NEEDS-CHANGES

One FAIL (diagnostic labeling bug in `ProvisioningException.FailedStep`), three WARNs.  
All critical functional behaviors — idempotency, 7-step sequence, rollback, config reads, no hardcoded credentials — **PASS**.

---

## Spec Compliance Check

**§2 Codebase Map:**
- `Services/IUserProvisioningService.cs` — ✅ created
- `Services/UserProvisioningService.cs` — ✅ created
- `Services/Exceptions/ProvisioningException.cs` — ✅ created
- `FortressAI.V2.Web.csproj` — ✅ AWSSDK.S3 + Npgsql added
- `Program.cs` — ✅ AddAWSService + AddScoped registered
- `appsettings.json` — ✅ AWS:WorkspaceBucket + PostgresConnection added

**§6 Out of Scope:** ✅ No out-of-scope changes detected.

**§7 Acceptance Criteria:**
- [x] IUserProvisioningService interface with ProvisionAsync ✅
- [x] ProvisioningResult record (WasProvisioned, WorkspaceS3Prefix, PgSchemaName) ✅
- [x] ProvisioningException in Services/Exceptions/ ✅
- [x] 7-step sequence per spec ✅
- [x] S3 uses PutObjectAsync (idempotent) ✅
- [x] PG uses CREATE IF NOT EXISTS throughout ✅
- [x] Rollback: S3 deleted if PG fails ✅
- [x] Rollback: PG dropped if Aurora save fails ✅
- [x] ChangeTracker detached on rollback ✅

**Spec compliance verdict:** ✅ COMPLIANT (functional behaviors pass — failure is diagnostic accuracy)

---

## Consistency Audit

**Files Cross-Referenced:**
- `UserProvisioningService.cs` ↔ `IUserProvisioningService.cs` — ✅ signature matches exactly
- `UserProvisioningService.cs` ↔ `appsettings.json` — ✅ config key `AWS:WorkspaceBucket` matches; `ConnectionStrings:PostgresConnection` matches
- `UserProvisioningService.cs` ↔ `Program.cs` — ✅ DI registrations match service type
- `Program.cs` ↔ `FortressAI.V2.Web.csproj` — ✅ `IAmazonS3` requires `AWSSDK.S3` + `AWSSDK.Extensions.NETCore.Setup`, both present

**Undocumented Dependencies Found:** None outside the reviewed files.

---

## CC Review Summary

CC ran 24 checks. 19 PASS, 1 FAIL, 3 WARN, 1 PASS-with-notes.

**Confirmed real issues:**
- ✅ C1 (FAIL): `FailedStep` ternary has two off-by-one misidentification bugs — real, actionable
- ✅ W1 (WARN): userId SQL injection surface — real but low risk for UUID/Entra OID inputs; defensive fix is easy
- ✅ W2 (WARN): No `CancellationToken` in `DropPgSchemaAsync` — legitimate design gap
- ✅ W3 (WARN): Concurrent call TOCTOU on step 6 — architectural concern; missing unique constraint on `(user_id, topic_slug)`

**False positives:** None. All findings are real.

---

## Critical Issues [1]

### C1: FailedStep ternary misidentifies two failure scenarios
- **File:** `UserProvisioningService.cs` (lines ~264–268)
- **Category:** correctness / diagnosability
- **Issue:** The `FailedStep` ternary uses `auroraRecordCreated` (set at the `_db.MainAssistants.Add()` call in Step 5, which cannot throw) to represent Aurora save failure. This means any exception in Step 6 (memory_topics loop) is also labeled `"aurora-save"` when `SaveChangesAsync` was never called. Additionally, a partial S3 write failure (some objects written, not all) reports `"pg-schema"` when PG was never reached.

**Evidence:**
```csharp
// auroraRecordCreated is set TRUE here — at Add(), not at SaveChangesAsync:
_db.MainAssistants.Add(assistant);
auroraRecordCreated = true;    // ← can't actually fail here

// ...Step 6 memory_topics loop follows (AnyAsync CAN throw)...

// FailedStep ternary:
auroraRecordCreated ? "aurora-save"          // ← fires for Step 6 failures too
    : pgSchemaCreated ? "aurora-record"
    : s3ObjectsWritten.Count > 0 ? "pg-schema"  // ← fires for partial S3 failure
    : "s3-write"
```

**Fix:**
```diff
  // Before Step 6:
+ var step6Complete = false;

  // After Step 6 loop completes:
+ step6Complete = true;

  // FailedStep ternary:
- auroraRecordCreated ? "aurora-save"
-     : pgSchemaCreated ? "aurora-record"
-     : s3ObjectsWritten.Count > 0 ? "pg-schema"
-     : "s3-write"
+ !step6Complete && auroraRecordCreated ? "memory-topics-seed"
+     : auroraRecordCreated && !step6Complete ? "memory-topics-seed"
+     : pgSchemaCreated && !auroraRecordCreated ? "aurora-record"
+     : pgSchemaCreated ? "aurora-save"
+     : s3ObjectsWritten.Count > 0 ? "s3-write"   // partial S3 failure
+     : "s3-write"
```

Simpler clean version:
```csharp
// Add flags for each logical step boundary:
var s3Complete = false;
var pgComplete = false;
var auroraAddComplete = false;
var seedComplete = false;

// Set each after the step fully completes, then:
var failedStep = !s3Complete ? "s3-write"
    : !pgComplete ? "pg-schema"
    : !auroraAddComplete ? "aurora-record"
    : !seedComplete ? "memory-topics-seed"
    : "aurora-save";
```

---

## Important Issues [0]

None.

---

## Nitpicks [3]

### N1: No CancellationToken in DropPgSchemaAsync
**File:** `UserProvisioningService.cs` (lines ~322–328)  
`DropPgSchemaAsync` takes no `CancellationToken` and passes none to `OpenAsync`/`ExecuteNonQueryAsync`. If the PG host is unreachable during rollback, this will block indefinitely. Consider passing a short-timeout CT during rollback, or at minimum add a `CancellationToken ct = default` parameter and pass it through. Not blocking.

### N2: No userId format guard before SQL interpolation
**File:** `UserProvisioningService.cs` (`GetPgSchemaName`)  
The schema name is built from `userId` and double-quoted in all DDL. Entra OIDs and UUIDs are hex+hyphens only so this is not currently exploitable, but there's no explicit validation. A one-liner guard is cheap insurance:
```csharp
private static string GetPgSchemaName(string userId)
{
    if (!System.Text.RegularExpressions.Regex.IsMatch(userId, @"^[a-zA-Z0-9\-]+$"))
        throw new ArgumentException($"Invalid userId format: {userId}");
    return "user_" + userId.Replace("-", "_");
}
```

### N3: Concurrent provisioning TOCTOU — missing unique constraint
**Aurora `memory_topics` table:** The Step 6 `AnyAsync` check is not atomic. Two concurrent calls for the same `userId` that both pass the Step 1 idempotency check will both see 0 existing topics and both stage 4 rows. A unique constraint on `(user_id, topic_slug)` in the Aurora schema would make the second insert fail cleanly rather than produce duplicate rows. Flag for the schema owner — not a code change.

---

## Positive Observations

- Rollback coverage is solid — both S3 and PG are cleaned up on Aurora save failure, and each rollback step is independently try/caught so a failed S3 delete doesn't mask the original error.
- `PutObjectAsync` is correctly used throughout — no `PutObject` sync calls.
- `CREATE EXTENSION IF NOT EXISTS vector` in `CreatePgSchemaAsync` is correctly idempotent.
- All four template files have complete placeholder substitution — no `{DisplayName}` or `{Email}` literals in output.
- `GetPgSchemaName` correctly handles the hyphen → underscore requirement for Entra OIDs.
- Config reads use the null-coalescing throw pattern — fast-fail if misconfigured, no silent defaults.

---

## What Tony needs to fix

**One required change before this ships:**

**`UserProvisioningService.cs` — Fix the `FailedStep` ternary.** Replace the `auroraRecordCreated` flag with step-boundary flags that are set only after each step fully completes. The rollback behavior is correct — this is a diagnostics fix only, but `ProvisioningException.FailedStep` carrying wrong values makes ops debugging harder and violates the interface contract.

The three nitpicks are non-blocking. N1 and N2 can go in the same commit if Tony wants to clean them up. N3 is a schema constraint note for the migration.

---

_Reviewed by Hawkeye — cycle 1/2. Fix C1, resubmit for cycle 2._
