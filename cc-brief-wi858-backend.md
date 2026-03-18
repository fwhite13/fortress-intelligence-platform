# CC Brief: WI858 — FfE Entra Auth Refactor (FAIT Backend)

You are making changes to the FAIT backend at `~/projects/fip/fait/src/FortressAI.Web/`.

## MANDATORY: Read the full spec first
```
cat ~/projects/fait-for-excel/FFE-ENTRA-AUTH-SPEC.md
```

## Context
Adding Entra JWT Bearer authentication alongside the existing AppKey scheme. The backend must accept EITHER an Entra JWT (from FfE users signing in via MSAL) OR an AppKey (for CI/testing). Fix the hardcoded Fred White claims in AppKeyAuthHandler. Add a whoami endpoint for first-login identity resolution.

The `AppUser` model is in `~/projects/fip/fait/src/FortressAI.Shared/Models/AppUser.cs`:
```csharp
public class AppUser {
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "user";
    public bool IsEntraUser { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // ... navigation props
}
```
NOTE: There is NO `EntraOid` field. Look up Entra users by `IsEntraUser == true && Email == email`.

## Files to MODIFY:

### 1. `Auth/AppKeyAuthHandler.cs` — Fix hardcoded claims

Current code has hardcoded Fred White claims for ALL valid AppKeys. Fix it:

Replace the claims block with per-key logic:
```csharp
// Check if this is the FfE Excel Addin key (not the Haven key)
var isExcelAddinKey = Options.ApiKeys.Contains(apiKey);
var claims = isExcelAddinKey
    ? new[]
      {
        // Service-level identity for CI/testing — no personal KB access
        new Claim(ClaimTypes.NameIdentifier, "00000000-0000-0000-0000-000000000001"),
        new Claim(ClaimTypes.Name,           "FfE Service Account"),
        new Claim(ClaimTypes.Email,          "ffe-service@internal"),
      }
    : new[]
      {
        // Haven key — existing Fred White claims (unchanged for backward compat)
        new Claim(ClaimTypes.NameIdentifier, "08de7605-3f7d-427d-858a-637777b41018"),
        new Claim("oid",                     "08de7605-3f7d-427d-858a-637777b41018"),
        new Claim(ClaimTypes.Email,          "fwhite@refugems.com"),
        new Claim(ClaimTypes.Name,           "Fred White"),
        new Claim("preferred_username",      "fwhite@refugems.com"),
        new Claim("groups",                  "FIP-Users"),
        new Claim("groups",                  "FAIT-Users"),
      };
```

Keep all other logic in the handler intact (the NoResult for missing header, AllKeys validation, etc.).

### 2. `Program.cs` — Add EntraBearer JWT scheme

Current auth setup (around line 160):
```csharp
builder.Services.AddAuthentication(options => { ... })
.AddCookie(options => { ... })
.AddScheme<AppKeyAuthOptions, AppKeyAuthHandler>("AppKeyAuth", options => { ... });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AppKeyOnly", policy =>
        policy.AddAuthenticationSchemes("AppKeyAuth")
              .RequireAuthenticatedUser());
});
```

Change it to:

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    // Keep ALL existing cookie options unchanged
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.Name = ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
})
.AddScheme<AppKeyAuthOptions, AppKeyAuthHandler>("AppKeyAuth", options =>
{
    options.ApiKey  = builder.Configuration["AppKeys:Haven"];
    options.ApiKeys = new List<string>
    {
        builder.Configuration["AppKeys:ExcelAddin"] ?? ""
    };
})
// NEW: Entra JWT Bearer for FfE
.AddJwtBearer("EntraBearer", options =>
{
    var tenantId = builder.Configuration["Azure:TenantId"]
                   ?? "d2bf3425-f8ab-451c-83bd-1e0ebd9508fe";
    var clientId = builder.Configuration["Azure:ClientId"]
                   ?? "887206bc-fac1-436a-a8ed-2150418d76c0";
    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    options.Audience  = $"api://{clientId}";
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer   = true,
        ValidIssuer      = $"https://login.microsoftonline.com/{tenantId}/v2.0",
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew        = TimeSpan.FromMinutes(5),
    };
    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            // Map the Entra user to their FAIT AppUser.Id for personal KB scoping
            // Entra tokens use 'preferred_username' for email and 'oid' for the object ID
            var email = ctx.Principal?.FindFirst("preferred_username")?.Value
                        ?? ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            if (string.IsNullOrEmpty(email)) return;

            var dbFactory = ctx.HttpContext.RequestServices
                .GetRequiredService<IDbContextFactory<FortressAI.Web.Data.AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var user = await db.Users.FirstOrDefaultAsync(
                u => u.IsEntraUser && u.Email == email);

            if (user != null)
            {
                // Inject the FAIT userId as NameIdentifier so controllers get the right userId
                var identity = ctx.Principal!.Identity as System.Security.Claims.ClaimsIdentity;
                var existing = identity?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (existing != null) identity?.RemoveClaim(existing);
                identity?.AddClaim(new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier,
                    user.Id.ToString()));
            }
            // If user not found, leave claims as-is; whoami endpoint will provision them
        }
    };
});
```

Update the authorization policy — rename `AppKeyOnly` to `ExcelAddinAccess` and add `EntraBearer`:
```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
    options.AddPolicy("AppKeyOnly", policy =>  // Keep for backward compat
        policy.AddAuthenticationSchemes("AppKeyAuth")
              .RequireAuthenticatedUser());
    options.AddPolicy("ExcelAddinAccess", policy =>  // NEW — used by HavenChat + ExcelAddin
        policy.AddAuthenticationSchemes("AppKeyAuth", "EntraBearer")
              .RequireAuthenticatedUser());
});
```

Add the required using at the top if not present:
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
```

### 3. `Controllers/HavenChatController.cs`
Update the `[Authorize]` attribute on the class to use the new policy:
```csharp
// Before:
[Authorize(AuthenticationSchemes = "AppKeyAuth", Policy = "AppKeyOnly")]

// After:
[Authorize(Policy = "ExcelAddinAccess")]
```

That's the ONLY change to HavenChatController.cs.

## File to CREATE:

### 4. `Controllers/ExcelAddinController.cs` — new file
```csharp
using System.Security.Claims;
using FortressAI.Web.Data;
using FortressAI.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Controllers;

/// <summary>
/// REST endpoints for the FAIT for Excel add-in.
/// GET /api/excel/whoami — resolve FAIT identity for an Entra-authenticated user.
/// </summary>
[ApiController]
[Route("api/excel")]
public class ExcelAddinController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ExcelAddinController> _logger;

    public ExcelAddinController(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<ExcelAddinController> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    /// <summary>
    /// Resolve or provision the FAIT AppUser for the calling Entra user.
    /// Called by the taskpane after first sign-in to get the FAIT userId.
    /// </summary>
    [HttpGet("whoami")]
    [Authorize(AuthenticationSchemes = "EntraBearer")]
    public async Task<IActionResult> WhoAmI()
    {
        var email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value ?? "";
        var name  = User.FindFirst("name")?.Value
                    ?? User.FindFirst(ClaimTypes.Name)?.Value ?? email;

        if (string.IsNullOrEmpty(email))
            return Unauthorized(new { error = "No email claim in token" });

        await using var db = await _dbFactory.CreateDbContextAsync();

        // Look up existing Entra user by email
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.IsEntraUser && u.Email == email);

        if (user == null)
        {
            // Provision new FAIT user for this Entra identity (first login)
            user = new AppUser
            {
                Id          = Guid.NewGuid(),
                Email       = email,
                DisplayName = name,
                IsEntraUser = true,
                IsActive    = true,
                Role        = "user",
                CreatedAt   = DateTime.UtcNow,
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            _logger.LogInformation(
                "ExcelAddin: Provisioned new Entra user {Email} as FAIT user {Id}",
                email, user.Id);
        }

        return Ok(new
        {
            userId = user.Id,
            email  = user.Email,
            name   = user.DisplayName ?? user.Email,
        });
    }
}
```

## Gate Checks (run after all changes)
```bash
# AppKey fallback intact
grep -n "AppKeyAuth\|AppKeys\|ExcelAddin" ~/projects/fip/fait/src/FortressAI.Web/Auth/AppKeyAuthHandler.cs | head -4

# Hardcoded Fred White claims FIXED (should show conditional block, not just hardcoded)
grep -n "08de7605\|isExcelAddinKey\|FfE Service" ~/projects/fip/fait/src/FortressAI.Web/Auth/AppKeyAuthHandler.cs | head -6

# Entra JWT in Program.cs
grep -n "AddJwtBearer\|EntraBearer\|OnTokenValidated\|ExcelAddinAccess" ~/projects/fip/fait/src/FortressAI.Web/Program.cs | head -8

# HavenChatController updated
grep -n "Authorize\|ExcelAddinAccess" ~/projects/fip/fait/src/FortressAI.Web/Controllers/HavenChatController.cs | head -4

# whoami endpoint exists
grep -rn "whoami\|WhoAmI" ~/projects/fip/fait/src/FortressAI.Web/Controllers/ | head -4

# Build check
cd ~/projects/fip/fait/src/FortressAI.Web && dotnet build 2>&1 | tail -10
```

## Critical Constraints
1. DO NOT remove or break `AppKeyAuth` scheme — it must remain for the Haven PWA and CI
2. DO NOT remove the `AppKeyOnly` policy — keep it for backward compat, just ADD `ExcelAddinAccess`
3. `AppUser` has NO `EntraOid` field — look up by `IsEntraUser == true && Email == email`
4. The `OnTokenValidated` callback only injects NameIdentifier if the user ALREADY EXISTS in DB — new users are provisioned by `/api/excel/whoami`, not by OnTokenValidated
5. Keep ALL existing cookie auth options exactly as they are
6. The `ExcelAddinController` must use `[Authorize(AuthenticationSchemes = "EntraBearer")]` (Entra only — whoami is not accessible via AppKey)
7. Add `using Microsoft.AspNetCore.Authentication.JwtBearer;` and `using Microsoft.IdentityModel.Tokens;` to Program.cs if not present
