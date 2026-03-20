# FIRM Test Auth Bypass Spec

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-17  
**Status:** Ready for Implementation  
**Target:** Tony Stark (software-engineer) via CC  
**Reviewer:** Clint Barton (code-reviewer)  
**Reference:** FAIT implementation in `fip/fait/src/FortressAI.Web/Controllers/TestAuthController.cs`

---

## Pre-Read: What Was Confirmed

**FAIT pattern (reference):**
- `TestAuthService.cs` — validates `TestAuth:Secret` config; builds `ClaimsPrincipal` with userId, email, name, oid, tid claims
- `TestAuthController.cs` — `POST /auth/test-session`, `[AllowAnonymous]`, gated by `_services.GetService<TestAuthService>() == null → 404`
- `Program.cs` line 244: `if (builder.Environment.IsDevelopment()) { builder.Services.AddSingleton<TestAuthService>(); ... }`
- Rate limiter `"test-auth"` registered in same `IsDevelopment()` block
- `[EnableRateLimiting("test-auth")]` on the endpoint

**FIRM vs FAIT auth setup diff:**

| Aspect | FAIT | FIRM | Diff |
|--------|------|------|------|
| Cookie name | `.FortressAI.Session` | `.FortressAI.Session` | ✅ Identical |
| Cookie domain | `Auth__CookieDomain` config | `Auth__CookieDomain` config | ✅ Identical |
| DataProtection key ring | Shared `fred_dev.DataProtectionKeys` table | Shared `fred_dev.DataProtectionKeys` table | ✅ Identical |
| Auth scheme | `CookieAuthenticationDefaults.AuthenticationScheme` | `CookieAuthenticationDefaults.AuthenticationScheme` | ✅ Identical |
| Login redirect | `/auth/redirect-to-login` | `/auth/redirect-to-login` | ✅ Identical |
| `TestAuth:Secret` config | `TestAuth:Secret` key | Not present | ❌ Missing |
| `TestAuthService` | Registered in `IsDevelopment()` | Not registered | ❌ Missing |
| Rate limiter | Registered in `IsDevelopment()` | Not registered | ❌ Missing |
| `TestAuthController` | Present | **Not present** | ❌ Missing |

**FIRM-specific difference:** FIRM's user provisioning uses `FirmUser` (not FAIT's `AppUser`). The test session must also create/find a `FirmUser` record for the test identity — otherwise `MeetingService.GetOrCreateUserAsync()` fails on first authenticated request.

---

## Files to Create/Modify

| File | Action |
|------|--------|
| `Services/TestAuthService.cs` | **New** — verbatim port of FAIT's; update namespace |
| `Controllers/TestAuthController.cs` | **New** — verbatim port of FAIT's; update namespace; add FirmUser provision |
| `Models/TestSessionRequest.cs` | **New** — verbatim port of FAIT's; update namespace |
| `Program.cs` | **Modified** — add `IsDevelopment()` block for service registration |

---

## Task 1: `Services/TestAuthService.cs`

Copy FAIT's implementation exactly. Change namespace only.

```csharp
// firm/src/FortressIntelligenceRM.Web/Services/TestAuthService.cs
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FortressIntelligenceRM.Web.Services;

public class TestAuthService
{
    private readonly IConfiguration _config;
    private readonly ILogger<TestAuthService> _logger;

    public TestAuthService(IConfiguration config, ILogger<TestAuthService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public bool ValidateSecret(string secret)
    {
        var expected = _config["TestAuth:Secret"];
        if (string.IsNullOrEmpty(expected)) return false;
        return string.Equals(secret, expected, StringComparison.Ordinal);
    }

    public ClaimsPrincipal BuildTestPrincipal(string userId, string displayName)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, displayName),
            new(ClaimTypes.Email, userId),
            new("preferred_username", userId),
            new("http://schemas.microsoft.com/identity/claims/objectidentifier",
                Guid.NewGuid().ToString()),
            new("tid", _config["Azure:TenantId"] ?? "test-tenant"),
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }
}
```

---

## Task 2: `Models/TestSessionRequest.cs`

```csharp
// firm/src/FortressIntelligenceRM.Web/Models/TestSessionRequest.cs
namespace FortressIntelligenceRM.Web.Models;

public class TestSessionRequest
{
    public string Secret      { get; set; } = string.Empty;
    public string UserId      { get; set; } = "natasha@fortressam.ai";
    public string DisplayName { get; set; } = "Natasha Romanoff (Test)";
}
```

---

## Task 3: `Controllers/TestAuthController.cs`

FIRM-specific addition: provision a `FirmUser` row for the test identity so auth-gated pages don't fail on `GetOrCreateUserAsync`. The test user gets a stable fake `EntraOid` derived from the `UserId` string.

```csharp
// firm/src/FortressIntelligenceRM.Web/Controllers/TestAuthController.cs
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using FortressIntelligenceRM.Web.Models;
using FortressIntelligenceRM.Web.Services;

namespace FortressIntelligenceRM.Web.Controllers;

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

    [EnableRateLimiting("test-auth")]
    [HttpPost("test-session")]
    public async Task<IActionResult> CreateTestSession([FromBody] TestSessionRequest request)
    {
        // Guard: only available in Development. Returns 404 in all other environments.
        var testAuth = _services.GetService<TestAuthService>();
        if (testAuth == null)
            return NotFound();

        if (!testAuth.ValidateSecret(request.Secret))
        {
            _logger.LogWarning("TestAuth: invalid secret attempt from {IP}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid secret" });
        }

        // FIRM-specific: ensure a FirmUser row exists for the test identity
        // so post-auth flows (MeetingService.GetOrCreateUserAsync) don't fail.
        var meetingService = _services.GetRequiredService<MeetingService>();
        // Use a stable fake EntraOid derived from the userId string
        var fakeOid = $"test-{request.UserId.Replace("@", "-at-").Replace(".", "-")}";
        await meetingService.GetOrCreateUserAsync(fakeOid, request.UserId, request.DisplayName);

        var principal = testAuth.BuildTestPrincipal(request.UserId, request.DisplayName);

        _logger.LogInformation("TestAuth: creating FIRM test session for {UserId} from {IP}",
            request.UserId, HttpContext.Connection.RemoteIpAddress);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddHours(8)
            });

        return Ok(new
        {
            message   = "FIRM test session created",
            userId    = request.UserId,
            expiresIn = "8 hours"
        });
    }
}
```

---

## Task 4: `Program.cs` — Add `IsDevelopment()` Block

Find the existing pattern in `Program.cs` (after the service registrations, before `app.Build()`). Add:

```csharp
// ── Dev-only: test auth bypass for Natasha's QA flows ──
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<TestAuthService>();
    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("test-auth", policy =>
        {
            policy.Window            = TimeSpan.FromMinutes(1);
            policy.PermitLimit       = 10;
            policy.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
            policy.QueueLimit        = 0;
        });
    });
}
```

**Add rate limiter middleware** after `app.Build()` (if not already present):

```csharp
app.UseRateLimiter();
```

Place this before `app.UseAuthentication()`. If `UseRateLimiter()` is already called (check current `Program.cs`), do not add it again.

---

## Environment Variable

The `TestAuth:Secret` value must be set in `appsettings.Development.json` for local dev:

```json
{
  "TestAuth": {
    "Secret": ""
  }
}
```

Leave the value empty in the committed file. Natasha sets her own secret via `FIRM__TestAuth__Secret` env var in her local `.env` or launch profile. **Never commit a real secret value.**

`TestAuth__Secret` must **not** appear in:
- Production secrets store
- ECS task definition
- CI/CD prod pipeline

It is only read when `ASPNETCORE_ENVIRONMENT=Development`. In production, `TestAuthService` is never registered (`IsDevelopment()` guard), so the endpoint returns 404 regardless.

---

## Acceptance Criteria

1. `POST /auth/test-session` with correct secret returns 200 + sets `.FortressAI.Session` cookie in Development.
2. Subsequent requests to `/meetings` or any auth-gated FIRM page succeed with that cookie.
3. A `firm_users` row exists for the test user after the first `POST /auth/test-session` call.
4. `POST /auth/test-session` returns 404 in Production (`ASPNETCORE_ENVIRONMENT=Production`).
5. `POST /auth/test-session` with wrong secret returns 401.
6. More than 10 calls per minute from the same IP returns 429.

---

## Clint Review Priorities

```
⚠️  HIGH: Verify the IsDevelopment() guard is on the builder.Services block
          (at build time), not just on the app block (at request time).
          TestAuthService must NOT be registered in Production DI.
          The controller's GetService<TestAuthService>() == null check is
          a defense-in-depth measure, not the primary guard.

⚠️  HIGH: Verify TestAuth:Secret is empty in appsettings.Development.json
          and is NOT present in appsettings.json or any environment-specific
          file that gets committed. The secret must come from env var only.

⚠️  MEDIUM: Verify app.UseRateLimiter() is called. Without it, the
            [EnableRateLimiting] attribute is silently ignored.
            Check current Program.cs before adding — do not duplicate.

⚠️  LOW: Verify the fake EntraOid used for FirmUser provisioning
         (fakeOid = "test-{userId}") is deterministic across test runs.
         The same UserId must produce the same EntraOid so repeated
         test-session calls don't create duplicate FirmUser rows.
         The current implementation is deterministic — confirm this.
```

---

_Spec by Reed Richards | FIRM test auth: 3 new files + 1 modified (Program.cs). Verbatim port of FAIT pattern + FIRM-specific FirmUser provisioning. Gated on `IsDevelopment()` at service registration time._
