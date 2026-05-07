# Build Report — ADO#2868

**Task:** FAIT v2: Convert Entra auth to FIP shared cookie consumer pattern  
**Commit:** 380e2dd  
**Build:** ✅ SUCCEEDED — 0 errors, 0 warnings  
**Date:** 2026-05-06

---

## What was built

Replaced standalone Entra OIDC (`AddMicrosoftIdentityWebApp`) with the FIP shared cookie consumer
pattern. FAIT v2 now reads the `.FortressAI.Session` cookie set by `fip.fortressam.ai` via a shared
`DataProtectionKeys` table (same pattern as FIRM). No independent OIDC flow.

---

## Files changed

| File | Change |
|------|--------|
| `FortressAI.V2.Web.csproj` | Removed `Microsoft.Identity.Web` + `Microsoft.Identity.Web.UI`; added `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 8.0.*` |
| `Program.cs` | Removed `AddMicrosoftIdentityWebApp` + OIDC usings + `AddControllersWithViews` + `app.MapControllers()`; replaced with FIP cookie consumer auth + `SharedKeyRingDbContext` wiring + `DisableAutomaticKeyGeneration()`; added `/auth/redirect-to-login` → `fip.dev.fortressam.ai` |
| `Data/SharedKeyRingDbContext.cs` | **Created** — minimal `IDataProtectionKeyContext` DbContext, namespace `FortressAI.V2.Web.Data`, `DataProtectionKeys` table |
| `appsettings.json` | Removed `AzureAd` block; added `Auth.CookieName`, `FIP__LoginUrl`, `DataProtection.ApplicationName`; updated `FIP.LoginUrl` to `fip.dev.fortressam.ai` |

---

## Pattern used

Exact FIRM (`FortressIntelligenceRM.Web/Program.cs`) pattern replicated:
- `DefaultScheme` + `DefaultChallengeScheme` = `CookieAuthenticationDefaults.AuthenticationScheme`
- Cookie: `.FortressAI.Session`, `SameSite=Lax`, `SecurePolicy=Always`, `IsEssential=true`
- `Auth__CookieDomain` env var (double-underscore for ECS nested config)
- `SharedKeyRingDbContext` → `fred_dev.DataProtectionKeys`
- `SetApplicationName("FortressAI")` — matches FIRM and FIP portal
- `DisableAutomaticKeyGeneration()` — fait-v2 is a consumer, not a key generator

---

## Acceptance criteria verification

- [x] No `OpenIdConnect`, `AddMicrosoftIdentityWebApp` in Program.cs
- [x] `DefaultScheme` + `DefaultChallengeScheme` = Cookie
- [x] `SharedKeyRingDbContext` registered and wired to `AddDataProtection`
- [x] `DisableAutomaticKeyGeneration()` present
- [x] `/auth/redirect-to-login` endpoint exists → redirects to `FIP__LoginUrl`
- [x] `appsettings.json` has no `AzureAd` block
- [x] `Data/SharedKeyRingDbContext.cs` exists, namespace `FortressAI.V2.Web.Data`
- [x] `csproj` has no `Microsoft.Identity.Web` references
- [x] `csproj` has `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`

---

## Parallelization

No parallelization — single sequential CC run (all changes in one file set).

---

## CC sessions run

1 CC session (sonnet) — completed with 0 errors.

---

## Known edge cases / things Clint should scrutinize

1. **`Auth__CookieDomain` (double-underscore)** — ECS env var for nested config uses `__` separator. This matches FIRM's pattern. Verify ECS task def uses this exact name when Rhodey deploys.

2. **`FIP__LoginUrl` in appsettings.json** — Double-underscore in JSON key is unusual but valid as a fallback. The ECS env var `FIP__LoginUrl` will override it at runtime. CC preserved this intentionally.

3. **`app.MapControllers()` removed** — This was the MicrosoftIdentity challenge controller. No longer needed. Confirm no other routes relied on it (the only controller route was `/signin-oidc` and `/signout-callback-oidc`, both gone with OIDC).

4. **No `/auth/signin` route** — The old `LoginPath = "/auth/signin"` is replaced by `/auth/redirect-to-login`. Any hardcoded links to `/auth/signin` in Razor components should be checked (unlikely since auth is FallbackPolicy-driven).

5. **ECS env vars required at deploy time:**
   - `FORTRESS_DB_HOST` — Aurora host
   - `FORTRESS_DB_PORT` — default 3306
   - `FORTRESS_DB_USER` — default `fortress_mysql`
   - `FORTRESS_DB_PASS` — required
   - `FIP_KEYRING_DB_NAME` — set to `fred_dev`
   - `FIP__LoginUrl` — set to `https://fip.dev.fortressam.ai`
   - `Auth__CookieDomain` — set to `.fortressam.ai`

---

## How to test locally

1. Build: `cd ~/projects/fip/fait-v2/src/FortressAI.V2.Web && dotnet build` → 0 errors
2. Unauthenticated browser hit → redirected to `/auth/redirect-to-login` → redirected to `fip.dev.fortressam.ai`
3. After FIP portal login (on dev, with `.fortressam.ai` cookie), return to FAIT v2 → authenticated via shared cookie

---

_Sent to Clint Barton for review._

---

## BUILD Cycle 2 — ADO#2868

**Issue fixed:** I1 — Remove stale `AzureAd` block from `appsettings.Development.json`  
**Commit:** `d42f070`  
**Date:** 2026-05-07  
**Build:** ✅ SUCCEEDED — 0 errors, 0 warnings

### What was done
- Removed the `AzureAd` JSON object (TenantId, ClientId, ClientSecret) from `appsettings.Development.json`
- `appsettings.Production.json` does not exist — no action needed there
- JSON remains valid; all other config untouched

### Clint's original finding
Clint flagged `appsettings.Development.json` still contained a dead `AzureAd` config block from before the OIDC middleware was removed. This is now cleaned up.

_Cycle 2 complete. ADO comment posted (id: 781710)._
