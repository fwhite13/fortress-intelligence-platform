# Build Report — WI972
**Engineer:** Tony Stark  
**Date:** 2026-03-20  
**Commit:** `8faf09f`  
**Branch:** `main`  
**Pushed:** ✅ `git push origin main`

---

## CC Invocation

```bash
cd ~/projects/fip
cat ~/projects/fip/pipeline/WI972-CC-BRIEF.md | claude --model sonnet --dangerously-skip-permissions -p
```

---

## Summary

Task Center returning empty for real users because `FAMOS_QA_BYPASS=true` was active in ECS,
overriding all auth claims with `qa@fortressam.ai`. Three code fixes applied (Fix 1 is Rhodey's job
at deploy time — remove env var from ECS task def).

---

## Changes Made

### Fix 2 (P1) — QA bypass middleware guard — `Program.cs`

**File:** `famos/src/FamOs.Web/Program.cs`

Changed QA bypass condition from `||` (OR — dangerously broad) to `&&` (AND — dev-only):

```csharp
// BEFORE:
if (app.Environment.IsDevelopment() ||
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")

// AFTER:
if (app.Environment.IsDevelopment() &&
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
```

Also fixed the same `||` → `&&` in the `/qa/login` endpoint guard (~line 413).

Updated comment from "dev/staging only" to "dev only".

**Impact:** QA bypass can no longer activate in production even if env var is accidentally set.

### Fix 3 (P1) — OwnerUserId backfill — `Program.cs`

Added to DB init block (inside `db` scope, after migrations, before `app.Run()`):

```csharp
// WI972: Backfill OwnerUserId — empty string treated as unowned, breaks task filter
try
{
    await db.Database.ExecuteSqlRawAsync(
        "UPDATE opportunities SET OwnerUserId = NULL WHERE OwnerUserId = ''");
    logger.LogInformation("WI972: Backfilled empty OwnerUserId to NULL");
}
catch (Exception ex)
{
    logger.LogWarning("WI972: OwnerUserId backfill skipped: {Msg}", ex.Message);
}
```

**Impact:** 60 opportunities with empty-string `OwnerUserId` will be normalized to `NULL` on next
startup. Tasks on those opps were previously invisible to everyone; after deploy they'll remain
unowned (no tasks shown) but won't corrupt future filter results.

### Fix 4 (P2) — Null guard in TaskService — `TaskService.cs`

Added `t.Opportunity.OwnerUserId != null &&` to all 3 Where clauses:
- `GetOpenTasksForUserAsync`
- `GetOpenTasksPagedAsync`
- `GetOpenTaskCountForUserAsync`

**Impact:** Prevents null OwnerUserId from causing unexpected EF Core behavior or crashes in task
filter queries.

---

## Files Modified

| File | Change |
|------|--------|
| `famos/src/FamOs.Web/Program.cs` | Fix 2 (`||` → `&&` in 2 places) + Fix 3 (backfill block) |
| `famos/src/FamOs.Web/Services/TaskService.cs` | Fix 4 (null guard in 3 Where clauses) |

---

## Verification

```
# Fix 2: IsDevelopment guard
grep -n "IsDevelopment\|QA_BYPASS" Program.cs
367: // QA bypass — dev only (FAMOS_QA_BYPASS=true env var required)
369: if (app.Environment.IsDevelopment() &&
370:     Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
413:     if (!((app.Environment.IsDevelopment() &&
414:            Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true") &&

# Fix 3: backfill SQL
grep -n "WI972" Program.cs
342: // WI972: Backfill OwnerUserId — empty string treated as unowned, breaks task filter
347: logger.LogInformation("WI972: Backfilled empty OwnerUserId to NULL");
351: logger.LogWarning("WI972: OwnerUserId backfill skipped: {Msg}", ex.Message);

# Fix 4: null guard  
grep -n "OwnerUserId" TaskService.cs
27:  && t.Opportunity.OwnerUserId != null
28:  && t.Opportunity.OwnerUserId == userId
93:  && t.Opportunity.OwnerUserId != null
94:  && t.Opportunity.OwnerUserId == userId
123: && t.Opportunity.OwnerUserId != null
124: && t.Opportunity.OwnerUserId == userId
```

---

## Self-Review Checklist

- [x] Fix 2: Both occurrences of `||` changed to `&&` in QA bypass blocks
- [x] Fix 3: Backfill SQL is inside `db` scope (verified line placement)
- [x] Fix 3: Wrapped in try/catch with LogWarning — will not crash startup on failure
- [x] Fix 4: All 3 task filter methods updated (GetOpenTasksForUserAsync, GetOpenTasksPagedAsync, GetOpenTaskCountForUserAsync)
- [x] No other files touched — surgical changes only
- [x] Committed and pushed to `main`
- [x] Fix 1 (ECS env var removal) intentionally skipped — Rhodey's job at deploy time

---

## Fix 1 Note (Rhodey)

Fix 1 (P0) is a deployment config change — remove `FAMOS_QA_BYPASS=true` from the `famos-dev` ECS
task definition. This is NOT in this commit. Rhodey handles it at deploy time.

Without Fix 1, the `IsDevelopment()` guard in Fix 2 will prevent QA bypass from activating even
with the env var present (ECS is not IsDevelopment), so **Fix 2 alone already neutralizes the
production issue**. Fix 1 is still needed for cleanup.

---

## Acceptance Criteria Status

| Criteria | Status |
|----------|--------|
| QA bypass cannot activate in production | ✅ Fix 2 — `&&` guard, IsDevelopment() required |
| 60 empty-string OwnerUserId opps normalized | ✅ Fix 3 — backfill on startup |
| Task filter handles null OwnerUserId | ✅ Fix 4 — null guard in all 3 Where clauses |
| Fix 1 (ECS env var) deferred to Rhodey | ✅ Noted — not a code change |
