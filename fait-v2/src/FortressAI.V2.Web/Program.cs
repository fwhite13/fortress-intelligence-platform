using Amazon.S3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using MudBlazor.Services;
using Serilog;
using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Components;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Serilog
builder.Logging.ClearProviders();
builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .Enrich.FromLogContext()
       .WriteTo.Console());

// Razor Components + Interactive Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddControllersWithViews();

// MudBlazor
builder.Services.AddMudServices();

// EF Core — FaitV2DbContext (Pomelo MySQL provider, GuidFormat=None in connection string)
builder.Services.AddDbContext<FaitV2DbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)
    ));

// AWS S3 (workspace bucket)
builder.Services.AddAWSService<IAmazonS3>();

// User provisioning
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Forward headers (ALB / ECS pattern)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});

// Security headers — must be before UseStaticFiles
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Health endpoint — public
app.MapGet("/health", () => "OK").AllowAnonymous();

// MicrosoftIdentity challenge/callback endpoints
app.MapControllers();

// Blazor components — all routes require auth
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly)
   .RequireAuthorization();

app.Run();
