# Natasha QA Auth — Definitive Fix Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-20  
**Status:** Ready for implementation  
**Audience:** Tony (build), Natasha (usage), Clint (review)  
**Affected apps:** FAM OS, FAIT (partial — see §2), FIRM, FORMS, Cowork  

---

## 1. Root Cause Diagnosis

### Why the Header Bypass Fails on Some Routes

FAM OS's existing QA bypass is middleware that sets `context.User` after `UseAuthorization()`:

```csharp
app.UseAuthentication();
app.UseAuthorization();     // ← auth policy evaluated HERE
// ... then later:
app.Use(async (context, next) => {
    if (context.Request.Headers["X-QA-Bypass"] == "natasha-qa-token-famos-dev")
        context.User = new ClaimsPrincipal(...);   // ← TOO LATE for this request
    await next();
});
```

**This has two failure modes:**

**Failure Mode A — HTTP routes:** The bypass middleware runs after `UseAuthorization()`. For standard controller actions and minimal API routes, ASP.NET Core evaluates the `[Authorize]` attribute during the authorization middleware pass. By the time the bypass middleware sets `context.User`, the 302 redirect to login has already been decided. The next middleware sees the new user, but the response has already been queued for redirect. On some routes this happens to "work" because the route's response doesn't enforce auth at the framework level (e.g., anonymous endpoints that just check `User.Identity.IsAuthenticated` in code). On others — specifically `[Authorize]`-decorated routes — it fails every time.

**Failure Mode B — Blazor interactive pages (the new `/accounts` failure):** Blazor Server components that use `@attribute [Authorize]` are evaluated at WebSocket circuit upgrade time. When the browser opens the SignalR connection for `/_blazor`, ASP.NET Core checks authentication state from the **HTTP cookie** at that moment. The header bypass cannot inject a user identity into a WebSocket handshake — there are no HTTP headers on a WebSocket frame after the initial handshake. The circuit's `AuthenticationStateProvider` is captured at upgrade, not re-evaluated per-component render. Even if the bypass middleware runs for the initial page load, the interactive circuit starts fresh with the cookie-based identity. If there's no valid session cookie, `[Authorize]` on the Blazor component kicks out to the login redirect.

**Summary:** Setting `context.User` in middleware after `UseAuthorization()` is fundamentally broken for Blazor interactive pages and unreliable for HTTP routes. It will always be a per-route, per-rendermode game of whack-a-mole.

### Why FAIT's TestAuth Works Everywhere

FAIT's `TestAuthController.POST /auth/test-session` writes a real `.FortressAI.Session` cookie via `HttpContext.SignInAsync()`. This is the same mechanism used by the OIDC callback. The cookie travels with every request and every WebSocket upgrade. The auth middleware reads it, reconstructs the `ClaimsPrincipal`, and populates `context.User` **before** authorization runs. Blazor's `AuthenticationStateProvider` reads the same cookie-backed identity. There is no bypass layer — just a real session established via a different path.

This is why FAIT's bypass works on 100% of routes and FAM OS's doesn't.

---

## 2. Recommended Approach: Option C (Test Session Endpoint) for All Apps

**Recommendation: Port FAIT's `TestAuthController` pattern to every FIP app that doesn't have it.**

This is not a new approach — it is a validated, already-deployed pattern. FAIT has been running it in dev since it was specced in `FAIT-TEST-AUTH-SPEC.md`. The work here is three things:
1. Add the endpoint to FAM OS, FIRM, FORMS, and Cowork (FAIT already has it)
2. Remove FAM OS's broken header bypass middleware
3. Update Natasha's QA runner to use the cookie-based approach

**Why not Option A (fix the header bypass)?**  
The header bypass is architecturally broken for Blazor interactive. There is no middleware insertion point that runs before Blazor's WebSocket auth check AND is per-request injectable via a header. The only correct fix is Option C.

**Why not Option B (Entra service account)?**  
A non-MFA Entra service account is a valid fallback but introduces ongoing maintenance: the account needs a stable password, conditional access must be carefully scoped to exclude it (security risk), and session duration depends on Entra token lifetimes (typically 1 hour for access tokens, up to 90 days for refresh — but Natasha needs to handle token refresh). The test-session endpoint approach is simpler, faster to implement, and easier to revoke.

**Why not a hybrid?**  
The test-session endpoint gives Natasha exactly what she needs. A hybrid adds complexity without adding reliability.

---

## 3. Security Model

The test-session endpoint is secure if and only if:

| Constraint | How Enforced |
|-----------|-------------|
| Dev/staging only — absent in prod | Service registered only `if (builder.Environment.IsDevelopment())` OR `ASPNETCORE_ENVIRONMENT != Production`. `TestAuthService` returns `NotFound()` when not registered. |
| Secret required | `TestAuth:Secret` env var validated on every call. No secret → endpoint returns 401. |
| `TestAuth:Secret` never in prod secrets | Tony: never add `TestAuth__Secret` to prod ECS task definitions, prod SSM parameters, or prod Secrets Manager entries. Enforced by Clint. |
| Rate limited | Fixed-window rate limiter: 10 requests per 5 minutes per IP. Prevents automated brute-force of the secret. |
| Sessions expire | `IsPersistent = false`, `ExpiresUtc = UtcNow + 8 hours`. Session dies at end of QA run or within 8 hours. |
| Claims are safe | The test principal has the same claim shape as a real Entra user. No admin claims, no elevated permissions beyond what the QA user needs. |
| Logged | Every test session creation is logged at Info level with UserId and IP. Audit trail exists. |

**Single secret, per-app:** Each app has its own `TestAuth:Secret`. They can be the same value (simpler for Natasha) or different values (tighter isolation). The spec below uses a single shared secret pattern for simplicity.

---

## 4. Implementation Spec

### 4.1 Shared Pattern — Apply to Each App

The following is a template. All five apps implement the same pattern. App-specific notes in §4.2.

#### `Services/TestAuthService.cs` — CREATE (in each app that doesn't have it)

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace {AppNamespace}.Services;

/// <summary>
/// Dev/staging only. Validates the test auth secret and builds a test ClaimsPrincipal
/// with the same claim shape as a real Entra-authenticated user.
/// </summary>
public class TestAuthService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TestAuthService> _logger;

    public TestAuthService(IConfiguration config, ILogger<TestAuthService> logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>Returns true iff the secret matches TestAuth:Secret config value.</summary>
    public bool ValidateSecret(string secret)
    {
        var expected = _config["TestAuth:Secret"];
        if (string.IsNullOrEmpty(expected)) return false;
        return string.Equals(secret, expected, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds a ClaimsPrincipal with the same claim types used by Entra OIDC.
    /// UserId should be an email address (e.g., "qa@fortressam.ai").
    /// </summary>
    public ClaimsPrincipal BuildTestPrincipal(string userId, string displayName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier,  userId),
            new(ClaimTypes.Name,            displayName),
            new(ClaimTypes.Email,           userId),
            new("preferred_username",       userId),
            new("email",                    userId),
            new("name",                     displayName),
            // Fixed OID for QA user — deterministic so DB records are predictable
            new("http://schemas.microsoft.com/identity/claims/objectidentifier",
                "00000000-0000-0000-0000-000000000099"),
            new("oid",
                "00000000-0000-0000-0000-000000000099"),
            new("tid", _config["AzureAd:TenantId"] ?? "test-tenant"),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
```

#### `Controllers/TestAuthController.cs` — CREATE (in each app that doesn't have it)

```csharp
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using {AppNamespace}.Services;

namespace {AppNamespace}.Controllers;

[ApiController]
[Route("auth")]
[AllowAnonymous]
public class TestAuthController : ControllerBase
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TestAuthController> _logger;

    public TestAuthController(IServiceProvider services, ILogger<TestAuthController> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// POST /auth/test-session
    /// Creates a real .FortressAI.Session cookie for the specified test user.
    /// Returns 404 when TestAuthService is not registered (i.e., in Production).
    /// </summary>
    [EnableRateLimiting("test-auth")]
    [HttpPost("test-session")]
    public async Task<IActionResult> CreateTestSession([FromBody] TestSessionRequest request)
    {
        var testAuth = _services.GetService<TestAuthService>();
        if (testAuth == null)
            return NotFound();   // Not registered in Production — safe 404

        if (!testAuth.ValidateSecret(request.Secret))
        {
            _logger.LogWarning("TestAuth: invalid secret from {IP}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid secret" });
        }

        var principal = testAuth.BuildTestPrincipal(
            request.UserId    ?? "qa@fortressam.ai",
            request.DisplayName ?? "QA Tester");

        _logger.LogInformation("TestAuth: session created for {UserId} from {IP}",
            request.UserId, HttpContext.Connection.RemoteIpAddress);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });

        return Ok(new {
            message    = "Test session created",
            userId     = request.UserId,
            expiresIn  = "8 hours"
        });
    }
}

public record TestSessionRequest(
    string Secret,
    string? UserId,
    string? DisplayName);
```

#### `Program.cs` changes — MODIFY (in each app)

Add in the **services section** (`builder.Services.*`):

```csharp
// ⚠️ TEST AUTH — DEVELOPMENT / STAGING ONLY — MUST NOT REACH PRODUCTION
// Register TestAuthService only when environment is not Production.
// The controller returns 404 when this service is not registered.
if (!builder.Environment.IsProduction())
{
    builder.Services.AddSingleton<TestAuthService>();
    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("test-auth", policy =>
        {
            policy.PermitLimit   = 10;
            policy.Window        = TimeSpan.FromMinutes(5);
            policy.QueueLimit    = 0;
        });
    });
}
```

Add in the **middleware section** after `UseRouting()`:

```csharp
// Enable rate limiter for test-auth (dev/staging only)
if (!app.Environment.IsProduction())
{
    app.UseRateLimiter();
}
```

**And add `app.MapControllers()` if not already present** (required to activate `TestAuthController`):

```csharp
app.MapControllers();
```

> **Note:** If the app doesn't currently have `builder.Services.AddControllers()` in the service registration, also add:
> ```csharp
> builder.Services.AddControllers();
> ```
> FAM OS and FORMS may not have this — check before assuming.

#### Remove the broken header bypass (FAM OS only)

In `~/projects/fip/famos/src/FamOs.Web/Program.cs`, **DELETE** the entire block:

```csharp
// QA bypass — dev/staging only (FAMOS_QA_BYPASS=true env var required)
// MUST be after UseAuthorization() so the bypass identity is not clobbered by the cookie auth check
if (app.Environment.IsDevelopment() ||
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Headers.ContainsKey("X-QA-Bypass") &&
            context.Request.Headers["X-QA-Bypass"] == "natasha-qa-token-famos-dev")
        {
            // ... all claims setup ...
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}
```

Also remove the `/qa/status` diagnostic endpoint (no longer needed):

```csharp
// DELETE THIS:
app.MapGet("/qa/status", () => Results.Ok(new { ... })).AllowAnonymous();
```

And remove the `FAMOS_QA_BYPASS` env var from ECS task definitions (Rhodey).

### 4.2 Per-App Notes

#### FAM OS (`fip/famos/src/FamOs.Web/`)

- **Action:** Add `TestAuthService.cs`, `TestAuthController.cs`; modify `Program.cs`; delete header bypass middleware
- **Has `AddControllers()`?** Check — likely no. Add if missing.
- **Cookie name:** `.FortressAI.Session` (already configured at line 37 of `Program.cs`) ✅
- **Claim used by `UserSessionService`:** `preferred_username` → `email` → `email` → `NameIdentifier` → `sub` → `oid`. The test principal sets all of these to `qa@fortressam.ai`. ✅
- **New env var needed:** `TestAuth__Secret` in dev ECS task definition + local `appsettings.Development.json`

#### FAIT (`fip/fait/src/FortressAI.Web/`)

- **Action:** None — `TestAuthService` and `TestAuthController` already exist and work
- **Verify:** Confirm `TestAuth:Secret` is set in dev environment and Natasha knows the current value
- **Cookie name:** `.FortressAI.Session` ✅

#### FIRM (`fip/firm/src/FortressIntelligenceRM.Web/`)

- **Action:** Add `TestAuthService.cs`, `TestAuthController.cs`; modify `Program.cs`
- **Check `AddControllers()`:** FIRM has controllers (`FirmIntegrationController`); `AddControllers()` is already registered ✅
- **Cookie name:** Verify — should be `.FortressAI.Session` with shared `Auth__CookieDomain`
- **New env var:** `TestAuth__Secret` in dev ECS task definition

#### FORMS (`fip/forms/src/FortressFormTools.Web/`)

- **Action:** Add `TestAuthService.cs`, `TestAuthController.cs`; modify `Program.cs`
- **Check `AddControllers()`:** Verify presence
- **Cookie name:** Verify matches `.FortressAI.Session`
- **New env var:** `TestAuth__Secret` in dev ECS task definition

#### Cowork (`fip/cowork/src/CoworkWeb/`)

- **Action:** Add `TestAuthService.cs`, `TestAuthController.cs`; modify `Program.cs`
- **Cookie name:** Verify — Cowork uses its own OIDC callback; cookie name may differ. Check `Program.cs` cookie options.
- **Claim used:** `CoworkSessionService` reads user identity from `HttpContext.User` — verify claim types match
- **New env var:** `TestAuth__Secret` in dev ECS task definition

### 4.3 Shared Secret Value

Use a single secret value across all apps for Natasha's simplicity. Generate one strong secret:

```
TestAuth:Secret = <generate with: openssl rand -hex 32>
```

Store in:
- Each app's `appsettings.Development.json` (for local dev — NOT committed if file is gitignored, use `secrets.json` or local env vars)
- Each app's dev ECS task definition as `TestAuth__Secret` (double-underscore for ECS env var → config key mapping)
- **NEVER** in prod ECS task definitions, prod SSM, or prod Secrets Manager

Natasha stores this value as `QA_TEST_AUTH_SECRET` in her agent config/environment.

---

## 5. Natasha's New QA Flow

### Setup (once per QA session)

```
Before navigating to any protected page:

1. POST https://{app}.dev.fortressam.ai/auth/test-session
   Body: { "secret": "$QA_TEST_AUTH_SECRET", "userId": "qa@fortressam.ai", "displayName": "QA Tester" }

2. Browser stores the returned .FortressAI.Session cookie.

3. Navigate to any route — auth is transparent.
```

The OpenClaw browser tool handles cookie storage automatically when requests are made within the same browser session. One `POST /auth/test-session` call at the start of a QA run is sufficient for the entire session (8-hour TTL).

### Per-App Auth Setup

```javascript
// Natasha pseudo-code for QA runner initialization:

async function initQaSession(appBaseUrl) {
  await browser.navigate(`${appBaseUrl}/auth/test-session`, { method: 'POST',
    body: { secret: QA_TEST_AUTH_SECRET, userId: 'qa@fortressam.ai', displayName: 'QA Tester' }
  });
  // Cookie is now set. Navigate freely.
}

// Called once per app at the start of each QA run:
await initQaSession('https://famos.dev.fortressam.ai');
await initQaSession('https://fait.dev.fortressam.ai');
await initQaSession('https://firm.dev.fortressam.ai');
// etc.
```

### What Natasha Stops Doing

- No more `X-QA-Bypass` header — remove from all QA runner request headers
- No more per-route workarounds for `/accounts` or other failing routes
- No more escalating to Fred when a new route breaks QA

### What Natasha Should Assert After Auth Setup

After calling `POST /auth/test-session`, Natasha should assert:

```javascript
// Verify session established:
assert(response.status == 200);
assert(response.body.userId == "qa@fortressam.ai");

// Then navigate to a known-protected route:
await browser.navigate(`${appBaseUrl}/`);
assert(currentUrl does NOT contain '/login' or '/auth/');
```

If the test-session call returns 404, the app does not have the test auth endpoint deployed. Escalate — do not attempt workarounds.

---

## 6. File Summary

### FAM OS — 2 new, 1 modified

```
fip/famos/src/FamOs.Web/Services/TestAuthService.cs              ← CREATE
fip/famos/src/FamOs.Web/Controllers/TestAuthController.cs        ← CREATE
fip/famos/src/FamOs.Web/Program.cs                               ← MODIFY (add svc reg, rate limiter, AddControllers if needed; DELETE header bypass block)
```

### FIRM — 2 new, 1 modified

```
fip/firm/src/FortressIntelligenceRM.Web/Services/TestAuthService.cs     ← CREATE
fip/firm/src/FortressIntelligenceRM.Web/Controllers/TestAuthController.cs ← CREATE
fip/firm/src/FortressIntelligenceRM.Web/Program.cs                      ← MODIFY
```

### FORMS — 2 new, 1 modified

```
fip/forms/src/FortressFormTools.Web/Services/TestAuthService.cs         ← CREATE
fip/forms/src/FortressFormTools.Web/Controllers/TestAuthController.cs   ← CREATE
fip/forms/src/FortressFormTools.Web/Program.cs                          ← MODIFY
```

### Cowork — 2 new, 1 modified

```
fip/cowork/src/CoworkWeb/Services/TestAuthService.cs                    ← CREATE
fip/cowork/src/CoworkWeb/Controllers/TestAuthController.cs              ← CREATE
fip/cowork/src/CoworkWeb/Program.cs                                     ← MODIFY
```

### FAIT — no changes (already implemented)

### ECS task definitions (Rhodey)

```
famos-dev task definition:  ADD TestAuth__Secret env var; REMOVE FAMOS_QA_BYPASS env var
firm-dev task definition:   ADD TestAuth__Secret env var
forms-dev task definition:  ADD TestAuth__Secret env var
cowork-dev task definition: ADD TestAuth__Secret env var
```

---

## 7. Clint Review Priorities

```
⚠️  HIGH: Verify `if (!builder.Environment.IsProduction())` is the correct guard.
          All FIP apps use ASPNETCORE_ENVIRONMENT set to either "Development" or
          "Production" in ECS. There is no "Staging" environment. The guard
          `!IsProduction()` covers local dev AND any future staging environment.
          If Tony uses `IsDevelopment()` instead, the endpoint will be absent in
          staging (if staging is ever added). Use `!IsProduction()` consistently.

⚠️  HIGH: FAM OS Program.cs deletion of header bypass block — confirm Tony deletes
          the ENTIRE block including the FAMOS_QA_BYPASS env var check and the
          `/qa/status` endpoint. A partial deletion leaves dead code.

⚠️  HIGH: `app.MapControllers()` must be present for TestAuthController to be
          routed. In apps that have no existing controllers (FAM OS, FORMS?),
          also confirm `builder.Services.AddControllers()` is added. Without
          AddControllers(), MapControllers() silently routes nothing.

⚠️  MEDIUM: Cookie name must match across the test-session write and the app's
            auth read. Verify each app's `options.Cookie.Name` setting. If any
            app uses a non-standard cookie name (not `.FortressAI.Session`),
            the session created by TestAuthController will be stored under the
            wrong name and auth reads will find nothing.

⚠️  MEDIUM: `TestAuth:Secret` must NEVER appear in any prod secrets store,
            prod ECS task definition, or prod CI/CD pipeline.
            Rhodey: when adding the secret to dev ECS task definitions,
            double-check the task definition family name includes "dev"
            (e.g., `famos-dev`, not `famos-prod`).

⚠️  LOW: Rate limiter registration (`AddRateLimiter` / `UseRateLimiter`) must be
         added in the correct order. `AddRateLimiter` in services section.
         `UseRateLimiter` in middleware section BEFORE `UseAuthentication`.
         If already present from other rate limiters in the app, just add the
         "test-auth" policy to the existing `AddRateLimiter` call — don't register
         AddRateLimiter twice.
```

---

## 8. Acceptance Criteria

1. `POST https://famos.dev.fortressam.ai/auth/test-session` with valid secret returns `200 { message: "Test session created", userId: "qa@fortressam.ai" }`
2. After test-session call, navigating to `https://famos.dev.fortressam.ai/accounts` does NOT redirect to login
3. After test-session call, navigating to `https://famos.dev.fortressam.ai/pipeline` does NOT redirect to login
4. `POST https://famos.dev.fortressam.ai/auth/test-session` with wrong secret returns `401`
5. `POST https://famos.dev.fortressam.ai/auth/test-session` with correct secret returns `404` when called against a prod deployment (ASPNETCORE_ENVIRONMENT=Production)
6. Same criteria (1–5) verified for FIRM, FORMS, and Cowork dev deployments
7. FAM OS `/qa/status` endpoint returns 404 (deleted)
8. FAM OS `X-QA-Bypass` header on any request is ignored (old middleware removed)
9. Natasha's QA runner updated to remove `X-QA-Bypass` header and use `POST /auth/test-session` at session start
10. Natasha successfully QAs all routes in FAM OS including `/accounts` without manual auth intervention

---

## 9. Why the Other Options Were Rejected

**Option A (Fix header bypass):**  
The header bypass can be made to work for HTTP routes by moving it before `UseAuthentication()`. But it cannot work for Blazor interactive pages. Blazor Server's `AuthenticationStateProvider` in interactive mode reads from the `CascadingAuthenticationState` which is initialized from the cookie at WebSocket connection time. There is no per-request mechanism that can inject an identity into an established SignalR circuit via an HTTP header. Even if we got it working today, every new Blazor component with `@attribute [Authorize]` would be a potential failure point. The fundamental mismatch between "header on HTTP request" and "cookie on WebSocket upgrade" cannot be papered over. Option A is a dead end.

**Option B (Entra service account):**  
Valid as a fallback. Not recommended as primary because:
- Non-MFA service accounts require conditional access policy exclusions — a permanent security configuration with ongoing audit burden
- Entra access tokens expire in ~1 hour; refresh tokens expire based on policy (1–90 days). Natasha needs token refresh logic or she gets auth failures mid-run.
- Password rotation requirements create operational overhead
- Any future conditional access policy change could silently break QA
The test-session endpoint is simpler, faster to implement, and easier to reason about. Option B is a valid fallback if Option C somehow fails, but it should not be the primary approach.

---

_Spec by Reed Richards | One endpoint per app, one cookie write, universal auth. 2 new files × 4 apps. FAIT already done. Clint review priorities: `!IsProduction()` guard, `MapControllers()` presence, cookie name consistency, `TestAuth__Secret` never in prod._
