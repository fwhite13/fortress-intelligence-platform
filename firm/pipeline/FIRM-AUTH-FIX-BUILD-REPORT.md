# FIRM Auth Architecture Fix — Build Report

**Date:** 2026-03-08
**Branch:** `firm-deploy`
**Commit:** `5c041109`

---

## Summary

FIRM has been refactored from an OIDC initiator to a **cookie consumer only**. All Entra/OpenIdConnect middleware has been removed. FIRM now redirects unauthenticated users to FAIT for login.

---

## Checklist

### ✅ OIDC Package Removed
- `Microsoft.AspNetCore.Authentication.OpenIdConnect` removed from `FortressIntelligenceRM.Web.csproj`
- Confirmed: 0 references to `OpenIdConnect`, `AddOpenIdConnect`, `entraConfigured`, `StubAuth` in any FIRM source file

### ✅ DataProtectionKeys Table Name Matches FAIT
- `FirmDbContext.OnModelCreating` now maps: `modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys")`
- **Was:** `firm_data_protection_keys`
- **Now:** `DataProtectionKeys` ← matches FAIT exactly
- `DatabaseInitializationService.cs`: `firm_data_protection_keys` entry removed from `extraTables`
- `Program.cs`: `.SetApplicationName("FortressAI")` ← matches FAIT exactly

### ✅ `/auth/redirect-to-login` Handler Added
```csharp
app.MapGet("/auth/redirect-to-login", ctx =>
{
    var faitLoginUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP:LoginUrl"]
        ?? "https://fait.dev.fortressam.ai/";
    ctx.Response.Redirect(faitLoginUrl);
    return Task.CompletedTask;
});
```

### ✅ Logout Handler Simplified
- `/auth/logout` now clears the local cookie only — no OIDC sign-out
- Redirects to `/` after clearing cookie

### ✅ appsettings.json Updated
- Removed: `Auth:EntraAuthority`, `Auth:EntraClientId`, `Auth:EntraClientSecret`, `UseStubAuth`
- Added: `FIP:LoginUrl` = `"https://fait.dev.fortressam.ai/"`
- Kept: `Auth:CookieDomain` (empty by default, set in ECS env)

### ✅ Login.razor Updated
- No longer shows "Sign in with Microsoft" / triggers local OIDC
- Shows "Sign in with Fortress AI" button → links to `FIP:LoginUrl`
- Redirects to `/meetings` if already authenticated

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:03.65
```

**Status: ✅ PASS — 0 errors, 0 warnings**

---

## Architecture After Fix

```
FAIT (fait.dev.fortressam.ai)
  ├── Owns AddOpenIdConnect — the ONLY OIDC middleware in FIP
  ├── Handles Entra login → sets .AspNetCore.Cookies scoped to .dev.fortressam.ai
  └── DataProtection keys in DataProtectionKeys table

FIRM (firm.dev.fortressam.ai)
  ├── Cookie consumer ONLY — no OIDC, no AddOpenIdConnect
  ├── Reads FAIT's auth cookie (same DataProtection key ring: DataProtectionKeys)
  ├── Unauthenticated → /auth/redirect-to-login → https://fait.dev.fortressam.ai/
  └── DataProtection: PersistKeysToDbContext<FirmDbContext>() + SetApplicationName("FortressAI")
```

---

## Files Changed

| File | Change |
|------|--------|
| `Program.cs` | Removed OIDC middleware, StubAuth; added cookie-only auth + redirect handlers |
| `appsettings.json` | Removed Entra vars, added `FIP:LoginUrl` |
| `Data/FirmDbContext.cs` | Changed DataProtectionKeys table from `firm_data_protection_keys` → `DataProtectionKeys` |
| `Data/DatabaseInitializationService.cs` | Removed `firm_data_protection_keys` from `extraTables` |
| `Components/Pages/Login.razor` | Replaced OIDC sign-in with "Sign in with Fortress AI" link to FAIT |
| `FortressIntelligenceRM.Web.csproj` | Removed OpenIdConnect NuGet package reference |
