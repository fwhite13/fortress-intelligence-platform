# Review Report — ADO#3126 — Fargate Session Lifecycle (Backend)

### Verdict: NEEDS-CHANGES

---

## Spec Compliance Check

**Brief:** v1 evolution sprint — port IUserAgentRuntime + FargateUserAgentRuntime from fait-v2 into v1. All changes additive only.

**§2 Codebase Map — Files Changed:**
- `src/FortressAI.Web/Services/IUserAgentRuntime.cs` — ✅ Created
- `src/FortressAI.Web/Services/FargateUserAgentRuntime.cs` — ✅ Created
- `src/FortressAI.Web/Data/Models/UserSession.cs` — ✅ Created
- `src/FortressAI.Web/Data/AppDbContext.cs` — ✅ UserSessions DbSet + OnModelCreating config added
- `src/FortressAI.Web/Program.cs` — ✅ DI registrations + /api/agent/status endpoint added
- `src/FortressAI.Web/Services/DatabaseInitializationService.cs` — ✅ user_sessions DDL block added
- `pipeline/ADO3126-migration.sql` — ✅ Migration SQL provided

**§6 Out of Scope:**
- ✅ No out-of-scope changes detected

**§7 Acceptance Criteria:**
- ✅ IUserAgentRuntime interface ported with correct v1 namespaces
- ✅ FargateUserAgentRuntime uses AppDbContext (not FaitV2DbContext)
- ✅ Zero v2 namespace references (FortressAI.V2.Web.*) in any changed file
- ✅ /api/agent/status endpoint present with RequireAuthorization()
- ✅ Migration SQL provided for Clint gate review

**Spec compliance verdict:** ✅ COMPLIANT (subject to issue #1 fix)

---

## Migration Safety Gate

| Check | Result |
|---|---|
| ANY `DROP TABLE` | ✅ Zero |
| ANY `DROP COLUMN` | ✅ Zero |
| `CREATE TABLE IF NOT EXISTS` | ✅ user_sessions table |
| `ALTER TABLE ADD COLUMN IF NOT EXISTS` | ✅ Both onboarding columns |
| New users columns nullable | ✅ Both `DATETIME(6) NULL` and `INT NULL` |
| user_sessions is entirely new | ✅ New table, no existing table touched |
| migration.sql ↔ DatabaseInitializationService.cs DDL parity | ✅ Byte-for-byte identical |
| Additive-only constraint | ✅ Met |

**Migration: ✅ SAFE — approved to run against fait_dev**

---

## Consistency Audit

**Files Cross-Referenced:**
- `UserSession.cs` [Table("user_sessions")] ↔ `migration.sql` `CREATE TABLE user_sessions` — ✅ Match
- `UserSession.cs` properties ↔ `AppDbContext.cs` `HasColumnName()` mappings ↔ `migration.sql` column names — ✅ All match exactly
  - `Id` property: no `HasColumnName` in EF config — MySQL column names are case-insensitive, `Id` → `id` works correctly (consistent with ChatAttachment pattern in this codebase)
  - `UserId` → `user_id` ✅, `StartedAt` → `started_at` ✅, `LastActiveAt` → `last_active_at` ✅, `EndedAt` → `ended_at` ✅, `TaskArn` → `task_arn` ✅, etc.
- `AppDbContext.cs` `UserSessions` DbSet ↔ `FargateUserAgentRuntime.cs` `db.UserSessions` — ✅ Name matches
- `IUserAgentRuntime.cs` interface ↔ `FargateUserAgentRuntime.cs` implementation — ✅ All 6 methods implemented
- `Program.cs` `AddSingleton<IUserAgentRuntime, FargateUserAgentRuntime>()` ↔ service uses `IDbContextFactory<AppDbContext>` — ✅ Singleton with factory pattern is correct

**Undocumented Dependencies:**
- `HarnessClient` named HttpClient registered at Program.cs line 304 ✅ — consumed by FargateUserAgentRuntime ✅
- `IAmazonECS` singleton at Program.cs line 261 ✅ — consumed by FargateUserAgentRuntime ✅

---

## Issues Found

| Severity | File | Line | Issue | Fix |
|----------|------|------|-------|-----|
| **Required** | `FargateUserAgentRuntime.cs` | 233 | `runResp.Tasks[0]` IndexOutOfRangeException if ECS returns empty Tasks with no Failures | Guard with `runResp.Tasks.Count == 0` check |
| Advisory | `FargateUserAgentRuntime.cs` | 20 | `_launchLocks` ConcurrentDictionary grows unbounded — one SemaphoreSlim per userId, never evicted | Low risk at current scale; document or add periodic cleanup |

### Issue #1 — Required Fix

**File:** `FargateUserAgentRuntime.cs`, line 233  
**Issue:** After `runResp.Failures.Count > 0` guard, code accesses `runResp.Tasks[0]` without checking whether `Tasks` is empty. ECS RunTask can return empty both `Tasks` and `Failures` on transient throttling or internal errors.  
**Fix:**
```diff
- if (runResp.Failures.Count > 0)
+ if (runResp.Failures.Count > 0 || runResp.Tasks.Count == 0)
  {
-     var reason = runResp.Failures[0].Reason;
+     var reason = runResp.Failures.FirstOrDefault()?.Reason ?? "RunTask returned no task and no failure reason";
      _logger.LogError("RunTask failed for user {UserId}: {Reason}", userId, reason);
      throw new InvalidOperationException($"Failed to start Fargate task: {reason}");
  }
```

---

## CC Review Summary

CC found the same two issues. Issue #1 (Tasks[0] guard) confirmed real. Issue #2 (unbounded dictionary) is accurate but negligible at current scale — flag it as advisory, not blocking. No false positives.

No v2 namespace leakage was found in any changed file. The migration SQL is fully safe. EF column mapping is correct throughout — explicit `HasColumnName` on all non-PK properties, and MySQL column-name case-insensitivity handles the `Id`/`id` discrepancy consistent with the ChatAttachment pattern already in production.

## Service Quality

- **Idempotency:** `EnsureRunningAsync` correctly checks DB then re-validates against ECS before launching — double-checked with per-user mutex ✅
- **Error handling:** ECS exceptions caught and logged throughout; DB errors propagate appropriately ✅
- **Security:** `/api/agent/status` uses `ClaimTypes.NameIdentifier` from claims (not query string), protected with `.RequireAuthorization()` ✅
- **DI:** IAmazonECS singleton matches v1 pattern for all AWS SDK clients ✅
- **AssignPublicIp = ENABLED:** Informational note — confirm this is correct for the target subnet topology (public subnets need this for egress without NAT)

---

## What Tony Needs to Fix

Fix issue #1 — one-line change at `FargateUserAgentRuntime.cs` line 233:

```csharp
// Before:
if (runResp.Failures.Count > 0)
{
    var reason = runResp.Failures[0].Reason;

// After:
if (runResp.Failures.Count > 0 || runResp.Tasks.Count == 0)
{
    var reason = runResp.Failures.FirstOrDefault()?.Reason ?? "RunTask returned no task and no failure reason";
```

That's the only required fix. Migration is approved as-is.

---

*Reviewed by Hawkeye (Clint) — ADO#3126 — 2026-05-09*
