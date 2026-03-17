using CoworkWeb.Services;
using CoworkWeb.Data;
using FipShared.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── MudBlazor ────────────────────────────────────────────────────────────────
builder.Services.AddMudServices();

// ── Blazor Server ────────────────────────────────────────────────────────────
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── Scoped services ──────────────────────────────────────────────────────────
builder.Services.AddScoped<CoworkSessionService>();
builder.Services.AddScoped<AgentApiClient>();
builder.Services.AddSingleton<InternalTokenService>();

// ── HTTP client for CoworkAgent API ──────────────────────────────────────────
builder.Services.AddHttpClient("cowork-agent", client =>
{
    var agentUrl = builder.Configuration["CoworkAgent:BaseUrl"] ?? "http://cowork-agent:3000";
    client.BaseAddress = new Uri(agentUrl);
    client.Timeout = TimeSpan.FromMinutes(5);
});

// ── Authentication: pure cookie consumer — FIP portal owns Entra OIDC ────────
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
    options.Cookie.Name = ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── Data protection: shared FIP key ring ─────────────────────────────────────
// CRITICAL: Must use SetApplicationName("FortressAI") — exact string — and DisableAutomaticKeyGeneration.
// Missing either line breaks shared .FortressAI.Session cookie for ALL FIP apps.
var keyRingConnStr = builder.Configuration.GetConnectionString("KeyRingDb")
    ?? throw new InvalidOperationException("ConnectionStrings:KeyRingDb is required");

builder.Services.AddDbContext<SharedKeyRingDbContext>(opt =>
    opt.UseMySql(keyRingConnStr, ServerVersion.AutoDetect(keyRingConnStr),
        mysql => mysql.EnableRetryOnFailure(3)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();

// ── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Auth routes ───────────────────────────────────────────────────────────────
app.MapGet("/auth/redirect-to-login", (HttpContext ctx, IConfiguration config) =>
{
    var fipLoginUrl = config["FIP:LoginUrl"]?.TrimEnd('/') ?? "https://fip.dev.fortressam.ai";
    var coworkCallbackUrl = config["FIP:CoworkCallbackUrl"]?.TrimEnd('/') ?? "https://cowork.dev.fortressam.ai/auth/cowork-session";
    var redirectUrl = $"{fipLoginUrl}/auth/firm-callback?returnUrl={Uri.EscapeDataString(coworkCallbackUrl)}";
    return Results.Redirect(redirectUrl);
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/auth/cowork-session", async (HttpContext ctx) =>
{
    var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!authResult.Succeeded) return Results.Redirect("/");
    return Results.Redirect("/");
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var fipLoginUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP:LoginUrl"]
        ?? "https://fip.dev.fortressam.ai";
    return Results.Redirect(fipLoginUrl);
}).DisableAntiforgery();

// ── Blazor ────────────────────────────────────────────────────────────────────
app.MapRazorComponents<CoworkWeb.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
