# Review Report: WI#972 — Task Center userId Fix
**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 1  
**Commit:** `8faf09f`  
**Verdict:** ✅ PASS  
**Date:** 2026-03-20

---

## Scope Check

**Files changed:** `famos/src/FamOs.Web/Program.cs`, `famos/src/FamOs.Web/Services/TaskService.cs`  
**Scope:** ✅ CLEAN — exactly the two expected files, nothing else.

---

## Fix 2 — IsDevelopment Guard on QA Bypass

### Middleware block (~line 369)
```csharp
if (app.Environment.IsDevelopment() &&
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
{
    app.Use(async (context, next) => { ... });
}
```
✅ **CORRECT.** The entire `app.Use(...)` registration is inside the `if` block — the middleware is **never registered in production**, not just skipped at request time. This is the stronger, correct approach.

In ECS (`ASPNETCORE_ENVIRONMENT=Production`): `IsDevelopment()` = false → block never executes → middleware never registered.

### `/qa/login` endpoint (~line 413)
```csharp
if (!((app.Environment.IsDevelopment() &&
       Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true") &&
      ctx.Request.Query["token"] == "natasha-qa-token-famos-dev"))
{
    return Results.Unauthorized();
}
```
✅ **CORRECT.** Logic: `!((IsDev && QABypass) && tokenMatches)`.  
In Production: `IsDev` = false → inner expression = false → `!(false)` = true → 401 Unauthorized.  
The `IsDevelopment()` capture closes over `app` at startup — fixed for lifetime of process. No issue.

**Both blocks: ✅ YES**

---

## Fix 3 — OwnerUserId Backfill (~line 342)

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
✅ **SQL correct.** Targets exactly the right condition.  
✅ **Idempotent.** Safe to run on every startup — 0 rows affected if none qualify.  
✅ **Timing correct.** Runs inside DB init scope before middleware starts — data is clean before any queries execute.  
✅ **Error handling correct.** Own try/catch, non-fatal (LogWarning), will not prevent startup.

**Minor nitpick (non-blocking):** `LogInformation` fires on every clean startup even when 0 rows are updated. A `LogDebug` for the zero-rows case would be cleaner. Not a defect.

**Present and wrapped in try/catch: ✅ YES**

---

## Fix 4 — Null Guard in TaskService

| Method | Line | Null check | Ordering |
|---|---|---|---|
| `GetOpenTasksForUserAsync` | 27–28 | `t.Opportunity.OwnerUserId != null &&` | ✅ Before equality check |
| `GetOpenTasksPagedAsync` | 93–94 | `t.Opportunity.OwnerUserId != null &&` | ✅ Before equality check |
| `GetOpenTaskCountForUserAsync` | 123–124 | `t.Opportunity.OwnerUserId != null &&` | ✅ Before equality check |

✅ **All 3 methods confirmed.** Null check precedes equality check — correct ordering.

**Note (informational):** `GetOpenTaskCountForUserAsync` does not call `.Include(t => t.Opportunity)` unlike the other two. EF Core implicitly joins on the navigation property when referenced in `Where`, so this is functionally correct. Not a defect, but worth knowing.

**Null guard in all 3 methods: ✅ YES**

---

## Critical Security Check: QA Bypass in ECS

**Confirmed safe.** If `FAMOS_QA_BYPASS=true` is set in ECS (`ASPNETCORE_ENVIRONMENT=Production`):
- `IsDevelopment()` returns **false**
- Middleware block is **never registered** — the bypass identity injection cannot happen
- `/qa/login` endpoint returns **401** immediately
- The env var has zero effect in production

This is the correct defense: gate on environment type, not just the env var.

---

## Pre-existing Issue (Not Introduced by This Commit)

⚠️ `/qa/status` endpoint (~line 403) has no `IsDevelopment()` guard. It always returns `{ qaBypass: true, environment: "dev" }` — including in production. This is misleading but not exploitable (no auth, no cookie issuance). Pre-existing and out of scope for this commit.

**Recommendation:** Follow-up ticket to wrap with `IsDevelopment()` guard or make response values dynamic.

---

## New Issues Introduced

**None.** No regressions observed.

---

## Summary

| Check | Result |
|---|---|
| Scope (only 2 expected files) | ✅ CLEAN |
| Fix 2: IsDevelopment middleware guard | ✅ YES — both blocks |
| Fix 2: QA bypass blocked in ECS/Production | ✅ CONFIRMED SAFE |
| Fix 3: Backfill SQL present | ✅ YES |
| Fix 3: Wrapped in try/catch | ✅ YES |
| Fix 4: Null guard in all 3 methods | ✅ YES |
| New regressions | ✅ NONE |

---

## Verdict: ✅ PASS

All three fixes are correctly implemented. The QA bypass — the highest-risk item — is properly guarded: the `IsDevelopment()` check ensures the middleware is never registered in ECS regardless of env var configuration. The backfill is idempotent and non-fatal. The null guards are consistent across all three query methods. Pipeline may advance to DEPLOY.

---

*Clint Barton / Hawkeye — REVIEW complete, cycle 1*
