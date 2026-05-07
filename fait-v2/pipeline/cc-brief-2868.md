# CC Brief: ADO#2868 — Convert FAIT v2 Entra auth to FIP shared cookie consumer

## Working directory
`/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web`

## Context
FAIT v2 was built with standalone Entra OIDC (`AddMicrosoftIdentityWebApp`). This is wrong.
FIP modules are **cookie consumers** — they read the shared `.FortressAI.Session` cookie set by
`fip.fortressam.ai` after Entra auth. No independent OIDC flow.

This brief covers ALL code changes. No architectural decisions needed — just execute exactly as specified.

---

## Task 1: Remove Microsoft.Identity.Web packages from csproj

**File:** `FortressAI.V2.Web.csproj`

Remove these two lines entirely:
```xml
<PackageReference Include="Microsoft.Identity.Web" Version="3.*" />
<PackageReference Include="Microsoft.Identity.Web.UI" Version="3.*" />
```

Add this line (in the Data Protection section, after the existing DataProtection reference):
```xml
<PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="8.0.*" />
```

---

## Task 2: Replace Program.cs auth block

**File:** `Program.cs`

### Remove ALL of these lines/using statements:
```csharp
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
```

Remove this entire block (the Entra SSO registration):
```csharp
// Entra SSO via Microsoft.Identity.Web
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"))
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// Override the cookie to use the shared FIP session cookie
builder.Services.Configure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.Cookie.Name = ".FortressAI.Session";
        options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? "";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = true;
        options.LoginPath = "/auth/signin";
        options.AccessDeniedPath = "/access-denied";
    });
```

### Replace with (FIP shared cookie consumer pattern — FIRM-exact):
```csharp
// FIP shared cookie consumer — no independent OIDC
// fip.fortressam.ai owns Entra auth; fait-v2 reads the shared .FortressAI.Session cookie
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});

// DataProtection: shared key ring points to fred_dev (FIP portal's DB)
// fait-v2 is a consumer — DisableAutomaticKeyGeneration so only FIP portal creates keys
var keyRingDbHost = builder.Configuration["FORTRESS_DB_HOST"];
var keyRingDbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var keyRingDbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var keyRingDbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "";
var keyRingDbName = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";

var keyRingCsb = new MySqlConnector.MySqlConnectionStringBuilder
{
    Server = keyRingDbHost ?? "localhost",
    Port = uint.Parse(keyRingDbPort),
    Database = keyRingDbName,
    UserID = keyRingDbUser,
    Password = keyRingDbPass,
    ConnectionTimeout = 10,
    GuidFormat = MySqlConnector.MySqlGuidFormat.None   // MANDATORY — matches existing FIRM pattern
};

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "FortressAI")
    .DisableAutomaticKeyGeneration(); // fait-v2 is a consumer — FIP portal creates keys
```

### Also remove this line (MicrosoftIdentity challenge controller — no longer needed):
```csharp
builder.Services.AddControllersWithViews();
```

### And remove this line (no longer needed after removing OIDC):
```csharp
// MicrosoftIdentity challenge/callback endpoints
app.MapControllers();
```

### Add /auth/redirect-to-login endpoint before `app.Run()`:

Add immediately before the `app.Run();` line:
```csharp
// Redirect unauthenticated users to FIP portal for login
app.MapGet("/auth/redirect-to-login", (IConfiguration cfg, HttpContext ctx) =>
{
    var fipUrl = cfg["FIP__LoginUrl"]?.TrimEnd('/') ?? "https://fip.dev.fortressam.ai";
    var returnUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/";
    return Results.Redirect($"{fipUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
}).AllowAnonymous();
```

### Also update the health endpoint to match FIRM pattern (return JSON instead of plain text):
Keep the existing health endpoint as-is (plain "OK" string is fine).

---

## Task 3: Create Data/SharedKeyRingDbContext.cs

**File to CREATE:** `Data/SharedKeyRingDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace FortressAI.V2.Web.Data;

/// <summary>
/// Minimal DbContext for reading the shared FIP data protection key ring.
/// Points to fred_dev (FIP portal's database) — DataProtectionKeys table only.
/// fait-v2 must read from the same key ring as fip.fortressam.ai to decrypt the shared auth cookie.
/// </summary>
public class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");
    }
}
```

---

## Task 4: Update appsettings.json

**File:** `appsettings.json`

Remove the entire `AzureAd` block:
```json
"AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "7152ea12-c930-44b0-bb52-069152161c5b",
    "ClientId": "PLACEHOLDER_NEEDS_REAL_ENTRA_APP_REGISTRATION",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-callback-oidc",
    "ClientSecret": "PLACEHOLDER_SET_IN_KEY_VAULT_OR_ENV"
},
```

Replace the existing `Auth` block:
```json
"Auth": {
    "CookieDomain": ""
},
```
With:
```json
"Auth": {
    "CookieName": ".FortressAI.Session",
    "CookieDomain": ""
},
"FIP__LoginUrl": "https://fip.dev.fortressam.ai",
"DataProtection": {
    "ApplicationName": "FortressAI"
},
```

Also update the existing `FIP` block. Change:
```json
"FIP": {
    "ComingSoonApps": "",
    "LoginUrl": "https://fait.fortressintelligence.com"
},
```
To:
```json
"FIP": {
    "ComingSoonApps": "",
    "LoginUrl": "https://fip.dev.fortressam.ai"
},
```

---

## Task 5: Verify using statements in Program.cs

After edits, ensure these usings are present at top of Program.cs (add if missing):
```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using FortressAI.V2.Web.Data;
```

And ensure these are REMOVED (not present):
```csharp
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
```

---

## Acceptance Criteria
1. Program.cs has NO reference to `OpenIdConnect`, `Microsoft.Identity.Web`, `AddMicrosoftIdentityWebApp`
2. Program.cs has `AddAuthentication` with `CookieAuthenticationDefaults.AuthenticationScheme` as both DefaultScheme and DefaultChallengeScheme
3. `SharedKeyRingDbContext` is registered and wired to `AddDataProtection().PersistKeysToDbContext<SharedKeyRingDbContext>()`
4. `DisableAutomaticKeyGeneration()` is present
5. `/auth/redirect-to-login` endpoint exists and redirects to FIP portal
6. `appsettings.json` has no `AzureAd` block
7. `Data/SharedKeyRingDbContext.cs` exists with namespace `FortressAI.V2.Web.Data`
8. `FortressAI.V2.Web.csproj` has no `Microsoft.Identity.Web` or `Microsoft.Identity.Web.UI` references
9. `FortressAI.V2.Web.csproj` has `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`

Do NOT touch any other files. Do NOT modify migration files. Do NOT change FaitV2DbContext.cs.
After all edits, run `dotnet build` from `/home/fredw/projects/fip/fait-v2/src/FortressAI.V2.Web` and report the result.
