# Review Report — ADO#2842
## FAIT v2: Blazor Server App Shell

**Reviewer:** Hawkeye (Clint Barton)
**Cycle:** 1 of 2
**Commit:** `598ee54`
**CC invocation:** `cat review-brief-ado2842.md | claude --model sonnet --print --dangerously-skip-permissions`

---

### Verdict: NEEDS-CHANGES

Two issues found. Neither is a logic bomb, but both are real gaps that must be fixed before this ships.

---

### Spec Compliance Check

**§2 Codebase Map — Files Created:**
- `Dockerfile.debian` ✅
- `FortressAI.V2.Web.csproj` ✅
- `Program.cs` ✅
- `appsettings.json` ✅
- `Theme/FipTheme.cs` ✅
- `Components/App.razor` ✅
- `Components/Routes.razor` ✅
- `Components/_Imports.razor` ✅
- `Components/RedirectToLogin.razor` ✅
- `Components/Layout/MainLayout.razor` ✅
- `Components/Pages/Dashboard.razor` ✅
- `Components/Pages/Onboarding.razor` ✅
- `Components/Pages/Memory.razor` ✅
- `Components/Pages/Tasks.razor` ✅
- `Components/Pages/Workspace.razor` ✅
- `Components/Pages/Connectors.razor` ✅

**§6 Out of Scope:** ✅ No out-of-scope changes detected.

**§7 Acceptance Criteria:**
- [x] Blazor Server scaffold in `fait-v2/` under monorepo ✅
- [x] Entra SSO — `Microsoft.Identity.Web`, `AddMicrosoftIdentityWebApp`, `.FortressAI.Session` cookie ✅
- [x] FIP waffle nav — `FipNavBar` from `FipShared.Components`, `FipModule.FAIT` active ✅
- [x] MudBlazor v7 theme — `FipTheme.Create()`, dark/light toggle, brand colors ✅
- [x] All 6 route stubs present ✅
- [x] Auth guard — `RequireAuthorization()` on all Blazor routes; `RedirectToLogin.razor` ✅ (circuit-level guard present; see I1 below for page-level gap)
- [x] `Dockerfile.debian` — correct debian base images ✅
- [x] `dotnet build` SUCCEEDED ✅
- [x] `appsettings.json` — TenantId correct, `GuidFormat=None` present ✅

**Spec compliance verdict:** ✅ COMPLIANT (issues are implementation quality, not spec non-compliance)

---

### Consistency Audit

**Files Cross-Referenced:**
- `Program.cs` auth registration ↔ `Routes.razor` AuthorizeRouteView ↔ page `[Authorize]` attributes — ⚠️ Onboarding.razor missing page-level attribute (see I1)
- `Program.cs` cookie name `.FortressAI.Session` — ✅ Matches expected shared FIP cookie
- `appsettings.json` TenantId ↔ checklist spec — ✅ `7152ea12-c930-44b0-bb52-069152161c5b` exact match
- `Dockerfile.debian` base images ↔ checklist — ✅ Both stages on `8.0-bookworm-slim`
- `Program.cs` security headers order ↔ `UseStaticFiles` position — ❌ Headers come after static files (see I2)
- `MainLayout.razor` FipNavBar ↔ FipShared.Components — ✅ `FipModule.FAIT`, correct enum value
- `MudThemeProvider @bind-IsDarkMode` ↔ `_isDarkMode` field ↔ `ToggleDarkMode()` method ↔ localStorage key `"fait-v2-dark-mode"` — ✅ All consistent

**Undocumented Dependencies:** None found.

---

### Critical Issues: 0

No critical (FAIL-blocking) issues found.

---

### Important Issues: 2

#### I1: `Onboarding.razor` Missing `@attribute [Authorize]`
- **File:** `Components/Pages/Onboarding.razor` (line 1 — only `@page "/onboarding"`, no authorize attribute)
- **Category:** correctness / defense-in-depth
- **Issue:** Five of six pages have `@attribute [Authorize]`. Onboarding is the only one without it. The circuit-level FallbackPolicy + `.RequireAuthorization()` on `MapRazorComponents` means unauthenticated users cannot reach this page in normal operation — but the per-page `[Authorize]` attribute is the standard defense-in-depth layer that makes auth intent explicit and provides protection if the circuit-level policy is ever changed.
- **Impact:** Not a live bypass (circuit-level auth catches it), but a code consistency problem that could become a real gap if someone refactors the middleware. Also fails consistency audit — all sibling pages have it.
- **Fix:**
  ```diff
  @page "/onboarding"
  + @attribute [Authorize]
  
  <PageTitle>Onboarding — FAIT v2</PageTitle>
  ```

#### I2: Security Headers Middleware Registered After `UseStaticFiles()`
- **File:** `Program.cs` (line 69: `app.UseStaticFiles()`, line 72: security headers middleware)
- **Category:** security
- **Issue:** ASP.NET Core middleware runs in pipeline order. `UseStaticFiles()` short-circuits — it serves the file and returns, never reaching downstream middleware. All static file responses (the MudBlazor CSS, MudBlazor JS, `app.css`, Inter font CSS, and any wwwroot files) are served without `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, or `Referrer-Policy`.
- **Impact:** Primarily affects `X-Content-Type-Options: nosniff` on script/stylesheet responses, which has real MIME-sniffing protection value. `X-Frame-Options` on JS is low practical risk. But the pattern is wrong and this is a known anti-pattern (documented in MEMORY.md from NEXUS Phase 2 review).
- **Fix:** Swap the order — move security headers middleware to before `app.UseStaticFiles()`:
  ```diff
  + // Security headers — must be before UseStaticFiles
  + app.Use(async (context, next) =>
  + {
  +     context.Response.Headers["X-Content-Type-Options"] = "nosniff";
  +     context.Response.Headers["X-Frame-Options"] = "DENY";
  +     context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
  +     context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
  +     await next();
  + });
  
  app.UseStaticFiles();
  
  - // Security headers
  - app.Use(async (context, next) =>
  - {
  -     context.Response.Headers["X-Content-Type-Options"] = "nosniff";
  -     context.Response.Headers["X-Frame-Options"] = "DENY";
  -     context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
  -     context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
  -     await next();
  - });
  ```

---

### Nitpicks: 2

- **N1:** `/onboarding` not present in `MudNavMenu` in `MainLayout.razor` — 5 nav links, all other pages included. If onboarding is an intentional "entry flow" that shouldn't be in persistent nav, this is fine. Confirm against spec intent.
- **N2:** `Microsoft.Identity.Web` and `Microsoft.Identity.Web.UI` pinned to `"3.*"` (floating major wildcard). Consider locking to `"3.3.*"` before production to prevent surprise breaking changes on a security-sensitive auth library.

---

### Positive Observations

Clean build. Everything that passes is solid:
- Entra SSO wired correctly, matches nexus pattern
- Cookie configuration is exact to spec (name, SameSite, SecurePolicy)
- TenantId verified, ClientId clearly PLACEHOLDER
- `GuidFormat=None` in connection string
- `Dockerfile.debian` uses correct MCR debian base, build and runtime stages separated properly, no spurious standard Dockerfile
- ForwardedHeaders for ALB/ECS present and correct
- Health endpoint `/health` is public
- FipNavBar with `FipModule.FAIT` — correct
- MudBlazor fully initialized: `AddMudServices`, `MudThemeProvider`, providers (Popover/Dialog/Snackbar all present)
- Dark/light toggle with localStorage persistence — well implemented
- All 6 routes exist with correct `@page` directives
- Logout uses `forceLoad: true` to `MicrosoftIdentity/Account/SignOut` — correct server-side signout
- No Cognito references anywhere in source

---

### What to Fix (2 items — straightforward)

**Tony — two quick fixes:**

1. **`Components/Pages/Onboarding.razor`** — Add `@attribute [Authorize]` on line 2 (same as all other pages).

2. **`Program.cs`** — Move the security headers `app.Use(...)` block from after `app.UseStaticFiles()` to before it. Swap lines ~69 and ~72-79. The FallbackPolicy and security headers need to appear before static file serving.

That's it. Clean build, solid pattern, two fixable gaps. Return for cycle 2 after both are addressed.

---

## Cycle 2 Review — ADO#2842

**Reviewer:** Hawkeye (Clint Barton)  
**Cycle:** 2 of 2  
**Commit:** `8362cdf`  
**CC invocation:** `cat pipeline/review-c2-brief.md | claude --model sonnet --print --dangerously-skip-permissions`

---

### Verdict: PASS

Both I1 and I2 fixes verified correct. No new issues introduced. No source scope creep.

---

### Fix Verification

#### I1 — `Onboarding.razor` — ✅ FIXED
- `@attribute [Authorize]` present at line 2, directly after `@page "/onboarding"`
- No other changes to this file
- All 6 pages now carry `[Authorize]` (Dashboard ✅ Memory ✅ Tasks ✅ Workspace ✅ Connectors ✅ Onboarding ✅)

#### I2 — `Program.cs` Middleware Order — ✅ FIXED

Confirmed ordering (line numbers):

| Line | Middleware |
|------|------------|
| 70 | `app.Use(...)` — security headers START |
| 77 | `});` — security headers END |
| 79 | `app.UseStaticFiles()` |
| 81 | `app.UseRouting()` |
| 82 | `app.UseAuthentication()` |
| 83 | `app.UseAuthorization()` |
| 84 | `app.UseAntiforgery()` |
| 87 | `/health` — `.AllowAnonymous()` |
| 93 | `app.MapRazorComponents<App>()` |

Security headers block is before `UseStaticFiles`. Static file responses (MudBlazor CSS/JS, wwwroot) now receive `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `Referrer-Policy`. `await next()` is correctly called — no request blocking.

---

### Scope Creep — ✅ CLEAN (source files only)

Commit touched 5 files total. The 3 beyond the expected 2 are pipeline artifacts under `pipeline/` (build report, review report, C2 brief) — not source code. No functional scope creep.

---

### New Issues — None

No issues introduced by the cycle 2 changes. Belt-and-suspenders auth pattern intact (`FallbackPolicy = DefaultPolicy` + `.RequireAuthorization()` on `MapRazorComponents` + per-page `[Authorize]`). `/health` correctly anonymous.

---

### Final Verdict: PASS — Ready to ship.
