# Review Report — ADO#2848: Per-user Fargate Task Lifecycle

**Reviewer:** Hawkeye (Clint Barton) — Code Reviewer  
**Date:** 2026-05-07  
**Commit:** c97fc00  
**Review Cycle:** 1 of 2  

---

## 🟡 Verdict: **NEEDS-CHANGES**

One confirmed Important issue must be fixed before this ships. All critical requirements pass. No security issues.

---

## Spec Compliance Check

**Brief:** ADO#2848: Per-user Fargate task lifecycle — IUserAgentRuntime + FargateUserAgentRuntime

**§2 Codebase Map (Files Modified):**
- ✅ `Services/IUserAgentRuntime.cs` — Created
- ✅ `Services/FargateUserAgentRuntime.cs` — Created
- ✅ `Program.cs` — Updated with DI registrations
- ✅ `appsettings.json` — Added Fargate config section
- ✅ `Data/Migrations/20260507032637_AddFargateColumnsToUserSession.cs` — Created

**§7 Acceptance Criteria:**
- ✅ `IUserAgentRuntime` defines all 5 methods: `EnsureRunningAsync`, `StopAsync`, `GetSessionAsync`, `IsHealthyAsync`, `SendTurnAsync`
- ✅ `SendTurnAsync` returns `IAsyncEnumerable<HarnessEvent>` (not `Task<>`)
- ✅ `FargateUserAgentRuntime` implements all 5 methods with correct signatures
- ✅ Aurora `user_sessions` accessed exclusively via `FaitV2DbContext` + EF; no raw ADO.NET
- ✅ `UserSession` model has 4 new properties with proper `[Column]` attributes and EF fluent config
- ✅ EF migration adds 4 columns with correct types and constraints
- ✅ DI: `IAmazonECS` registered as Singleton; `IUserAgentRuntime` as Scoped
- ✅ `dotnet build` result: **0 errors, 0 warnings**

**Spec compliance verdict:** ✅ **COMPLIANT** — All acceptance criteria met.

---

## Consistency Audit

**Schema → Entity → Code Alignment (MANDATORY):**

| Component | Field | EF Entity Property | [Column] Attribute | Migration Column | EF Fluent Config | Status |
|-----------|-------|-------------------|-------------------|------------------|------------------|--------|
| UserSession | TaskArn | ✅ `TaskArn` | ✅ `"task_arn"` | ✅ `"task_arn"` | ✅ `HasColumnName("task_arn")` | ✅ |
| UserSession | PrivateIp | ✅ `PrivateIp` | ✅ `"private_ip"` | ✅ `"private_ip"` | ✅ `HasColumnName("private_ip")` | ✅ |
| UserSession | FargateStatus | ✅ `FargateStatus` | ✅ `"fargate_status"` | ✅ `"fargate_status"` | ✅ `HasColumnName("fargate_status")` | ✅ |
| UserSession | FargateSessionId | ✅ `FargateSessionId` | ✅ `"fargate_session_id"` | ✅ `"fargate_session_id"` | ✅ `HasColumnName("fargate_session_id")` | ✅ |

**Result:** ✅ **ZERO MISMATCHES** — All 4 new Fargate columns perfectly aligned.

---

## Critical Issues

**None found.** ✅

---

## Important Issues

### I1: Double DbContext Registration — `AddDbContext` + `AddDbContextFactory` for Same Type
- **File:** `Program.cs`, lines 86–95 and 99–104
- **Category:** DRY violation / configuration
- **Issue:** `FaitV2DbContext` is registered twice — once via `AddDbContext<FaitV2DbContext>` and again via `AddDbContextFactory<FaitV2DbContext>` — with identical connection string options.
- **Evidence:**
  ```csharp
  // Line 86 — AddDbContext registration
  builder.Services.AddDbContext<FaitV2DbContext>(options =>
      options.UseMySql(
          builder.Configuration.GetConnectionString("DefaultConnection")!,
          new MySqlServerVersion(new Version(8, 0, 28)),
          mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)
      ));

  // Line 99 — AddDbContextFactory registration (same type, same options)
  builder.Services.AddDbContextFactory<FaitV2DbContext>(options =>
      options.UseMySql(
          builder.Configuration.GetConnectionString("DefaultConnection")!,
          new MySqlServerVersion(new Version(8, 0, 28)),
          mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)
      ));
  ```
- **Impact:** EF Core 8 handles this without a runtime crash (factory registration wins for `IDbContextFactory<>` injection; `AddDbContext` registration satisfies direct `FaitV2DbContext` injection). However:
  1. **DRY violation** — identical options block duplicated; any future change needs to be made in two places.
  2. **Confusing intent** — implies both a pooled scoped context AND a factory are needed. `FargateUserAgentRuntime` only uses the factory; no code directly injects `FaitV2DbContext` (unscoped).
  3. **Unnecessary service registration** — `AddDbContext` adds both `FaitV2DbContext` and `IDbContextFactory<FaitV2DbContext>` to the DI container. `AddDbContextFactory` then re-registers the factory. This is redundant.
- **Fix:** Remove `AddDbContext<FaitV2DbContext>` (lines 86–95). Keep only `AddDbContextFactory<FaitV2DbContext>`. `AddDbContextFactory` also registers `FaitV2DbContext` as transient for code that injects the context directly (if any exists).
  ```diff
  - // EF Core — FaitV2DbContext (Pomelo MySQL provider, GuidFormat=None in connection string)
  - builder.Services.AddDbContext<FaitV2DbContext>(options =>
  -     options.UseMySql(
  -         builder.Configuration.GetConnectionString("DefaultConnection")!,
  -         new MySqlServerVersion(new Version(8, 0, 28)),
  -         mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)
  -     ));
  
  // ...keep the factory registration:
    builder.Services.AddDbContextFactory<FaitV2DbContext>(options =>
        options.UseMySql(
            builder.Configuration.GetConnectionString("DefaultConnection")!,
            new MySqlServerVersion(new Version(8, 0, 28)),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)
        ));
  ```
  > ⚠️ **Before removing:** grep the codebase for any code that directly injects `FaitV2DbContext` (not `IDbContextFactory<FaitV2DbContext>`). If any Blazor pages or services do so, they'll need to migrate to using the factory too — or keep `AddDbContext` and remove `AddDbContextFactory`. The goal is one registration, not two.

---

## Investigated — Not an Issue

### I2 (Investigated — FALSE POSITIVE): CancellationTokenSource Leak in EnsureRunningAsync
- **Status:** ❌ FALSE POSITIVE — **no issue exists**
- **Investigation:** `EnsureRunningAsync` does NOT create a `CancellationTokenSource`. The method uses the caller-supplied `ct` directly throughout the entire poll loop. No linked CTS is created, so there is nothing to leak or dispose.
- **Verdict:** Clear.

---

## Nitpick Issues

### N1: HttpClient Not Explicitly Disposed in `SendTurnAsync`
- **File:** `Services/FargateUserAgentRuntime.cs`, ~line 285
- **Issue:** `var client = _httpClientFactory.CreateClient("HarnessClient")` used without `using`. Named HTTP clients from the factory are pooled and designed to be reused; the `HttpClient` wrapper is lightweight. Not a real leak, but inconsistent with `using var client` in other methods (if any).
- **Recommendation:** `using var client = _httpClientFactory.CreateClient("HarnessClient");` — harmless improvement.
- **Severity:** Nitpick — won't block.

### N2: Poll Interval Hardcoded
- **File:** `Services/FargateUserAgentRuntime.cs`, ~lines 147–148
- **Issue:** `const int MaxPolls = 30; const int PollDelayMs = 3000;` hardcoded. Fine for MVP.
- **Recommendation:** Extract to `appsettings.json` `Fargate:MaxPolls` / `Fargate:PollDelayMs` in a future pass.
- **Severity:** Nitpick — MVP acceptable.

### N3: DateTime vs DateTimeOffset Mixing
- **File:** `Services/FargateUserAgentRuntime.cs`, ~lines 125 & 371
- **Issue:** `DateTime.UtcNow` stored; `new DateTimeOffset(s.StartedAt, TimeSpan.Zero)` used when mapping. Functionally correct (all UTC), minor pattern inconsistency.
- **Severity:** Nitpick — not blocking.

---

## Positive Observations

1. **EnsureRunningAsync idempotency is solid** — checks Aurora first, then verifies in ECS, falls through to launch only if the task is genuinely gone. No double-launch risk.
2. **CS1621 fix is correct** — `yield return` in catch block properly resolved: error captured to variable, catch closes, yield occurs in plain `if` block after. Compiler-safe.
3. **All 5 interface methods present** — signatures exactly match spec, including `IAsyncEnumerable<HarnessEvent>` return on `SendTurnAsync`.
4. **Schema alignment is perfect** — Zero mismatches across entity, EF config, and migration for all 4 Fargate columns.
5. **appsettings.json Fargate section complete** — All 6 required keys present.
6. **DI lifetimes correct** — `IAmazonECS` Singleton, `IUserAgentRuntime` Scoped. No captive dependency issues.

---

## Build Status

✅ **Clean build:** 0 errors, 0 warnings (per Build Report; confirmed by prior cycle)

---

## What to Fix

**One change required before PASS:**

**`Program.cs`:** Remove the `AddDbContext<FaitV2DbContext>` registration (lines 86–95). Keep only `AddDbContextFactory<FaitV2DbContext>`.

> Before removing, run: `grep -rn "FaitV2DbContext\b" src/ --include="*.cs" | grep -v "AddDbContextFactory\|AddDbContext\|FaitV2DbContext.cs\|Migration"` to confirm no services inject the context directly (as opposed to via factory). If any do, migrate them to factory injection first.

---

## Summary

Strong implementation — spec-compliant, clean build, schema aligned, correct lifetimes, robust error handling. One Important fix needed (duplicate DI registration) before this ships. Not a blocker for staging, but should be cleaned up before the security scan cycle.

- **Spec Compliance:** ✅ 100%
- **Consistency:** ✅ Perfect
- **Correctness:** ✅ All logic verified
- **Code Quality:** 🟡 One DRY fix required
- **Security (layer 4):** ✅ No issues

**Fix I1 → re-verify → PASS.**

---

## Review Cycle 2 — Final Verdict

**Reviewer:** Hawkeye (Clint Barton)  
**Date:** 2026-05-07  
**Commit:** dda9573  

### Verdict: ✅ PASS

**I1 verification:**
- ✅ `AddDbContext<FaitV2DbContext>` is GONE — confirmed absent from `Program.cs`
- ✅ `AddDbContextFactory<FaitV2DbContext>` present at line 91 — factory registration intact
- ✅ Only `Program.cs` changed (8 lines deleted) — no scope creep
- ✅ Build: **0 errors, 0 warnings** (verified via `dotnet build --no-incremental`)

**CC review (sonnet):** All three checks PASS. No new issues introduced.

This task is clear. Ships.
