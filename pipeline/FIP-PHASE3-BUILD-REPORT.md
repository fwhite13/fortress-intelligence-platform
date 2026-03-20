# FIP Phase 3 — Build Report
**Task:** FIP Phase 3 — Convert FAIT, FIRM, FORMS to Cookie Consumers
**Builder:** Tony Stark (software-engineer)
**Date:** 2026-03-14
**Commit SHA:** `0eeb8fa`

---

## Summary

All three apps (FAIT, FIRM, FORMS) have been migrated to the shared `.FortressAI.Session` cookie consumer pattern. FAIT retains OIDC (Entra) as the auth entry point. FIRM and FORMS now consume the shared cookie, validated against the shared `fred_dev.DataProtectionKeys` table via `SharedKeyRingDbContext`. All three apps use `SetApplicationName("FortressAI")` and `DisableAutomaticKeyGeneration()`.

---

## Files Modified

### FAIT
- `fait/src/FortressAI.Web/Program.cs`

### FIRM
- `firm/src/FortressIntelligenceRM.Web/Program.cs`
- `firm/src/FortressIntelligenceRM.Web/Data/SharedKeyRingDbContext.cs` *(new)*

### FORMS
- `forms/src/FortressFormTools.Web/Program.cs`
- `forms/src/FortressFormTools.Web/FortressFormTools.Web.csproj`
- `forms/src/FortressFormTools.Data/SharedKeyRingDbContext.cs` *(new)*

---

## Exact Changes

### Part A — FAIT (`fait/src/FortressAI.Web/Program.cs`)

**1. `OnTokenValidated` — added persistent cookie properties:**
```csharp
// Added after existing roles mapping code:
ctx.Properties!.IsPersistent = true;
ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12);
```

**2. `AddDataProtection` — added `DisableAutomaticKeyGeneration()`:**
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();  // ADDED
```

**⚠️ SURPRISE — Cookie Domain config key mismatch:**
FAIT's cookie domain reads from `Auth:CookieDomain` (colon separator), NOT `Auth__CookieDomain` (double-underscore). FIRM uses the same colon pattern. FORMS (new) uses `Auth__CookieDomain` (double-underscore per spec). The env var naming convention must match the config key format:
- For ECS: `Auth__CookieDomain` maps to `Auth:CookieDomain` in .NET config (double-underscore = section separator in env vars). Both forms are equivalent in practice — ECS env var `Auth__CookieDomain` will correctly populate `Auth:CookieDomain` in the config system. ✅ **No code change needed.**

---

### Part B — FIRM

**New file: `firm/src/FortressIntelligenceRM.Web/Data/SharedKeyRingDbContext.cs`**
Minimal `IDataProtectionKeyContext` DbContext pointing to `fred_dev` (FAIT's DB) via `FIP_KEYRING_DB_NAME` env var.

**Updated: `firm/src/FortressIntelligenceRM.Web/Program.cs`**

Replaced DataProtection block — was using `FirmDbContext` (points to `firm_dev`), now uses `SharedKeyRingDbContext` (points to `fred_dev`):
```csharp
// NEW: Key ring connection vars (FIP_KEYRING_DB_NAME defaults to fred_dev)
var keyRingDbName = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";
// ... builds keyRingCsb ...

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString, ...));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()  // CHANGED from FirmDbContext
    .SetApplicationName("FortressAI")                  // unchanged
    .DisableAutomaticKeyGeneration();                  // ADDED
```

**⚠️ SURPRISE — FIRM's `FIP:LoginUrl` config key uses colon notation:**
FIRM currently reads `FIP:LoginUrl` and `FIP:FirmCallbackUrl` (colon), which maps correctly from env vars using double-underscore (`FIP__LoginUrl`). No code change needed — the env var additions below will wire correctly.

---

### Part C — FORMS

**New file: `forms/src/FortressFormTools.Data/SharedKeyRingDbContext.cs`**
Same pattern as FIRM. Located in the Data project (namespace `FortressFormTools.Data`) because FORMS Web has no separate `Data/` folder — Data classes live in the companion `FortressFormTools.Data` project.

**⚠️ NOTE — `ToTable` extension unavailable in Data project:**
The `FortressFormTools.Data.csproj` only references `Microsoft.EntityFrameworkCore` (base), not the relational extension. `ToTable()` is a relational extension method. Since EF's default naming convention maps `DbSet<DataProtectionKey> DataProtectionKeys` → table `DataProtectionKeys` automatically, the explicit `ToTable` call was omitted. This is functionally identical to the FIRM version.

**Updated: `forms/src/FortressFormTools.Web/Program.cs`**

1. **Removed** dead using statements:
   - `using Microsoft.AspNetCore.Authentication.OpenIdConnect;`
   - `using Microsoft.IdentityModel.Tokens;`
   - `using System.Security.Claims;`

2. **Removed** Cognito env var reads:
   ```csharp
   // DELETED:
   var cognitoAuthority = builder.Configuration["Auth:CognitoAuthority"];
   var cognitoClientId = builder.Configuration["Auth:CognitoClientId"];
   var cognitoClientSecret = builder.Configuration["Auth:CognitoClientSecret"];
   ```

3. **Replaced** entire AddAuthentication block:
   - Was: Cookie + Cognito OpenIdConnect OIDC
   - Now: Cookie-only consumer (`.FortressAI.Session`, domain from `Auth__CookieDomain`, LoginPath `/auth/redirect-to-login`)

4. **Replaced** DataProtection block:
   - Was: `PersistKeysToDbContext<AppDbContext>()` + `SetApplicationName("FortressFormTools")`
   - Now: `PersistKeysToDbContext<SharedKeyRingDbContext>()` + `SetApplicationName("FortressAI")` + `DisableAutomaticKeyGeneration()`

5. **Replaced** auth endpoints:
   - REMOVED: `/auth/login` (Cognito challenge), `/auth/logout` (signed out of both Cookie + OIDC)
   - ADDED: `/auth/redirect-to-login` (redirects to FIP portal), `/auth/forms-session` (cookie arrival endpoint)
   - REPLACED logout: cookie sign-out only, redirects to `/`

6. **Removed** duplicate `OpenIdConnect` package reference from `.csproj`.

**⚠️ SURPRISE — FORMS had full Cognito OIDC (not Entra, not FIP cookie pattern):**
FORMS was completely standalone with AWS Cognito — not integrated with the FIP portal at all. The auth block was significantly different from FIRM's. Full replacement was required as specified.

---

## Build Results

| App | Errors | Warnings | Build Time |
|-----|--------|----------|------------|
| FAIT | **0** | 29 (pre-existing MudBlazor) | 5.26s |
| FIRM | **0** | 0 | 1.62s |
| FORMS | **0** | 120 (pre-existing MudBlazor + PdfPig NuGet) | 2.13s |

All three: ✅ **BUILD SUCCEEDED — 0 errors**

---

## Commit

```
SHA:     0eeb8fa
Branch:  main
Message: feat: Phase 3 — FAIT/FIRM/FORMS shared cookie consumer migration

         - FAIT: IsPersistent=true in OnTokenValidated, DisableAutomaticKeyGeneration
         - FIRM: SharedKeyRingDbContext → fred_dev DataProtectionKeys, DisableAutomaticKeyGeneration
         - FORMS: Remove Cognito OIDC, add cookie consumer pattern, SetApplicationName FortressAI

Files:   6 changed, 131 insertions(+), 73 deletions(-)
         create mode: firm/src/FortressIntelligenceRM.Web/Data/SharedKeyRingDbContext.cs
         create mode: forms/src/FortressFormTools.Data/SharedKeyRingDbContext.cs
```

---

## Env Var Changes Required per ECS Task Def

> **For Rhodey to apply during DEPLOY stage.**

### `fred-dev` Task Def (FAIT)

| Action | Key | Value |
|--------|-----|-------|
| ADD/SET | `Auth__CookieDomain` | `.dev.fortressam.ai` |

> Note: `Auth__CookieDomain` (double-underscore) maps to `Auth:CookieDomain` in .NET config. FAIT's code reads `Auth:CookieDomain` — the double-underscore env var form is the correct ECS convention. ✅

---

### `firm-web` Task Def (FIRM)

| Action | Key | Value |
|--------|-----|-------|
| ADD | `FIP_KEYRING_DB_NAME` | `fred_dev` |
| ADD/SET | `Auth__CookieDomain` | `.dev.fortressam.ai` |
| VERIFY/ADD | `FIP__LoginUrl` | `https://fip.dev.fortressam.ai` |
| KEEP | `FORTRESS_DB_HOST` | *(existing — reused for key ring)* |
| KEEP | `FORTRESS_DB_USER` | *(existing — reused for key ring)* |
| KEEP | `FORTRESS_DB_PASS` | *(existing — reused for key ring)* |

> Note: FIRM currently reads `FIP:LoginUrl` (colon). ECS env var `FIP__LoginUrl` (double-underscore) maps correctly via .NET config system. ✅

---

### `formiq-dev` Task Def (FORMS)

| Action | Key | Value |
|--------|-----|-------|
| ADD | `FIP_KEYRING_DB_NAME` | `fred_dev` |
| ADD | `Auth__CookieDomain` | `.dev.fortressam.ai` |
| ADD | `FIP__LoginUrl` | `https://fip.dev.fortressam.ai` |
| ADD | `FIP__FormsCallbackUrl` | `https://forms.dev.fortressam.ai/auth/forms-session` |
| REMOVE | `Auth__CognitoAuthority` | *(no longer used)* |
| REMOVE | `Auth__CognitoClientId` | *(no longer used)* |
| REMOVE | `Auth__CognitoClientSecret` | *(no longer used)* |
| KEEP | `FORTRESS_DB_HOST` | *(existing — reused for key ring)* |
| KEEP | `FORTRESS_DB_USER` | *(existing — reused for key ring)* |
| KEEP | `FORTRESS_DB_PASS` | *(existing — reused for key ring)* |

---

## Surprises / Notes for Review

1. **FAIT `Auth:CookieDomain` already wired correctly** — The config key was already present. Env var `Auth__CookieDomain = .dev.fortressam.ai` is all that's needed in the task def. No code change was required for this.

2. **FAIT `SetApplicationName("FortressAI")` was already correct** — No change needed. Pre-existing.

3. **FIRM was 90% there** — Had cookie consumer pattern, correct `SetApplicationName`, correct cookie name/domain config. Only gap was DataProtection pointing at `firm_dev` instead of `fred_dev`. One DataProtection block swap + new `SharedKeyRingDbContext`.

4. **FORMS was completely standalone (Cognito, not FIP-aware)** — Largest change. Full auth rip-and-replace. The Cognito OIDC block, logout, login endpoints, and DataProtection were all replaced. `SetApplicationName` was "FortressFormTools" — critical fix to "FortressAI" for shared cookie decryption.

5. **`SharedKeyRingDbContext.ToTable()` removed from FORMS version** — The `FortressFormTools.Data` project doesn't reference the EF relational package so `ToTable()` is unavailable. The default EF convention (DbSet name = table name) achieves the same result for `DataProtectionKeys`. FIRM version retains `ToTable()` since it's in a Web project with full Pomelo/relational stack.

6. **Duplicate OpenIdConnect package reference in FORMS `.csproj`** — Was listed twice. Cleaned up as part of this change.

---

*Build complete. Ready for Hawkeye's review.*
