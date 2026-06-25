using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using MudBlazor.Services;
using MySqlConnector;
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
        options.ResponseType = "code"; // auth code flow — required to get access + refresh tokens
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

            var oid = ctx.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                   ?? ctx.Principal?.FindFirst("oid")?.Value;

            // Capture tokens and store to MySQL
            var tokenService = ctx.HttpContext.RequestServices.GetService<TokenStorageService>();
            if (tokenService != null)
            {
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

            // Upsert rise_users + backfill access list on every login
            if (!string.IsNullOrEmpty(oid))
            {
                var email = ctx.Principal?.FindFirst("preferred_username")?.Value
                         ?? ctx.Principal?.FindFirst("email")?.Value;
                var displayName = ctx.Principal?.FindFirst("name")?.Value;
                var now = DateTime.UtcNow;

                var cs = ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>()
                             .GetConnectionString("RnFip");
                try
                {
                    await using var conn = new MySqlConnection(cs);
                    await conn.OpenAsync();

                    await using var upsertCmd = new MySqlCommand(
                        @"INSERT INTO rise_users (entra_oid, email, display_name, first_login, last_login)
                          VALUES (@oid, @email, @displayName, @now, @now)
                          ON DUPLICATE KEY UPDATE email=@email, display_name=@displayName, last_login=@now",
                        conn);
                    upsertCmd.Parameters.AddWithValue("@oid", oid);
                    upsertCmd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@displayName", (object?)displayName ?? DBNull.Value);
                    upsertCmd.Parameters.AddWithValue("@now", now);
                    await upsertCmd.ExecuteNonQueryAsync();

                    await using var backfillCmd = new MySqlCommand(
                        @"UPDATE rise_app_card_access
                          SET email=@email, display_name=@displayName
                          WHERE entra_oid=@oid
                            AND (email IS NULL OR email='' OR display_name IS NULL OR display_name='')",
                        conn);
                    backfillCmd.Parameters.AddWithValue("@oid", oid);
                    backfillCmd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
                    backfillCmd.Parameters.AddWithValue("@displayName", (object?)displayName ?? DBNull.Value);
                    await backfillCmd.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    var logger = ctx.HttpContext.RequestServices.GetService<ILogger<Program>>();
                    logger?.LogWarning("RISE: Failed to upsert rise_users on login: {Message}", ex.Message);
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
    options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? ".RISE.Session";
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

// ── RISE Portal DbContext (EF — GuidFormat=None is in the connection string) ──
builder.Services.AddDbContextFactory<RisePortalContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

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

// FIRM/RN callback — module redirects here to acquire shared auth cookie
app.MapGet("/auth/firm-callback", async (HttpContext ctx, string? returnUrl) =>
{
    if (ctx.User?.Identity?.IsAuthenticated != true)
    {
        var callbackUrl = "/auth/firm-callback" + (returnUrl != null ? "?returnUrl=" + Uri.EscapeDataString(returnUrl) : "");
        await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
        {
            RedirectUri = callbackUrl
        });
        return;
    }
    var target = returnUrl ?? "/";
    ctx.Response.Redirect(target);
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  RISE — Refuge Intelligence Suite for Enterprise");
Console.WriteLine("  Running at: http://localhost:8080");
Console.WriteLine("  Auth: Entra OIDC (Microsoft Identity Web)");
Console.WriteLine("═══════════════════════════════════════════════════");

app.Run();
