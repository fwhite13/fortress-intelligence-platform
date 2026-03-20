# FIP Phase 3 — Review Report
**Reviewer:** Hawkeye (Clint Barton) — `code-reviewer`
**Commit:** `0eeb8fa`
**Date:** 2026-03-14
**Review Cycle:** 1 of 2

---

## Verdict: ⚠️ NEEDS-CHANGES

**2 issues require fixes before this can ship.** One is a build-risk/hygiene issue (#19); the other is a config-key inconsistency that will cause a silent runtime failure in FORMS on ECS (#23-note). All critical correctness items (#16, #9, #15) pass.

---

## Checklist Results

### FAIT Changes (Items 1–5)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | `ctx.Properties!.IsPersistent = true` in `OnTokenValidated` | ✅ PASS | Present at line ~183 |
| 2 | `ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)` in `OnTokenValidated` | ✅ PASS | Present, immediately after IsPersistent |
| 3 | `.DisableAutomaticKeyGeneration()` chained on `AddDataProtection()` | ✅ PASS | Line 293 |
| 4 | `SetApplicationName("FortressAI")` still present in FAIT | ✅ PASS | Line 292, unchanged |
| 5 | No other auth changes — OIDC flow untouched | ✅ PASS | Only `OnTokenValidated` block modified; all other OIDC events, scopes, callback paths intact |

### FIRM SharedKeyRingDbContext (Items 6–10)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 6 | `SharedKeyRingDbContext` implements `IDataProtectionKeyContext` + has `DbSet<DataProtectionKey> DataProtectionKeys` | ✅ PASS | `firm/Data/SharedKeyRingDbContext.cs` — class declaration correct, expression-bodied property uses `Set<DataProtectionKey>()` |
| 7 | DB connection uses `FIP_KEYRING_DB_NAME` env var (defaults to `"fred_dev"`) | ✅ PASS | `builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev"` |
| 8 | Connection string uses `MySqlConnectionStringBuilder` (not raw string interpolation) | ✅ PASS | `MySqlConnector.MySqlConnectionStringBuilder` used throughout FIRM key ring setup |
| 9 | `PersistKeysToDbContext<SharedKeyRingDbContext>()` (not `FirmDbContext`) | ✅ PASS | Line 115 of FIRM Program.cs — `SharedKeyRingDbContext` confirmed |
| 10 | `SetApplicationName("FortressAI")` in FIRM | ✅ PASS | Line 116, unchanged from prior |

### FIRM DataProtection (Items 11–12)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 11 | `.DisableAutomaticKeyGeneration()` added to FIRM's `AddDataProtection()` | ✅ PASS | Line 117 |
| 12 | FIRM's existing `FirmDbContext` still registered and unchanged | ✅ PASS | `AddDbContextFactory<FirmDbContext>` still present at top of FIRM Program.cs; `FirmDbContext.cs` untouched |

> **Note on FirmDbContext:** `FirmDbContext` still implements `IDataProtectionKeyContext` with a `DataProtectionKeys` DbSet (left over from before SharedKeyRingDbContext was introduced). This is now dead code — FIRM no longer calls `PersistKeysToDbContext<FirmDbContext>()` — but it's harmless and isn't a blocking issue for this PR. Flag for cleanup in a future sprint.

### FORMS SharedKeyRingDbContext (Items 13–16)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 13 | FORMS `SharedKeyRingDbContext` implements `IDataProtectionKeyContext`, has `DataProtectionKeys` DbSet | ✅ PASS | `forms/FortressFormTools.Data/SharedKeyRingDbContext.cs` — correct interface, expression-bodied DbSet |
| 14 | Same `FIP_KEYRING_DB_NAME` pattern (default `"fred_dev"`) | ✅ PASS | `builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev"` in FORMS Program.cs |
| 15 | `PersistKeysToDbContext<SharedKeyRingDbContext>()` in FORMS | ✅ PASS | Line 123, fully-qualified `FortressFormTools.Data.SharedKeyRingDbContext` |
| 16 | **CRITICAL: `SetApplicationName("FortressAI")` in FORMS** | ✅ PASS | Line 124 — exact string `"FortressAI"` confirmed. Comment on line 99 also documents why. |

### FORMS Cognito Removal (Items 17–20)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 17 | All three Cognito env var reads removed (`CognitoAuthority`, `CognitoClientId`, `CognitoClientSecret`) | ✅ PASS | Zero matches for any of these strings in FORMS Program.cs |
| 18 | Entire `AddOpenIdConnect(...)` block removed from FORMS | ✅ PASS | No `AddOpenIdConnect` or OIDC configuration code present in Program.cs |
| 19 | Dead `using Microsoft.AspNetCore.Authentication.OpenIdConnect;` removed | ❌ **FAIL** | The `using` directive is NOT in Program.cs (good). However, **`FortressFormTools.Web.csproj` still contains the `Microsoft.AspNetCore.Authentication.OpenIdConnect` package reference.** The commit diff reveals the original csproj had a duplicate entry — the commit deleted one copy, leaving the other. The package is still present as a dependency. This must be removed. |
| 20 | `DefaultChallengeScheme` is `CookieAuthenticationDefaults.AuthenticationScheme` (NOT OpenIdConnect) | ✅ PASS | Lines 36-37 of FORMS Program.cs — both `DefaultScheme` and `DefaultChallengeScheme` set to `CookieAuthenticationDefaults.AuthenticationScheme` |

### FORMS Cookie Consumer Pattern (Items 21–24)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 21 | `options.LoginPath = "/auth/redirect-to-login"` set in FORMS cookie options | ✅ PASS | Line ~42 |
| 22 | Cookie: `Name = ".FortressAI.Session"`, `SameSite = Lax`, `SecurePolicy = Always` | ✅ PASS | All three confirmed: `".FortressAI.Session"`, `SameSiteMode.Lax`, `CookieSecurePolicy.Always` |
| 23 | `Cookie.Domain` reads from `Auth__CookieDomain` config | ✅ PASS (with note) | Line 46: `builder.Configuration["Auth__CookieDomain"] ?? ""` — **uses double-underscore ECS env var syntax.** FIRM uses `Auth:CookieDomain` (colon syntax). Both are valid ASP.NET Core config patterns (ECS env vars use `__` as hierarchy separator), but they must be set consistently in the ECS task definitions. Verify that the ECS task def for FORMS sets `Auth__CookieDomain` (not `Auth__Cookie__Domain` or `Auth:CookieDomain`). |
| 24 | `.DisableAutomaticKeyGeneration()` added to FORMS | ✅ PASS | Line 125 |

### FORMS Endpoints (Items 25–28)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 25 | `/auth/redirect-to-login` redirects to `{FIP__LoginUrl}/auth/firm-callback?returnUrl={FormsCallbackUrl}` (URL-encoded) | ✅ PASS | `Uri.EscapeDataString(formsCallbackUrl)` used. Default fallback URL is `https://forms.dev.fortressam.ai/auth/forms-session`. Config key is `FIP__FormsCallbackUrl`. |
| 26 | `/auth/forms-session` authenticates the shared cookie and redirects to `/` | ✅ PASS | Calls `AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme)`, redirects to `/` on success |
| 27 | New `/auth/logout` clears cookie only (no OIDC sign-out) | ✅ PASS | `SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)` only — no OIDC sign-out code anywhere |
| 28 | All three FORMS endpoints have `.AllowAnonymous()` and `.DisableAntiforgery()` | ✅ PASS | All three (`/auth/redirect-to-login`, `/auth/forms-session`, `/auth/logout`) have both attributes chained |

### Build Integrity (Items 29–30)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 29 | All three projects build with 0 errors | ✅ PASS | FAIT: 0 errors (29 pre-existing warnings); FIRM: 0 errors, 0 warnings; FORMS: 0 errors (120 pre-existing MudBlazor warnings) |
| 30 | No accidental changes to unrelated files | ✅ PASS | FIRM's `/auth/firm-session`, `/auth/redirect-to-login`, and `/auth/logout` endpoints are all present and intact. Git diff confirms only 6 files modified, all expected. |

---

## Issues Requiring Fixes

### 🔴 CRITICAL — #19: Dead OpenIdConnect package reference not fully removed

**File:** `forms/src/FortressFormTools.Web/FortressFormTools.Web.csproj`

**Problem:** The original csproj had a duplicate `Microsoft.AspNetCore.Authentication.OpenIdConnect` entry (two identical lines). The commit removed one copy but left the other. The `OpenIdConnect` NuGet package is **still a dependency** of FORMS.

This is a build-risk issue: the package brings in OIDC middleware assemblies. Since no OIDC code exists in Program.cs, it won't throw at startup — but it's dead weight and a maintenance hazard. If someone accidentally re-introduces `AddOpenIdConnect` thinking it's already wired (because the package is there), it would throw at runtime with missing Cognito credentials.

**Fix:** Remove the remaining `PackageReference` line:
```xml
<!-- DELETE this line from FortressFormTools.Web.csproj -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.OpenIdConnect" Version="8.0.*" />
```

### ⚠️ IMPORTANT — #23 Config key inconsistency: `Auth__CookieDomain` vs `Auth:CookieDomain`

**Files:**
- FORMS Program.cs line 46: `builder.Configuration["Auth__CookieDomain"]`
- FIRM Program.cs line 81: `builder.Configuration["Auth:CookieDomain"]`

**Problem:** FORMS uses the double-underscore ECS env var convention (`Auth__CookieDomain`), while FIRM uses colon syntax (`Auth:CookieDomain`). In ECS, these are set via different env var names (`Auth__CookieDomain` maps to `Auth:CookieDomain` in the config system automatically when using `__` as hierarchy separator). They will both work correctly **as long as the ECS task definition uses the double-underscore form** (`Auth__CookieDomain`) for all apps.

This isn't a code bug — ASP.NET Core's `IConfiguration` normalizes `__` to `:` when reading environment variables — but the inconsistency across files is a maintenance hazard and could cause confusion when setting ECS task def env vars.

**Recommended fix:** Standardize to one pattern. Since FORMS has already adopted `__` (correct for ECS env vars), update FIRM to match:
```csharp
// firm/Program.cs line 81 — change to:
options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
```

---

## Focus Item Outcomes

| Focus Item | Status |
|------------|--------|
| **#16** `SetApplicationName("FortressAI")` in FORMS | ✅ CONFIRMED — exact string, not "FortressFormTools" |
| **#9** FIRM uses `SharedKeyRingDbContext` for `PersistKeysToDbContext` | ✅ CONFIRMED |
| **#15** FORMS uses `SharedKeyRingDbContext` for `PersistKeysToDbContext` | ✅ CONFIRMED |
| **#18** `AddOpenIdConnect` block fully gone from FORMS Program.cs | ✅ CONFIRMED — gone from code; **package reference is the remaining issue (#19)** |

---

## Non-blocking Observations (Nitpicks — fix in follow-up)

1. **FIRM `FirmDbContext` has orphaned `IDataProtectionKeyContext`** — `FirmDbContext` still implements the interface and maps `DataProtectionKeys` to a table, but is no longer used for data protection. Harmless but confusing.

2. **FORMS `redirect-to-login` always uses `/auth/firm-callback` on FAIT** — The endpoint name `firm-callback` is slightly misleading in the FORMS context (implies FIRM), but functionally correct since FAIT's `/auth/firm-callback` is a generic "redirect back to caller" endpoint. Consider renaming to `/auth/fip-callback` in a future sprint.

3. **FORMS `FortressFormTools.Data.csproj` doesn't reference `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` properly** — It references the package, which is correct. No issue.

---

## Summary

28/30 items pass. Two issues need fixing:

1. **#19 (Critical — remove dead package):** One remaining `OpenIdConnect` package reference in `FortressFormTools.Web.csproj` — the commit removed a duplicate but left the original. One-line fix.
2. **#23-note (Important — standardize config key):** `Auth__CookieDomain` in FORMS vs `Auth:CookieDomain` in FIRM. Functionally equivalent under ECS but inconsistent. Recommended to standardize.

**Send back to Tony with these specific fixes. No scope creep.**

---

*— Hawkeye out*
