# WI#939 Investigation Report — FAM OS Submissions Crash

**Investigator:** Hawkeye (Clint Barton)  
**Date:** 2026-03-20  
**Error:** `Can't convert Text to Int32` — workspace crashes when opportunity has carrier submissions

---

## Root Cause: Confirmed

**The `submissions.Status` column is `longtext` in Aurora, but EF is configured to read it as `int`.**

### The Mismatch

| Layer | Type |
|-------|------|
| DB column (`submissions.Status`) | `longtext` |
| C# property (`Submission.Status`) | `SubmissionStatus` enum |
| DbContext mapping | `.HasConversion<int>()` |

**What happened:** `Program.cs` creates tables via `CreateTablesAsync()` (EF's relational DB creator). At the time the `submissions` table was first created, the `Status` property had no explicit `HasColumnType` — only `HasConversion<int>()`. EF's MySQL provider generates `longtext` for string/enum columns by default when `HasColumnType` is not specified, even if `HasConversion<int>()` is present. The conversion tells EF how to *serialize/deserialize*, but does NOT influence the DDL column type used during table creation.

Result: column is stored as `longtext` in Aurora, but when EF reads rows and tries to materialize `SubmissionStatus` via the `int` conversion, MySQL throws "Can't convert Text to Int32".

### Data Status

```sql
SELECT COUNT(*) FROM submissions;  -- Returns: 0
```

**The table is empty.** There is no existing data to migrate or transform. This is a clean `MODIFY COLUMN`.

---

## Fix: Option A — ALTER TABLE MODIFY COLUMN (Recommended)

Add to `Program.cs` startup migration block alongside the other `ALTER TABLE submissions` statements.

**Exact SQL:**
```sql
ALTER TABLE submissions MODIFY COLUMN Status INT NOT NULL DEFAULT 0;
```

**Where to add in Program.cs:** After the existing `ALTER TABLE submissions` block (around line 236), add:

```csharp
// WI#939: Fix Status column type mismatch (longtext → int)
try
{
    await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE submissions MODIFY COLUMN Status INT NOT NULL DEFAULT 0");
    logger.LogInformation("submissions.Status column migrated to INT");
}
catch (Exception ex)
{
    logger.LogWarning("submissions.Status MODIFY skipped (may already be INT): {Msg}", ex.Message);
}
```

**Why this is safe:**
- Table has 0 rows — no data conversion required
- `MODIFY COLUMN` on an empty table is instantaneous
- The try/catch handles the case where the column is already `INT` on a fresh environment (idempotent)
- No migration framework required — consistent with the existing `ADD COLUMN` pattern throughout Program.cs

**No changes needed to:**
- `Submission.cs` — entity model is correct
- `FamOsDbContext.cs` — `.HasConversion<int>()` mapping is correct, just never influenced the DDL
- `OpportunityService.cs` — include chain is correct

---

## Why Option B (HasConversion string) Is NOT the fix

The table is empty and the intent is clearly integer storage (enum int values). Option B would work as a workaround for populated tables with string-named values — not applicable here.

---

## Files Audited

| File | Finding |
|------|---------|
| `Data/Entities/Submission.cs` | ✅ Correct — `SubmissionStatus` enum, `.HasConversion<int>()` |
| `Data/FamOsDbContext.cs` line 60 | ✅ Correct — `HasConversion<int>()` present |
| `Program.cs` lines 227–236 | ⚠️ Missing MODIFY COLUMN for Status — all other submissions columns added here |
| `Services/OpportunityService.cs` line 48 | ✅ Correct — `.Include(o => o.Submissions)` is standard |
| Aurora `submissions` table | ❌ `Status` is `longtext NOT NULL` — must be `INT NOT NULL DEFAULT 0` |

---

## Exact Fix for Tony

**File:** `~/projects/fip/famos/src/FamOs.Web/Program.cs`  
**Location:** After line 236 (after the `UpdatedAt` ADD COLUMN try/catch)

```csharp
// WI#939: Fix Status column type mismatch (was created as longtext, must be int)
try
{
    await db.Database.ExecuteSqlRawAsync(
        "ALTER TABLE submissions MODIFY COLUMN Status INT NOT NULL DEFAULT 0");
    logger.LogInformation("submissions.Status column migrated to INT");
}
catch (Exception ex)
{
    logger.LogWarning("submissions.Status MODIFY skipped (already correct or failed): {Msg}", ex.Message);
}
```

**That's it. One try/catch block. No model changes. No DbContext changes.**

---

## Confidence: HIGH

The error message, the column DDL, and the EF mapping all point to exactly one cause. The fix is surgical and idempotent.
