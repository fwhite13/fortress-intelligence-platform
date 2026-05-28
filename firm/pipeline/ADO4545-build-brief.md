# CC Build Brief: ADO#4545 — FIRM JWT Bearer Authentication for Mobile API

## Context
FIRM's mobile companion app sends `Authorization: Bearer <token>` (Entra PKCE tokens).
Currently, `Program.cs` only has `AddCookie` — no JWT Bearer scheme.
Mobile API calls return HTML login redirects instead of JSON 401s.

Fix: add `AddJwtBearer` alongside existing cookie auth + a combined `CookieOrBearer` authorization 
policy on mobile endpoints only.

---

## Files to Read
1. `firm/src/FortressIntelligenceRM.Web/Program.cs` — find `AddAuthentication` / `AddCookie` block AND `AddAuthorization` block
2. `firm/src/FortressIntelligenceRM.Web/Controllers/MeetingsApiController.cs` — find mobile endpoints

---

## Changes Required

### 1. `firm/src/FortressIntelligenceRM.Web/Program.cs`

#### 1a. Add `AddJwtBearer` to existing `AddAuthentication` chain

Find this exact existing block:
```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    // Redirect to FAIT for login — FIRM has no auth of its own
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    // Must match FAIT's cookie name and domain exactly for cross-subdomain cookie sharing
    options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});
```

Change it to (append `.AddJwtBearer(...)` — DO NOT CHANGE the existing cookie options at all):
```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    // Redirect to FAIT for login — FIRM has no auth of its own
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    // Must match FAIT's cookie name and domain exactly for cross-subdomain cookie sharing
    options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
})
.AddJwtBearer("Bearer", options =>
{
    options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
    options.Audience = $"api://{builder.Configuration["AzureAd:ClientId"]}";
    options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true
    };
});
```

You will also need to add this using at the top of Program.cs if not already present:
```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
```

#### 1b. Add `CookieOrBearer` policy to existing `AddAuthorization` block

Find this existing block:
```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});
```

Change it to:
```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
    options.AddPolicy("CookieOrBearer", policy =>
        policy.AddAuthenticationSchemes(
            CookieAuthenticationDefaults.AuthenticationScheme,
            "Bearer")
        .RequireAuthenticatedUser());
});
```

### 2. `firm/src/FortressIntelligenceRM.Web/Controllers/MeetingsApiController.cs`

Update ONLY these 4 mobile endpoint attributes from `[Authorize]` to `[Authorize(Policy = "CookieOrBearer")]`:

1. `[HttpGet("/api/firm/me")]` — method `GetMe()` 
   Change: `[Authorize]` → `[Authorize(Policy = "CookieOrBearer")]`

2. `[HttpPost("/api/firm/register-push-token")]` — method `RegisterPushToken()`
   Change: `[Authorize]` → `[Authorize(Policy = "CookieOrBearer")]`

3. `[HttpPost("/api/meetings/mobile-upload")]` — method `MobileUpload()`
   Change: `[Authorize]` → `[Authorize(Policy = "CookieOrBearer")]`

4. `[HttpGet("/api/meetings/list")]` — method `ListMeetings()`
   Change: `[Authorize]` → `[Authorize(Policy = "CookieOrBearer")]`

**DO NOT change `[Authorize]` on any other endpoint.** All other endpoints (VpCallback, JoinMeeting,
DownloadTranscript, DownloadSummary, GetAudio, PushToKb, etc.) stay as-is.

Note: `VpCallback` and `VpGetOrgContext` are `[AllowAnonymous]` — do not touch these.

---

## Build Verification

After making the changes, run:
```bash
cd /home/fredw/projects/fip && dotnet build firm/src/FortressIntelligenceRM.Web/FortressIntelligenceRM.Web.csproj 2>&1
```

The build MUST return 0 errors. Warnings are acceptable.

Also verify the NuGet package for JWT Bearer is available. The project already has
`Microsoft.AspNetCore.Authentication.JwtBearer` available via the ASP.NET Core framework (it's a 
framework-provided package for .NET 8). If it's not already referenced and the build fails with
a missing type, add the package:
```bash
cd /home/fredw/projects/fip/firm/src/FortressIntelligenceRM.Web && dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.*
```

---

## Constraints

- DefaultScheme STAYS as `CookieAuthenticationDefaults.AuthenticationScheme` — Blazor web UI must be unaffected
- DefaultChallengeScheme STAYS as `CookieAuthenticationDefaults.AuthenticationScheme` — unauthenticated web users still redirect to login page
- The Bearer scheme is ADDITIVE — it does not change the default behavior for non-API routes
- Only the 4 listed mobile endpoints get `[Authorize(Policy = "CookieOrBearer")]`
- Everything else is untouched

---

## Output

When done, print a summary:
- Files modified (list each file)
- For Program.cs: confirm AddJwtBearer was added and CookieOrBearer policy was added
- For MeetingsApiController.cs: list each endpoint updated
- Build result: exit code and error count (e.g., "Build succeeded: 0 errors, 4 warnings")
