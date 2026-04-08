# Review Report — NEXUS WI #1657
**Superseded status + re-discovery trigger**
**Reviewer:** Hawkeye | **Cycle:** 1 of 2 | **Commit:** `3f3877a` | **Date:** 2026-04-08

---

## Verdict: FAIL

Critical DB constraint violation makes the resume+changes path non-functional at runtime.

---

## Spec Compliance Check

**Files modified:**
- `Services/Discovery/IDiscoveryService.cs` — ✅ `SupersedeSessionAsync` added
- `Services/Discovery/DiscoveryService.cs` — ✅ `SupersedeSessionAsync` implemented
- `Components/Pages/NewSpecWizard.razor` — ✅ `HandleSubmit` replaced with async implementation

**Out of scope:** ✅ No out-of-scope changes detected.

**Acceptance criteria:**
- ✅ `SupersedeSessionAsync` preserves Q&A — no `.Include()`, status-only UPDATE, no cascade triggered
- ✅ `DiscoverySessionStatus.Superseded` enum constant used (confirmed: `const string Superseded = "Superseded"` in DiscoverySessionStatus.cs:13)
- ✅ `_discoverySession` cleared immediately after supersede call
- ❌ `InitiateDiscoveryAsync` will throw DB unique constraint violation — see Critical #1
- ✅ Navigation to `_activeStep = 2` is correct (Step 3 Discovery, 0-indexed)
- ✅ `_hasChanges == false` path does not call supersede or initiate discovery
- ✅ Null guard on `_discoverySession` is correct
- ✅ TODO comments for WI #1655 and WI #1659 placed correctly

**Spec compliance verdict:** ❌ NON-COMPLIANT — critical runtime failure blocks acceptance criterion 4.

---

## Consistency Audit

**DbFactory pattern:** ✅ `await using var db = await _dbFactory.CreateDbContextAsync(ct)` — matches `SkipDiscoveryAsync` and `InitiateDiscoveryAsync`

**Logging prefix:** ✅ `[DISCOVERY]` prefix used in `SupersedeSessionAsync` log message (line 177)

**`_submissionId` thread safety:** ⚠️ See Nitpick N1

---

## Critical Issues — 1

### C1: `InitiateDiscoveryAsync` will fail with DB unique constraint violation after supersede

**File:** `DiscoveryService.cs` (InitiateDiscoveryAsync ~line 50), migration `20260407180206_AddDiscoveryConversation.cs` lines 121-124

**Category:** correctness / schema

**Issue:** The `discovery_sessions` table has a unique index on `submission_id`:

```sql
-- Migration 20260407180206_AddDiscoveryConversation.cs
CREATE UNIQUE INDEX IX_discovery_sessions_submission_id
ON discovery_sessions (submission_id);
```

EF model snapshot confirms:
```csharp
// NexusDbContextModelSnapshot.cs
b.HasIndex("SubmissionId").IsUnique();
```

`SupersedeSessionAsync` performs an UPDATE only — it sets `Status = "Superseded"` but the row remains in the table. When `InitiateDiscoveryAsync` immediately follows (NewSpecWizard.razor line 480), it INSERTs a new `DiscoverySession` with the same `SubmissionId`:

```csharp
var session = new DiscoverySession { Id = Guid.NewGuid(), SubmissionId = submissionId, ... };
db.DiscoverySessions.Add(session);
await db.SaveChangesAsync(ct);  // ← MySqlException: Duplicate entry for IX_discovery_sessions_submission_id
```

The `catch (Exception ex)` in `HandleSubmit` shows the user `"Submit failed: Duplicate entry..."` — every resume+changes submission fails.

**Impact:** The primary feature of this WI — re-discovery on resume with changes — is 100% non-functional at runtime. Build passes clean; this only surfaces at runtime.

**Fix (Option A — recommended, no migration required):**
Change `SupersedeSessionAsync` to DELETE the session instead of updating it. Questions and answers are cascade-deleted on `DiscoverySession` delete (per existing FK cascade behavior), but this is intentional for a superseded session — it's being replaced. Rename the method to `DeleteSessionForSupersessionAsync` or keep the name but change the behavior. The supersede status concept then lives only in a log entry (or add a new `SupersededSessions` audit table if the audit requirement is strict).

Actually, re-reading the review spec: "Questions and answers must survive for audit." This means DELETE is **not** acceptable.

**Fix (Option B — correct, requires migration):**
1. Add a new migration to **drop** `IX_discovery_sessions_submission_id` (move from 1:1 to 1:many relationship between `Submission` and `DiscoverySession`)
2. Update EF model in `NexusDbContext.OnModelCreating` to remove the `HasIndex(...).IsUnique()` constraint
3. Update `GetSessionAsync` to add `OrderByDescending(s => s.CreatedAt)` and filter out superseded sessions: `.Where(s => s.Status != DiscoverySessionStatus.Superseded)`
4. This ensures the active (newest, non-superseded) session is always returned

Option B is the architecturally correct path given the audit requirement.

---

## Important Issues — 1

### I1: `GetSessionAsync` lacks ordering — will be critical if unique index is dropped

**File:** `DiscoveryService.cs` ~line 85

```csharp
return await db.DiscoverySessions
    .Include(s => s.Questions).ThenInclude(q => q.Answer)
    .FirstOrDefaultAsync(s => s.SubmissionId == submissionId, ct);
```

No `OrderBy`. `FirstOrDefaultAsync` without ordering is non-deterministic. Currently safe because the unique index prevents multiple sessions per submission. If the unique index is dropped (required fix for C1 Option B), this method **must** add:
- `OrderByDescending(s => s.CreatedAt)` — to get the most recent session
- `.Where(s => s.Status != DiscoverySessionStatus.Superseded)` — to skip superseded sessions

Without this, the background poll in `HandleSubmit` may return the old superseded session to the component, causing the user to see stale questions.

**This is a required co-change with the C1 Option B fix.**

---

## Nitpicks — 1

### N1: `InvokeAsync(StateHasChanged)` in fire-and-forget `finally` block is unprotected from circuit-disposal

**File:** `NewSpecWizard.razor` ~line 509

```csharp
finally
{
    _discoveryLoading = false;
    await InvokeAsync(StateHasChanged);  // throws ObjectDisposedException if user navigates away
}
```

If the user navigates away during the 15-second poll window, `InvokeAsync` throws `ObjectDisposedException`, which propagates as an unhandled exception from the fire-and-forget task. This is consistent with the pre-existing pattern at GoToStep2Discovery (~line 424). Low priority — Blazor's circuit dispatcher typically swallows this silently — but both locations benefit from:

```csharp
finally
{
    _discoveryLoading = false;
    try { await InvokeAsync(StateHasChanged); } catch (ObjectDisposedException) { }
}
```

Not blocking.

---

## Positive Observations

- **Q&A preservation is solid.** `SupersedeSessionAsync` fetches without `.Include()`, meaning EF tracks no navigation collections. A status UPDATE triggers no cascade side-effects on Questions/Answers. The intent is correct — the bug is at the DB layer, not the service layer.
- **Null guard on `_discoverySession` is architecturally correct.** A resume with no prior session still initiates fresh discovery; the guard is inside the null check and `InitiateDiscoveryAsync` is outside it.
- **Exception handling on `_isSubmitting` is sound.** Set before `try`, reset in `catch`. The early `_isSubmitting = false` before the fire-and-forget launch is correctly placed so the button is re-enabled while the poll runs in background.
- **TODO placement is excellent.** WI #1655 and #1659 stubs are at exactly the right insertion points for future work, making the deferred paths unambiguous.
- **DB context and logging patterns are consistent** with all other `DiscoveryService` methods.
- **Step index is correct.** `_activeStep = 2` correctly maps to the Discovery step (0-indexed third step).

---

## What Tony Needs to Fix

### Fix C1 (blocking):
Add a new migration to drop the unique index on `discovery_sessions.submission_id`:

```csharp
// New migration: RemoveDiscoverySessionUniqueIndex
migrationBuilder.DropIndex(
    name: "IX_discovery_sessions_submission_id",
    table: "discovery_sessions");
```

Update `NexusDbContext.OnModelCreating` to remove the `HasIndex("SubmissionId").IsUnique()` call.

### Fix I1 (required co-change with C1):
Update `GetSessionAsync` in `DiscoveryService.cs`:

```csharp
// Before:
return await db.DiscoverySessions
    .Include(s => s.Questions).ThenInclude(q => q.Answer)
    .FirstOrDefaultAsync(s => s.SubmissionId == submissionId, ct);

// After:
return await db.DiscoverySessions
    .Include(s => s.Questions).ThenInclude(q => q.Answer)
    .Where(s => s.SubmissionId == submissionId && s.Status != DiscoverySessionStatus.Superseded)
    .OrderByDescending(s => s.CreatedAt)
    .FirstOrDefaultAsync(ct);
```

---

## Summary

The service-layer implementation is clean and logically correct. The single critical failure is that this WI did not update the DB schema to support multiple sessions per submission — the unique index that enforced the old 1:1 relationship is still in place and will immediately crash `InitiateDiscoveryAsync` after supersede. Fix is a small migration + one method update. No architectural rework needed.

---

---

## Review Report — NEXUS WI #1657
**Superseded status + re-discovery trigger**
**Reviewer:** Hawkeye | **Cycle:** 2 | **Commit:** `3dc9f58` | **Date:** 2026-04-08

---

## Verdict: NEEDS-CHANGES

The two critical Cycle 1 issues are correctly fixed. One important issue remains: the EF model still declares a `1:1` relationship between `Submission` and `DiscoverySession` after the unique index was dropped (enabling `1:N` at the DB layer). The `NexusDbContext.cs` `HasOne/WithOne` configuration was not updated.

---

## Spec Compliance Check

**Files expected in scope:** Migration `.cs`, migration `.Designer.cs`, `NexusDbContextModelSnapshot.cs`, `DiscoveryService.cs`

| File | Status |
|------|--------|
| `Migrations/20260408180000_DropDiscoverySessionsUniqueSubmissionIndex.cs` | ✅ Present |
| `Migrations/20260408180000_DropDiscoverySessionsUniqueSubmissionIndex.Designer.cs` | ✅ Present |
| `Migrations/NexusDbContextModelSnapshot.cs` | ✅ Updated (IsUnique removed) |
| `Services/Discovery/DiscoveryService.cs` | ✅ Updated (Where + OrderBy added) |
| `Data/NexusDbContext.cs` | ⚠️ NOT updated — WithOne/WithOne still in place (see Important I1) |

**Out of scope:** Pipeline docs touched (fine). No unexpected application logic changes.

**Spec compliance verdict:** ✅ PARTIALLY COMPLIANT — the Cycle 1 critical fixes are present; one omission (NexusDbContext.cs relationship cardinality) creates a model-schema mismatch.

---

## Consistency Audit

| Check | Result |
|-------|--------|
| Migration index name vs. previous migration `20260407180206_AddDiscoveryConversation.cs` line 121 | ✅ Exact match: `IX_discovery_sessions_submission_id` |
| `NexusDbContextModelSnapshot.cs` — `IsUnique()` gone from DiscoverySession index | ✅ Confirmed: `b.HasIndex("SubmissionId")` with no `.IsUnique()` |
| `NexusDbContext.cs` relationship cardinality updated | ❌ Still `HasOne/WithOne` — see I1 |
| `GetSessionAsync` uses `DiscoverySessionStatus.Superseded` constant | ✅ Enum constant, not raw string |
| All `GetSessionAsync` call sites use `int` submissionId | ✅ All four call sites verified |

---

## Critical Issues — 0

None. The Cycle 1 critical (Duplicate entry crash) is resolved.

---

## Important Issues — 1

### I1: `NexusDbContext.cs` relationship not updated from 1:1 to 1:N

**File:** `Data/NexusDbContext.cs` (lines 73–77)
**Category:** model/schema consistency

**Issue:** The unique index was dropped, enabling multiple `DiscoverySession` rows per `Submission` at the DB level. However, the EF model was not updated:

```csharp
// NexusDbContext.cs line 73-77 — STILL UNCHANGED
entity.HasOne(e => e.DiscoverySession)
      .WithOne(ds => ds.Submission)
      .HasForeignKey<DiscoverySession>(ds => ds.SubmissionId)
      .OnDelete(DeleteBehavior.Cascade)
      .IsRequired(false);
```

The snapshot reflects the same mismatch (`WithOne("DiscoverySession")` on the Submission side, line ~539).

**Impact:** Current code paths (`GetSessionAsync`, `InitiateDiscoveryAsync`, `SupersedeSessionAsync`) all query `DiscoverySessions` directly and are unaffected. However, `submission.DiscoverySession` nav property — used anywhere a `Submission` is loaded with `.Include(s => s.DiscoverySession)` — will silently return only one row when multiple sessions exist. EF will not throw; it will just return an indeterminate session. This is a correctness landmine for any future code touching submissions after re-discovery.

The snapshot's `NexusDbContextModelSnapshot.cs` will self-resolve once `NexusDbContext.cs` is fixed and the snapshot regenerated.

**Fix:**

In `NexusDbContext.cs`, update the Submission→DiscoverySession relationship (currently inside the `Submission` entity block, around line 73):

```diff
- entity.HasOne(e => e.DiscoverySession)
-       .WithOne(ds => ds.Submission)
-       .HasForeignKey<DiscoverySession>(ds => ds.SubmissionId)
-       .OnDelete(DeleteBehavior.Cascade)
-       .IsRequired(false);
+ entity.HasMany(e => e.DiscoverySessions)
+       .WithOne(ds => ds.Submission)
+       .HasForeignKey(ds => ds.SubmissionId)
+       .OnDelete(DeleteBehavior.Cascade);
```

In `Models/Entities/Submission.cs`, update the nav property:
```diff
- public DiscoverySession? DiscoverySession { get; set; }
+ public ICollection<DiscoverySession> DiscoverySessions { get; set; } = [];
```

After making these changes:
1. Run `dotnet ef migrations add UpdateDiscoverySessionRelationship` — verify the generated migration has **no `migrationBuilder` calls** (no DB changes needed; only model metadata changes). If EF emits DB changes, something is misconfigured.
2. Run `dotnet build` — 0 errors.
3. Regenerate snapshot: `dotnet ef database update` or let the Designer pick it up.

Note: Any existing code that references `submission.DiscoverySession` (singular) will need to be updated to `.DiscoverySessions.FirstOrDefault(...)`.

---

## Nitpicks — 0

---

## Positive Observations

- **Migration is correct.** Index name exactly matches the previous migration's `CreateIndex` name. `Up()` drops then recreates non-unique. `Down()` is fully symmetric. `unique: false` is valid EF Core syntax.
- **`GetSessionAsync` is clean.** The Where/OrderBy/Include chain is correct EF Core 8 syntax. Using the enum constant (not a raw string) for the status filter is the right pattern. All four call sites pass `int` — no type confusion.
- **Build is clean.** `dotnet build` — 0 errors, 0 warnings.
- **Snapshot index block is correct.** `b.HasIndex("SubmissionId")` with no `.IsUnique()` — exactly what's needed.

---

## What Tony Needs to Fix

### Fix I1 (required for PASS):

1. Update `NexusDbContext.cs` — change `HasOne/WithOne` to `HasMany/WithOne` for the `Submission→DiscoverySession` relationship (see diff above).
2. Update `Submission.cs` entity — change `DiscoverySession?` nav property to `ICollection<DiscoverySession>`.
3. Run `dotnet ef migrations add UpdateDiscoverySessionRelationship` — verify empty migration (no DB ops).
4. Run `dotnet build` — 0 errors.
5. Regenerate or confirm snapshot is updated.
6. Check for any call sites using `submission.DiscoverySession` (singular) — update to `.DiscoverySessions.FirstOrDefault(...)`.

This is a small change (~15 lines) with no DB migration delta expected.

---

## Summary

Cycle 1 critical issues resolved. One important omission: `NexusDbContext.cs` relationship cardinality was not updated to match the new 1:N schema. The fix is small and low-risk. Build is clean.

