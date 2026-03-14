# Build Report: FIP Phase 2 — FIP Portal

**Task:** FIP-PHASE2  
**Agent:** Tony Stark (software-engineer)  
**Date:** 2026-03-14  
**Commit SHA:** `f9feef9` (portal app) + `8c3dcb2` (Dockerfile.debian)  

---

## Status: ✅ BUILD SUCCEEDED

- `dotnet build`: **0 errors, 0 warnings**
- `docker build`: **SUCCESS** — image `fip-portal-test:latest` (340 MB)
- Monorepo: pushed to `github.com:fwhite13/fortress-intelligence-platform.git` @ `main`

---

## Files Created

```
fip/
├── Dockerfile                         # MCR-based (for AWS CodeBuild)
├── Dockerfile.debian                  # MCR-free WSL2 build (mirrors FIRM pattern)
├── .dockerignore
└── src/
    └── FortressIntelligencePlatform.Web/
        ├── FortressIntelligencePlatform.Web.csproj
        ├── Program.cs
        ├── Components/
        │   ├── _Imports.razor         # Global Blazor using directives
        │   ├── App.razor              # Root Blazor document
        │   ├── Routes.razor           # Router with AuthorizeRouteView
        │   ├── RedirectToLogin.razor  # OIDC redirect helper
        │   ├── Layout/
        │   │   └── MainLayout.razor   # Minimal pass-through layout
        │   └── Pages/
        │       └── Home.razor         # App-switcher landing [Authorize]
        ├── Data/
        │   └── SharedKeyRingDbContext.cs  # IDataProtectionKeyContext for shared key ring
        └── wwwroot/
            └── app.css                # Dark gold theme, app-grid tiles
```

---

## `dotnet build` Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:01.40
```

**Fix applied:** The spec's `Program.cs` used fully-qualified `Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersOptions` — added `using Microsoft.AspNetCore.HttpOverrides;` at top of file and simplified inline references. Logically identical; required to compile.

---

## `docker build` Result

```
Successfully built fip-portal-test:latest
Image ID: 62221137fa87
Size: 340 MB (95.6 MB on disk)
```

**Note on Dockerfile:** MCR (`mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim`) is unreachable from the WSL2 Docker daemon (known DNS/TLS EOF issue; curl reaches MCR fine). Built and verified using `Dockerfile.debian` — same pattern as FIRM. The MCR-based `Dockerfile` is the production artifact for AWS CodeBuild (which has MCR cached). Both are committed.

---

## Monorepo Commit

```
f9feef9  feat: FIP portal — new ASP.NET 8 app, Entra OIDC, app-switcher landing page, shared key ring
8c3dcb2  chore: add Dockerfile.debian for local WSL2 builds (MCR-free, mirrors FIRM pattern)
```

Pushed to: `github.com:fwhite13/fortress-intelligence-platform.git` @ `main`

---

## Key Design Decisions

### 1. No MudBlazor
The FIP portal is an app-switcher, not a full application UI. Zero component library dependencies keeps the image small (340 MB vs ~420+ MB with MudBlazor) and eliminates a major transitive dependency surface.

### 2. DB-Backed Key Ring (`SharedKeyRingDbContext`)
FIP portal is the **sole key generator** for the FortressAI key ring. `PersistKeysToDbContext<SharedKeyRingDbContext>()` writes to `fred_dev.DataProtectionKeys`. FAIT, FIRM, and FORMS will consume keys in Phase 3 via `DisableAutomaticKeyGeneration()` + same `SharedKeyRingDbContext` pointing at the same DB table. `SetApplicationName("FortressAI")` ensures all apps share the same key ring namespace.

### 3. `/auth/firm-callback` Endpoint
Cross-app auth entry point for FIRM/FORMS → FIP login flow. Validates `returnUrl` against `.fortressam.ai` domain (HTTPS only) before redirecting. Prevents open redirect.

### 4. `/auth/logout` Endpoint
Dual sign-out: clears both cookie scheme and OIDC session (back-channel signout to Entra). Anonymous to allow unauthenticated users to hit it safely.

### 5. HTTPS Redirect Enforcement in OIDC
`OnRedirectToIdentityProvider` rewrites `http://` → `https://` in the redirect URI. Required when running behind ALB with TLS termination (ASP.NET sees `http://` internally but Entra callback must be `https://`).

### 6. Domain-Scoped Cookie
`.Cookie.Domain = Auth__CookieDomain` (expected value: `.dev.fortressam.ai`) enables SSO across FAIT, FIRM, FORMS subdomains after a single FIP login. `SameSite=Lax` + `SecurePolicy=Always` + `IsEssential=true`.

### 7. ForwardedHeaders Middleware
Trusts all proxies/networks (`KnownNetworks.Clear()` + `KnownProxies.Clear()`) for ALB-forwarded `X-Forwarded-For` and `X-Forwarded-Proto`. Applied before auth middleware.

### 8. `_Imports.razor` Added
Spec did not include this file but it's required for Blazor 8 to resolve `Router`, `AuthorizeRouteView`, `PageTitle`, `FocusOnNavigate`, `HeadOutlet` etc. Added standard Blazor 8 imports.

---

## DevOps WIs Updated

| WI | Status | Comment |
|----|--------|---------|
| #668 | ✅ Done | FIP portal app complete. Commit: f9feef9. Docker build: SUCCESS. |
| #669 | ✅ Done | Commit: f9feef9 |
| #670 | ✅ Done | Commit: f9feef9 |
| #671 | ✅ Done | Commit: f9feef9 |
| #672 | ✅ Done | Commit: f9feef9 |
| #673 | ✅ Done | Commit: f9feef9 |
| #674 | ✅ Done | Commit: f9feef9 |
| #675 | ✅ Done | Commit: f9feef9 |
| #676 | ✅ Done | Commit: f9feef9 |

---

## Self-Review Checklist

- [x] All files from spec created
- [x] `dotnet build` — 0 errors, 0 warnings
- [x] `docker build` — SUCCESS
- [x] Monorepo committed and pushed
- [x] `[Authorize]` on Home.razor (app-switcher)
- [x] Shared key ring: `SharedKeyRingDbContext` implements `IDataProtectionKeyContext`
- [x] `SetApplicationName("FortressAI")` — consistent with FIP suite key ring name
- [x] `/health` endpoint is `.AllowAnonymous()`
- [x] `/auth/logout` is `.AllowAnonymous()`
- [x] `/auth/firm-callback` validates returnUrl domain before redirect (no open redirect)
- [x] Cookie domain scoped to `.dev.fortressam.ai` via config
- [x] HTTPS redirect in OIDC `OnRedirectToIdentityProvider`
- [x] ForwardedHeaders configured for ALB TLS termination
- [x] No MudBlazor added
- [x] DevOps WIs #668–676 marked Done
