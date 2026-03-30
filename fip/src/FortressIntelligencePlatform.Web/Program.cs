using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using FortressIntelligencePlatform.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Data Protection (FIP portal is the KEY GENERATOR for the shared key ring) ─
// All other FIP apps (FAIT, FIRM, FORMS) consume keys with DisableAutomaticKeyGeneration()
var dbHost = builder.Configuration["FORTRESS_DB_HOST"] ?? "localhost";
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "";
var keyRingDb = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";

var keyRingConnectionString = new MySqlConnector.MySqlConnectionStringBuilder
{
    Server = dbHost,
    Port = uint.Parse(dbPort),
    UserID = dbUser,
    Password = dbPass,
    Database = keyRingDb,
}.ConnectionString;

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingConnectionString, ServerVersion.AutoDetect(keyRingConnectionString),
        mysql => mysql.EnableRetryOnFailure(3)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI");

// ── FIP token store (fip_dev schema) ──────────────────────────────────────────
var fipDbName = builder.Configuration["FIP_DB_NAME"] ?? "fip_dev";
var fipCsb = new MySqlConnector.MySqlConnectionStringBuilder
{
    Server = dbHost,
    Port = uint.Parse(dbPort),
    UserID = dbUser,
    Password = dbPass,
    Database = fipDbName,
    GuidFormat = MySqlConnector.MySqlGuidFormat.None,
    ConnectionTimeout = 10
};
builder.Services.AddDbContextFactory<FipDbContext>(options =>
    options.UseMySql(fipCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

// ── Authentication ────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    options.Cookie.Name = ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
})
.AddOpenIdConnect(options =>
{
    options.Authority = $"https://login.microsoftonline.com/{builder.Configuration["AzureAd:TenantId"]}/v2.0";
    options.ClientId = builder.Configuration["AzureAd:ClientId"];
    options.ClientSecret = builder.Configuration["AzureAd:ClientSecret"];
    options.ResponseType = "code";
    options.SaveTokens = true;
    options.GetClaimsFromUserInfoEndpoint = true;
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.Scope.Add("offline_access");
    options.Scope.Add("https://graph.microsoft.com/Calendars.Read");
    options.Scope.Add("https://graph.microsoft.com/User.Read");
    options.CallbackPath = "/signin-oidc";
    options.SignedOutCallbackPath = "/signout-callback-oidc";
    options.MapInboundClaims = false;

    options.Events.OnRedirectToIdentityProvider = ctx =>
    {
        if (ctx.ProtocolMessage.RedirectUri?.StartsWith("http://") == true)
            ctx.ProtocolMessage.RedirectUri =
                ctx.ProtocolMessage.RedirectUri.Replace("http://", "https://");
        return Task.CompletedTask;
    };

    options.Events.OnTokenValidated = async ctx =>
    {
        // Existing role mapping
        var roles = ctx.Principal?.FindAll("roles").Select(c => c.Value) ?? [];
        var identity = ctx.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
        if (identity != null)
            foreach (var role in roles)
                identity.AddClaim(new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.Role, role));

        ctx.Properties!.IsPersistent = true;
        ctx.Properties.ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12);

        // NEW: store delegated Graph token in fip_dev
        var entraOid = ctx.Principal?.FindFirst("oid")?.Value
            ?? ctx.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var accessToken = ctx.TokenEndpointResponse?.AccessToken;
        var refreshToken = ctx.TokenEndpointResponse?.RefreshToken;
        var expiresIn = ctx.TokenEndpointResponse?.ExpiresIn;

        if (!string.IsNullOrEmpty(entraOid) && !string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                var dbFactory = ctx.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<FipDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var expiresAt = int.TryParse(expiresIn, out var secs)
                    ? DateTime.UtcNow.AddSeconds(secs)
                    : DateTime.UtcNow.AddHours(1);
                var email = ctx.Principal?.FindFirst("preferred_username")?.Value
                    ?? ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

                var existing = await db.UserMicrosoftTokens.FindAsync(entraOid);
                if (existing != null)
                {
                    existing.AccessToken = accessToken;
                    existing.RefreshToken = refreshToken;
                    existing.ExpiresAt = expiresAt;
                    existing.MicrosoftEmail = email;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    db.UserMicrosoftTokens.Add(new FipUserMicrosoftToken
                    {
                        EntraOid = entraOid,
                        AccessToken = accessToken,
                        RefreshToken = refreshToken,
                        ExpiresAt = expiresAt,
                        MicrosoftEmail = email
                    });
                }
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log but never fail login
                var logger = ctx.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "[FIP] Failed to store Graph token for OID {Oid}", entraOid);
            }
        }
    };
});

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// ── Forwarded headers (ALB terminates TLS) ──────────────────────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Health check ─────────────────────────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "fip" }))
   .AllowAnonymous()
   .DisableAntiforgery();

// ── Sign-out ──────────────────────────────────────────────────────────────────
app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme,
        new AuthenticationProperties { RedirectUri = "/" });
}).AllowAnonymous();

// ── Cross-app callback (FIRM/FORMS redirect here after FIP login) ─────────────
app.MapGet("/auth/firm-callback", (HttpContext ctx, string? returnUrl) =>
{
    if (!(ctx.User.Identity?.IsAuthenticated ?? false))
        return Results.Redirect("/");

    if (!string.IsNullOrEmpty(returnUrl) &&
        Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.EndsWith(".fortressam.ai", StringComparison.OrdinalIgnoreCase))
        return Results.Redirect(returnUrl);

    return Results.Redirect("/");
}).RequireAuthorization();

app.MapRazorComponents<FortressIntelligencePlatform.Web.Components.App>()
   .AddInteractiveServerRenderMode();

app.Run();
