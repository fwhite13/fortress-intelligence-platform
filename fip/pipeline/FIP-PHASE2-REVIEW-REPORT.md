# FIP Portal — Phase 2 Review Report

**Reviewer:** Hawkeye (Clint Barton)
**Repo:** `~/projects/fip/fip/` — commit `f9feef9`
**Review Cycle:** 1 of 2
**Date:** 2026-03-14

---

## Verdict: ✅ PASS

All 25 checklist items verified. Two minor observations noted (non-blocking). No critical issues. No security blockers.

---

## Checklist Results

### Authentication + Cookie (items 1–8)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 1 | `DefaultScheme = Cookie`, `DefaultChallengeScheme = OIDC` | ✅ PASS | Exact. Lines 44–47. |
| 2 | Cookie name `.FortressAI.Session` | ✅ PASS | Exact match. Line 51. |
| 3 | `Cookie.Domain` from `Auth__CookieDomain` config — not hardcoded | ✅ PASS | `builder.Configuration["Auth__CookieDomain"]`. Line 52. |
| 4 | `SameSite = Lax`, `SecurePolicy = Always`, `IsEssential = true` | ✅ PASS | All three. Lines 53–55. |
| 5 | `ExpireTimeSpan = 12h`, `SlidingExpiration = true` | ✅ PASS | Lines 56–57. |
| 6 | `OnTokenValidated`: `IsPersistent = true` AND `ExpiresUtc = UtcNow + 12h` | ✅ PASS | Both set. Lines 86–87. |
| 7 | `OnRedirectToIdentityProvider`: http→https rewrite | ✅ PASS | String replace present. Lines 71–74. |
| 8 | `MapInboundClaims = false` | ✅ PASS | Line 64. |

### Data Protection Key Ring (items 9–11)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 9 | `SharedKeyRingDbContext` implements `IDataProtectionKeyContext`, has `DbSet<DataProtectionKey>` | ✅ PASS | Clean implementation. Correct interface, correct property, explicit `ToTable("DataProtectionKeys")`. |
| 10 | `AddDataProtection()` → `.PersistKeysToDbContext<SharedKeyRingDbContext>()` | ✅ PASS | Correct context. Line 39. |
| 11 | **`SetApplicationName("FortressAI")`** — CRITICAL | ✅ PASS | **Exact string confirmed.** Line 40. No deviation. Phase 3 cookie sharing will work. |

### DB Connection (items 12–13)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 12 | `MySqlConnectionStringBuilder` (not raw interpolation) | ✅ PASS | Builder pattern used. Handles `=` in passwords correctly. Lines 22–31. |
| 13 | `ServerVersion.AutoDetect()` — no duplicate connection | ✅ PASS | Passed the already-built connection string — no second connection opened. Line 35. |

### Endpoints (items 14–17)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 14 | `/health` returns `{ status: "healthy", service: "fip" }`, `.AllowAnonymous()` | ✅ PASS | Exact JSON shape. Anonymous. Lines 100–101. |
| 15 | `/auth/logout` signs out BOTH Cookie AND OIDC, `.AllowAnonymous()` | ✅ PASS | Both schemes. Anonymous. Lines 104–108. |
| 16 | `/auth/firm-callback`: validates scheme == `https` AND host ends with `.fortressam.ai` | ✅ PASS | Both checks present. `OrdinalIgnoreCase`. `.RequireAuthorization()`. Lines 112–122. |
| 17 | No unauthenticated endpoint leaks sensitive data | ✅ PASS | Only `/health` and `/auth/logout` are anonymous. Health returns a static object. No data leaks. |

### Middleware Order (items 18–19)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 18 | `UseForwardedHeaders()` BEFORE `UseAuthentication()` and `UseAuthorization()` | ✅ PASS | `UseForwardedHeaders()` is first in pipeline, lines 95–100. |
| 19 | Full middleware sequence present and correct | ✅ PASS | `UseStaticFiles` → `UseRouting` → `UseAuthentication` → `UseAuthorization` → `UseAntiforgery`. Lines 101–105 (approx). Correct order. |

### Blazor Structure (items 20–22)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 20 | `_Imports.razor` exists with necessary using directives | ✅ PASS | Present. Includes `Authorization`, `Components.Authorization`, `Forms`, `Routing`, `Web`, `JSInterop`, project namespaces. |
| 21 | `App.razor` has `Router` with `AuthorizeRouteView` | ✅ PASS | Router is in `Routes.razor`, referenced from `App.razor` via `<Routes />`. `AuthorizeRouteView` is in `Routes.razor` with `NotAuthorized` handler and `RedirectToLogin`. This is valid Blazor 8 structure. |
| 22 | `Home.razor` has `@attribute [Authorize]` | ✅ PASS | Line 2 of Home.razor. |

### Dockerfile (items 23–24)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 23 | Production `Dockerfile` uses `mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim` | ✅ PASS | Exact image, base stage. NOT alpine. |
| 24 | Multi-stage: `base` → `build` → `publish` → `final`, `EXPOSE 80` | ✅ PASS | All four stages. `EXPOSE 80` on base. |

### Security (item 25)

| # | Item | Result | Notes |
|---|------|--------|-------|
| 25 | No hardcoded secrets, credentials, or tenant IDs | ✅ PASS | All sensitive values (`TenantId`, `ClientId`, `ClientSecret`, `FORTRESS_DB_PASS`, etc.) read from `builder.Configuration`. No appsettings.json files committed. No GUIDs or actual credentials in source. |

---

## Focus Item Verification

### ⚡ #11 — `SetApplicationName("FortressAI")` — CRITICAL
**VERIFIED EXACT.** `Program.cs` line 40:
```csharp
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI");
```
String is `"FortressAI"` — no trailing space, no variant casing, no typo. Phase 3 cross-app cookie sharing will work correctly.

### ⚡ #16 — `/auth/firm-callback` Open Redirect Protection
**BOTH CHECKS PRESENT.** `Program.cs` lines 116–119:
```csharp
Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) &&
uri.Scheme == Uri.UriSchemeHttps &&
uri.Host.EndsWith(".fortressam.ai", StringComparison.OrdinalIgnoreCase)
```
Scheme check: ✅ `Uri.UriSchemeHttps` constant (not string comparison).
Domain check: ✅ `.fortressam.ai` with `OrdinalIgnoreCase`. No open redirect vulnerability.

### ⚡ #6 — `IsPersistent = true` in `OnTokenValidated`
**CONFIRMED PRESENT.** `Program.cs` lines 86–87:
```csharp
ctx.Properties!.IsPersistent = true;
ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12);
```
Both lines present. Cookies will persist for FIRM/FORMS to read.

---

## Observations (Non-Blocking)

### OBS-1: `Dockerfile.debian` — `EXPOSE 8080` mismatch
`Dockerfile.debian` exposes port **8080** and sets `ASPNETCORE_URLS=http://+:8080`, while the production `Dockerfile` exposes port **80**. This is intentional per the file header ("for local WSL2 builds only") and is not a bug — but it's worth making sure the team knows the local Dockerfile is **not** a drop-in for production. A comment in the file already calls this out. No action required.

### OBS-2: `Home.razor` — App tile URLs not validated
App tile `href` values (`Apps__FaitUrl`, `Apps__FirmUrl`, `Apps__FormsUrl`) come from configuration and are rendered directly. If misconfigured, a user could be redirected off-domain. This is an internal configuration concern — not an exploitable XSS vector since Blazor renders these as `href` attributes on `<a>` tags, not as script. Low-priority for a Phase 2 review; worth revisiting if these URLs ever come from user input. No action required now.

---

## Summary

| Category | Pass | Issues |
|----------|------|--------|
| Auth + Cookie | 8/8 | 0 |
| Data Protection | 3/3 | 0 |
| DB Connection | 2/2 | 0 |
| Endpoints | 4/4 | 0 |
| Middleware | 2/2 | 0 |
| Blazor Structure | 3/3 | 0 |
| Dockerfile | 2/2 | 0 |
| Security | 1/1 | 0 |
| **Total** | **25/25** | **0 blocking** |

**Critical items all verified:**
- ✅ `SetApplicationName("FortressAI")` — exact
- ✅ `/auth/firm-callback` — both scheme and domain validated
- ✅ `IsPersistent = true` — present

This build is clean. Ready to advance to SECURITY stage.

---

*— Hawkeye (Clint Barton), Review Cycle 1 of 2*
