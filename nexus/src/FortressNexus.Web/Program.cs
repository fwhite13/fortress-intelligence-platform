using Amazon.S3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using MySqlConnector;
using FortressNexus.Web.Components;
using FortressNexus.Web.Data;
using FortressNexus.Web.Services;
using FortressNexus.Web.Services.Exporters;
using Azure.Identity;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// Razor Components + Interactive Server
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Cookie auth — consume the FIP shared session cookie
// NEXUS is a FIP module; FAIT owns auth and sets .FortressAI.Session at login
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
    // Redirect to FIP portal for login — NEXUS has no auth of its own
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    // Must match FAIT's cookie name and domain exactly for cross-subdomain sharing
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
builder.Services.AddControllersWithViews();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// MudBlazor
builder.Services.AddMudServices();

// Database — NEXUS_DB_* preferred, fall back to FORTRESS_DB_*
var dbHost = builder.Configuration["NEXUS_DB_HOST"]
    ?? builder.Configuration["FORTRESS_DB_HOST"]
    ?? "localhost";
var dbUser = builder.Configuration["NEXUS_DB_USER"]
    ?? builder.Configuration["FORTRESS_DB_USER"]
    ?? "root";
var dbPassword = builder.Configuration["NEXUS_DB_PASSWORD"]
    ?? builder.Configuration["FORTRESS_DB_PASS"]
    ?? "dev";

var csb = new MySqlConnectionStringBuilder
{
    Server = dbHost,
    Port = 3306,
    Database = builder.Configuration["FIP_DB_NAME"] ?? builder.Configuration["FRED_DB_NAME"] ?? "nexus",
    UserID = dbUser,
    Password = dbPassword,
    GuidFormat = MySqlGuidFormat.None,   // MANDATORY — prevents CHAR(36) auto-cast
    AllowPublicKeyRetrieval = true,
    SslMode = builder.Environment.IsDevelopment() ? MySqlSslMode.None : MySqlSslMode.Required,
    ConnectionTimeout = 10
};

var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContext<NexusDbContext>(options =>
    options.UseMySql(csb.ConnectionString, serverVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));

// DataProtection: shared key ring points to fred_dev (FAIT's DB) — same DataProtectionKeys table
// SharedKeyRingDbContext reads from fred_dev via FIP_KEYRING_DB_NAME env var
var keyRingDbHost = builder.Configuration["FORTRESS_DB_HOST"];
var keyRingDbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var keyRingDbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var keyRingDbPass = builder.Configuration["NEXUS_DB_PASSWORD"]
    ?? builder.Configuration["FORTRESS_DB_PASS"]
    ?? "";
var keyRingDbName = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";

var keyRingCsb = new MySqlConnector.MySqlConnectionStringBuilder
{
    Server = keyRingDbHost ?? "localhost",
    Port = uint.Parse(keyRingDbPort),
    Database = keyRingDbName,
    UserID = keyRingDbUser,
    Password = keyRingDbPass,
    ConnectionTimeout = 10
};

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

// NEXUS is a consumer — DisableAutomaticKeyGeneration so only FIP portal creates keys
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();

// Application services
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<ISpecGenerationService, SpecGenerationService>();
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddScoped<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IMockupProcessingService, MockupProcessingService>();
builder.Services.AddScoped<IArtifactGenerationService, ArtifactGenerationService>();
builder.Services.AddScoped<UserContextService>();
builder.Services.AddScoped<IAdoService, StubAdoService>();
builder.Services.AddScoped<ISpecExporter, MarkdownExporter>();
builder.Services.AddScoped<BedrockService>();
builder.Services.AddScoped<IMockupSectionizer, MockupSectionizerService>();
builder.Services.AddScoped<ISpecService, SpecService>();

// DB initialization (CREATE TABLE IF NOT EXISTS at startup)
builder.Services.AddHostedService<DatabaseInitializationService>();

// Azure Key Vault — load production secrets
var vaultUri = builder.Configuration["KeyVaultSettings:VaultUri"];
if (!string.IsNullOrEmpty(vaultUri)
    && vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
    && vaultUri.Contains(".vault.azure.net", StringComparison.OrdinalIgnoreCase)
    && !vaultUri.Contains("placeholder", StringComparison.OrdinalIgnoreCase))
{
    builder.Configuration.AddAzureKeyVault(new Uri(vaultUri), new DefaultAzureCredential());
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseStaticFiles();
// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Content-Security-Policy"] = "frame-ancestors 'none'";
    await next();
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Health endpoint — public, no auth
app.MapGet("/health", () => "OK").AllowAnonymous();

// Auth redirect (for unauthenticated users)
app.MapGet("/auth/redirect-to-login", (HttpContext ctx) =>
{
    var loginUrl = builder.Configuration["FIP:LoginUrl"]?.TrimEnd('/') ?? "https://fip.fortressam.ai";
    var path = ctx.Request.Path.Value ?? "/";
    var returnUrl = Uri.EscapeDataString($"https://nexus.fortressam.ai{path}");
    return Results.Redirect($"{loginUrl}?returnUrl={returnUrl}");
}).AllowAnonymous();

// API controllers (SubmissionExportController — ADO#1526)
app.MapControllers();

// Blazor components — ALL routes require auth
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly)
   .RequireAuthorization();

app.Run();
