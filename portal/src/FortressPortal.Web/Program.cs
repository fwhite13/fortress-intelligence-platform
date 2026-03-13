using MudBlazor.Services;
using FortressPortal.Web.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── MudBlazor ──
builder.Services.AddMudServices();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── Auth Stub vs. Cognito OIDC ──
var useStubAuth = builder.Configuration.GetValue<bool>("UseStubAuth");

if (useStubAuth)
{
    // Stub mode: register a no-op authentication scheme so [Authorize] is satisfied
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        // In stub mode, all paths are accessible — login just redirects to root
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
    });

    // Allow anonymous access everywhere in stub mode
    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = null; // No forced auth in stub mode
    });

    Console.WriteLine("⚠️  UseStubAuth=true — Cognito auth is DISABLED. For scaffold testing only.");
}
else
{
    // Real Cognito OIDC auth (wired in weekend sprint)
    var cognitoAuthority = builder.Configuration["Cognito__Authority"];
    var cognitoClientId = builder.Configuration["Cognito__ClientId"];
    var cognitoClientSecret = builder.Configuration["Cognito__ClientSecret"];

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/access-denied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = cognitoAuthority;
        options.ClientId = cognitoClientId;
        options.ClientSecret = cognitoClientSecret;
        options.ResponseType = "code";
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";

        options.TokenValidationParameters = new TokenValidationParameters
        {
            RoleClaimType = "cognito:groups"
        };

        options.Events = new OpenIdConnectEvents
        {
            OnRedirectToIdentityProvider = ctx =>
            {
                if (ctx.ProtocolMessage.RedirectUri != null && ctx.ProtocolMessage.RedirectUri.StartsWith("http://"))
                    ctx.ProtocolMessage.RedirectUri = ctx.ProtocolMessage.RedirectUri.Replace("http://", "https://");
                if (ctx.ProtocolMessage.PostLogoutRedirectUri != null && ctx.ProtocolMessage.PostLogoutRedirectUri.StartsWith("http://"))
                    ctx.ProtocolMessage.PostLogoutRedirectUri = ctx.ProtocolMessage.PostLogoutRedirectUri.Replace("http://", "https://");
                return Task.CompletedTask;
            },
            OnTokenValidated = ctx =>
            {
                var groups = ctx.Principal?.FindAll("cognito:groups").Select(c => c.Value) ?? [];
                var identity = ctx.Principal?.Identity as ClaimsIdentity;
                if (identity != null)
                {
                    foreach (var group in groups)
                        identity.AddClaim(new Claim(ClaimTypes.Role, group));
                }
                return Task.CompletedTask;
            }
        };

        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        options.Scope.Add("profile");
    });

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = options.DefaultPolicy;
    });

    Console.WriteLine("✅  Cognito OIDC auth enabled.");
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

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

// ── Health check endpoint (ALB health probe) ──
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })).AllowAnonymous();

// ── Auth endpoints ──
app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!useStubAuth)
        await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/");
}).AllowAnonymous();

app.MapGet("/auth/login", async (HttpContext ctx) =>
{
    if (useStubAuth)
    {
        ctx.Response.Redirect("/");
        return;
    }
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  Fortress Intelligence Platform — Portal");
Console.WriteLine("  Running at: http://localhost:8080");
Console.WriteLine($"  Auth mode: {(useStubAuth ? "STUB (no auth)" : "Cognito OIDC")}");
Console.WriteLine("═══════════════════════════════════════════════════");

app.Run();
