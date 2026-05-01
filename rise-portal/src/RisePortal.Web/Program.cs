using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using MudBlazor.Services;
using RisePortal.Web.Components;
using RisePortal.Web.Data;
using RisePortal.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── Entra OIDC Auth (Microsoft Identity Web) ──
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options =>
    {
        builder.Configuration.Bind("AzureAd", options);
        options.SaveTokens = true;
        options.Scope.Add("offline_access");
        options.Scope.Add("OnlineMeetings.Read");
        options.Scope.Add("User.ReadBasic.All");
        options.Scope.Add("Calendars.Read");

        options.Events ??= new OpenIdConnectEvents();
        var originalTokenValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async ctx =>
        {
            if (originalTokenValidated != null)
                await originalTokenValidated(ctx);

            // Capture tokens and store to MySQL
            var tokenService = ctx.HttpContext.RequestServices.GetService<TokenStorageService>();
            if (tokenService != null)
            {
                var oid = ctx.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                       ?? ctx.Principal?.FindFirst("oid")?.Value;
                var accessToken = ctx.TokenEndpointResponse?.AccessToken;
                var refreshToken = ctx.TokenEndpointResponse?.RefreshToken;
                var expiresIn = ctx.TokenEndpointResponse?.ExpiresIn;
                var scopes = ctx.TokenEndpointResponse?.Scope;

                if (!string.IsNullOrEmpty(oid) && !string.IsNullOrEmpty(accessToken))
                {
                    var expiresAt = DateTime.UtcNow.AddSeconds(
                        int.TryParse(expiresIn, out var sec) ? sec : 3600);

                    await tokenService.StoreTokenAsync(oid, accessToken, refreshToken, expiresAt, scopes);
                }
            }
        };

        // Fix redirect URIs behind ALB (HTTP → HTTPS)
        var originalRedirect = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async ctx =>
        {
            if (originalRedirect != null)
                await originalRedirect(ctx);

            if (ctx.ProtocolMessage.RedirectUri?.StartsWith("http://") == true)
                ctx.ProtocolMessage.RedirectUri = ctx.ProtocolMessage.RedirectUri.Replace("http://", "https://");
            if (ctx.ProtocolMessage.PostLogoutRedirectUri?.StartsWith("http://") == true)
                ctx.ProtocolMessage.PostLogoutRedirectUri = ctx.ProtocolMessage.PostLogoutRedirectUri.Replace("http://", "https://");
        };
    });

// Cookie settings
builder.Services.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, options =>
{
    var cookieDomain = builder.Configuration["Auth:CookieDomain"];
    if (!string.IsNullOrEmpty(cookieDomain))
        options.Cookie.Domain = cookieDomain;
    options.Cookie.Name = ".RISE.Auth";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

// ── DataProtection (EF Core → MySQL) ──
var connectionString = builder.Configuration.GetConnectionString("RnFip");
builder.Services.AddDbContext<DataProtectionKeyContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<DataProtectionKeyContext>()
    .SetApplicationName("RISE");

// ── App Services ──
builder.Services.AddSingleton<TokenStorageService>();
builder.Services.AddHostedService<DatabaseInitializationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// ── Forwarded Headers (ALB) ──
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Health endpoint ──
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "rise-portal",
    timestamp = DateTime.UtcNow
})).AllowAnonymous();

// ── Auth endpoints ──
app.MapGet("/auth/login", async (HttpContext ctx) =>
{
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
}).AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  RISE — Refuge Intelligence Suite for Enterprise");
Console.WriteLine("  Running at: http://localhost:8080");
Console.WriteLine("  Auth: Entra OIDC (Microsoft Identity Web)");
Console.WriteLine("═══════════════════════════════════════════════════");

app.Run();
