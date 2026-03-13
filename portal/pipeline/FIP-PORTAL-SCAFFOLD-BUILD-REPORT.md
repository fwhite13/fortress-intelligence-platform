# Build Report: FIP Portal Scaffold

**Task:** FIP-PORTAL-SCAFFOLD
**Agent:** Tony Stark (software-engineer)
**Date:** 2026-03-13
**Status:** ✅ BUILD PASSED — 0 Error(s)

---

## Summary

FIP Portal scaffold built successfully. App switcher landing page with 4 app tiles (FAIT, FIRM, FORMS, FORGE), dark navy theme, MudBlazor UI, and Cognito auth stub (`UseStubAuth=true` by default). Ready for Cognito wiring in weekend sprint.

---

## Build Details

| Item | Value |
|------|-------|
| **Build result** | ✅ Build succeeded — 0 Error(s), 0 Warning(s) |
| **Framework** | .NET 8.0 Blazor Server |
| **MudBlazor version** | `7.16.0` (resolved from `7.*`) |
| **Dockerfile EXPOSE** | `8080` |
| **docker-compose port** | `3334:8080` |
| **ECR repo** | `742932328420.dkr.ecr.us-east-1.amazonaws.com/fortress-tools-portal` |
| **ECS service** | `fortress-portal-dev` on `fortress-tools-cluster` |
| **Image tag** | `portal-latest` |
| **Git push** | ❌ Local-only — `fwhite13/fortress-portal` repo does not exist yet on GitHub. Maria to create repo and push. |
| **Commit SHA** | `0d12374` |

---

## Files Created

```
~/projects/fip/portal/
├── .gitignore
├── buildspec.yml                          ← CodeBuild pipeline (cloned from FORMS, updated for portal)
├── docker-compose.yml                     ← Port 3334:8080 for local testing
└── src/
    └── FortressPortal.Web/
        ├── FortressPortal.Web.csproj      ← MudBlazor 7.*, OpenIdConnect 8.0.*
        ├── Program.cs                     ← Cognito OIDC + UseStubAuth stub
        ├── appsettings.json               ← UseStubAuth: true
        ├── Dockerfile                     ← EXPOSE 8080, single-project build
        ├── Components/
        │   ├── App.razor
        │   ├── Routes.razor
        │   ├── RedirectToLogin.razor
        │   ├── _Imports.razor
        │   ├── Layout/
        │   │   ├── MainLayout.razor       ← Dark navy, gold header, Fortress shield icon
        │   │   └── NavMenu.razor          ← Minimal top bar with sign-out button
        │   └── Pages/
        │       ├── Index.razor            ← App switcher grid (4 tiles, [AllowAnonymous])
        │       └── Login.razor            ← Cognito stub — redirects to / in stub mode
        └── wwwroot/
            ├── favicon.svg
            └── css/
                └── portal.css            ← Dark navy theme, gold accent, responsive grid
```

---

## App Tiles

| Tile | Icon | URL | State |
|------|------|-----|-------|
| FAIT — Fortress AI | `SmartToy` | `https://fait.dev.fortressam.ai` | ✅ Active |
| FIRM — Meeting Intelligence | `VideoCall` | `https://meetings.dev.fortressam.ai` | ✅ Active |
| FORMS — Form Intelligence | `Assignment` | `https://forms.dev.fortressam.ai` | ✅ Active |
| FORGE — Intelligence Forge | `Construction` | `#` (disabled) | 🔒 Coming Soon |

---

## Auth Stub

- `UseStubAuth=true` in `appsettings.json` — auth is bypassed entirely for scaffold testing
- `Index.razor` uses `[AllowAnonymous]` — renders without challenge when stub is active
- `Program.cs` has full Cognito OIDC block ready: reads `Cognito__Authority`, `Cognito__ClientId`, `Cognito__ClientSecret` from config
- Switching to real auth: set `UseStubAuth=false` (or remove the key) + populate Cognito config values

---

## Build Command Output

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.88
```

---

## Git Status

```
Branch: main
Commit: 0d12374 feat: FIP Portal scaffold — app switcher grid, Cognito auth stub
Remote: git@github.com:fwhite13/fortress-portal.git (NOT YET CREATED)
Push status: Local-only — repo creation and push pending (Maria to handle)
```

---

## Notes for Weekend Sprint

1. Create `fwhite13/fortress-portal` GitHub repo and push this commit
2. Set `UseStubAuth=false` in ECS task definition environment variables
3. Populate `Cognito__Authority`, `Cognito__ClientId`, `Cognito__ClientSecret` in ECS secrets
4. Run `scripts/fip-deploy.sh portal` (or push to CodeBuild) to deploy to `fortress-portal-dev`
5. Route `fip.dev.fortressam.ai` → ALB → `fortress-portal-dev` ECS service
