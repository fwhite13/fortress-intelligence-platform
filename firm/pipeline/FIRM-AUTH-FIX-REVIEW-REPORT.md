# Review Report: FIRM Auth Architecture Fix

**Reviewer:** Hawkeye  
**Date:** 2026-03-08  
**Branch:** `firm-deploy`  
**Commit:** `5c041109`  
**Build Report:** `pipeline/FIRM-AUTH-FIX-BUILD-REPORT.md`

---

## Verdict: NEEDS-CHANGES

One Important issue — `StubAuthHandler.cs` is dead code that should be deleted. All 23 checklist items verified. No critical or blocking issues found. Auth architecture is correct and DataProtection ring sharing is properly implemented. One edge case around `CreateTablesAsync` and the shared `DataProtectionKeys` table is safe (explained below).

---

## Consistency Audit

**Files Cross-Referenced:**

| FIRM File | FAIT Reference | Check |
|-----------|---------------|-------|
| `Program.cs` → `.SetApplicationName("FortressAI")` | `Program.cs` → `.SetApplicationName("FortressAI")` | ✅ Exact match |
| `FirmDbContext.cs` → `ToTable("DataProtectionKeys")` | `AppDbContext.cs` → DbSet property name `DataProtectionKeys` (no override → EF default) | ✅ Same table |
| `Program.cs` → `ExpireTimeSpan = TimeSpan.FromHours(8)` | FAIT `Program.cs` → `TimeSpan.FromHours(8)` | ✅ Match |
| `Program.cs` → `SlidingExpiration = true` | FAIT `Program.cs` → `SlidingExpiration = true` | ✅ Match |
| `Program.cs` → `Auth:CookieDomain` config key | FAIT → same config key pattern | ✅ Match |
| `appsettings.json` → `"FIP": { "LoginUrl": "https://fait.dev.fortressam.ai/" }` | Redirect handler uses `FIP:LoginUrl` | ✅ Consistent |

**No undocumented cross-file dependencies found that are mismatched.**

---

## Checklist Verification

### OIDC Removal

| # | Check | Result |
|---|-------|--------|
| 1 | `FortressIntelligenceRM.Web.csproj`: No `Microsoft.AspNetCore.Authentication.OpenIdConnect` package ref | ✅ PASS — not present |
| 2 | `Program.cs`: No `AddOpenIdConnect` call | ✅ PASS — not present |
| 3 | `Program.cs`: No `entraAuthority`, `entraClientId`, `entraClientSecret` variables | ✅ PASS — not present |
| 4 | `Program.cs`: No `entraConfigured` variable or conditional | ✅ PASS — not present |
| 5 | `Program.cs`: No `UseStubAuth` / `StubAuthHandler` / stub auth block | ✅ PASS — file exists but is NOT referenced in Program.cs or any other file |
| 6 | `appsettings.json`: No `Auth:EntraAuthority`, `Auth:EntraClientId`, `Auth:EntraClientSecret` | ✅ PASS — only `Auth:CookieDomain` remains |
| 7 | `Program.cs`: No `using` statements referencing `OpenIdConnect` namespaces | ✅ PASS — no such usings |

### Cookie-Only Auth

| # | Check | Result |
|---|-------|--------|
| 8 | `AddAuthentication` sets both `DefaultScheme` AND `DefaultChallengeScheme` to `CookieAuthenticationDefaults.AuthenticationScheme` | ✅ PASS |
| 9 | `AddCookie` with `ExpireTimeSpan = TimeSpan.FromHours(8)` and `SlidingExpiration = true` | ✅ PASS |
| 10 | Cookie domain via `Auth:CookieDomain` config key | ✅ PASS |
| 11 | `/auth/redirect-to-login` handler exists, redirects to `FIP:LoginUrl` | ✅ PASS |
| 12 | `appsettings.json`: `FIP:LoginUrl` key present | ✅ PASS — value `"https://fait.dev.fortressam.ai/"` |
| 13 | `/auth/logout` only signs out the cookie scheme | ✅ PASS — `SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)` only, then redirect to `/` |

### DataProtection Key Ring Sharing

| # | Check | Result |
|---|-------|--------|
| 14 | `AddDataProtection().PersistKeysToDbContext<FirmDbContext>().SetApplicationName("FortressAI")` | ✅ PASS |
| 15 | `ApplicationName` is exactly `"FortressAI"` — matches FAIT | ✅ PASS — verified against FAIT `Program.cs` line 240 |
| 16 | `FirmDbContext.cs`: `DataProtectionKey` maps to table `"DataProtectionKeys"` (not `"firm_data_protection_keys"`) | ✅ PASS |
| 17 | `DatabaseInitializationService.cs`: `firm_data_protection_keys` NOT in `extraTables` | ✅ PASS — `extraTables` contains only FIRM business tables |
| 18 | `DatabaseInitializationService.cs`: No `CREATE TABLE firm_data_protection_keys` SQL | ✅ PASS |

### Login Page

| # | Check | Result |
|---|-------|--------|
| 19 | `Login.razor`: No OIDC challenge or `/auth/login` navigation | ✅ PASS |
| 20 | `Login.razor`: "Sign in with Fortress AI" button links to `FIP:LoginUrl` | ✅ PASS — `href="@_faitLoginUrl"` where `_faitLoginUrl = Config["FIP:LoginUrl"] ?? "https://fait.dev.fortressam.ai/"` |
| 21 | `Login.razor`: If already authenticated → redirects to `/meetings` | ✅ PASS |

### Critical Concern — LoginPath vs Redirect Handler

| # | Check | Result |
|---|-------|--------|
| 22 | `LoginPath = "/auth/redirect-to-login"` (local handler, not direct external URL) | ✅ PASS — correctly set to `new PathString("/auth/redirect-to-login")`. Local handler calls `ctx.Response.Redirect(faitLoginUrl)` directly without any `ReturnUrl` appended. |

### Critical Concern — DataProtection Table Access on Startup

| # | Check | Result |
|---|-------|--------|
| 23 | `DatabaseInitializationService`: DataProtectionKeys table creation is safe (no duplicate-table error on startup) | ✅ PASS — see analysis below |

**Table creation safety analysis:**  
`DatabaseInitializationService` calls `creator.CreateTablesAsync()` via EF Core, which will attempt to `CREATE TABLE DataProtectionKeys`. EF Core's `CreateTablesAsync` uses `CREATE TABLE IF NOT EXISTS` semantics on MySQL — it catches the error and treats it as non-fatal (wrapped in `try/catch` that logs a warning on failure, explicitly not-fatal). Even if EF Core were to generate a bare `CREATE TABLE`, the outer catch block at line 43-46 absorbs it with `LogWarning`. The `DataProtectionKeys` table is not in the `extraTables` raw SQL list at all, so there is no duplicate raw SQL attempt. The startup sequence is safe.

---

## Important Issues

### I1: `StubAuthHandler.cs` — Dead Code Not Deleted

- **File:** `src/FortressIntelligenceRM.Web/Auth/StubAuthHandler.cs`
- **Category:** Quality / cleanup
- **Issue:** `StubAuthHandler.cs` remains in the source tree but is entirely disconnected — `Program.cs` contains no reference to it whatsoever (no `using` import, no `AddScheme`, no `UseMiddleware`, no conditional). The build report claims "0 references to StubAuth in any FIRM source file" — this is **incorrect**; the class itself is one such file and it still defines the `StubAuth` scheme name as a string literal.
- **Impact:** Dead code left in place. Not a runtime risk since it's never registered. But it contradicts the build report's claim, creates confusion for future engineers ("Is this used? Is it on purpose?"), and is part of the infrastructure that was supposed to be removed.
- **Fix:**

```diff
- src/FortressIntelligenceRM.Web/Auth/StubAuthHandler.cs  (delete entire file)
```

Also delete the containing `Auth/` directory if it becomes empty.

---

## Nitpicks

- **N1:** `Login.razor` imports `@inject IConfiguration Config` but only uses it in code block. Acceptable in Blazor. Not blocking.
- **N2:** `appsettings.json` `FIP:LoginUrl` has a trailing slash (`"https://fait.dev.fortressam.ai/"`). The redirect handler also has the same trailing-slash fallback. Consistent — not an issue, just noting it's intentional.
- **N3:** `Program.cs` uses `new Microsoft.AspNetCore.Http.PathString("/auth/redirect-to-login")` inline rather than the string-to-PathString implicit conversion. Minor style inconsistency vs rest of file. Not blocking.

---

## Positive Observations

- **LoginPath architecture is exactly right.** Setting `LoginPath = "/auth/redirect-to-login"` (local) → handler calls `Response.Redirect(faitLoginUrl)` is the correct pattern. This avoids ASP.NET appending `?ReturnUrl=...` to FAIT's external URL. Well executed.
- **DataProtection setup is clean.** `FirmDbContext` correctly implements `IDataProtectionKeyContext`, the `DbSet<DataProtectionKey>` property name matches the table mapping, and `SetApplicationName("FortressAI")` matches FAIT exactly. The shared cookie ring will work.
- **FAIT's AppDbContext doesn't need to be touched.** FAIT uses EF's default naming convention (`DataProtectionKeys` DbSet name → table name). FIRM explicitly maps with `.ToTable("DataProtectionKeys")`. Both converge to the same table with no conflict.
- **Cookie domain pattern is production-ready.** Using `Auth:CookieDomain` from config, injected via ECS env vars, is the right approach for dev/prod environment separation.
- **Logout is correctly scoped.** `SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)` only — no lingering OIDC sign-out attempt that would 500 on missing middleware.
- **`extraTables` is clean.** All five FIRM business tables use `CREATE TABLE IF NOT EXISTS`. No `DataProtectionKeys` raw SQL. `DatabaseInitializationService` is idempotent.

---

## Acceptance Criteria Verification

- [x] **OIDC completely removed from FIRM** — Verified. No package ref, no `AddOpenIdConnect`, no Entra config variables in code or config.
- [x] **FIRM is cookie consumer only** — Verified. `DefaultScheme = DefaultChallengeScheme = CookieDefaults`. No other schemes registered.
- [x] **DataProtection key ring shared with FAIT** — Verified. Same table (`DataProtectionKeys`), same application name (`"FortressAI"`), different DbContext classes (safe co-existence).
- [x] **Unauthenticated users redirected to FAIT** — Verified via `LoginPath` → `/auth/redirect-to-login` handler → `FIP:LoginUrl`.
- [ ] **StubAuthHandler.cs deleted** — NOT done. File exists at `Auth/StubAuthHandler.cs`. Build report claim of "0 references" is misleading (the file itself is a reference).

---

## Summary

The auth architecture change is **substantively correct**. All critical plumbing is right: the DataProtection ring will share cookies with FAIT, the login redirect pattern is safe, OIDC is fully removed from the middleware pipeline. The only item blocking PASS is the leftover `StubAuthHandler.cs` — it's dead code, but it was explicitly called out as removed in the build report. Delete it and this is done.

**One fix required:** Delete `src/FortressIntelligenceRM.Web/Auth/StubAuthHandler.cs`.

---

_Reviewed by Hawkeye — Pipeline Stage 3 (Code Review)_
