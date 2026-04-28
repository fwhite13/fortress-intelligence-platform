using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MySqlConnector;
using MudBlazor.Services;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Services;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Amazon.Batch;
using Amazon.ECS;
using Amazon.S3;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Database — prefer env vars (ECS/Aurora) over appsettings
var dbHost = builder.Configuration["FORTRESS_DB_HOST"];
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "dev";
var dbName = builder.Configuration["FIRM_DB_NAME"] ?? "firm_dev";
string firmConnectionString;
if (!string.IsNullOrEmpty(dbHost))
{
    var csb = new MySqlConnectionStringBuilder
    {
        Server = dbHost,
        Port = uint.Parse(dbPort),
        Database = dbName,
        UserID = dbUser,
        Password = dbPass,
        ConnectionTimeout = 10,
        GuidFormat = MySqlGuidFormat.None    // ADO#1329: prevent Char36 auto-cast on CHAR(36) cols
    };
    firmConnectionString = csb.ConnectionString;
    Console.WriteLine($"FIRM: Using Aurora MySQL: {dbHost}/{dbName}");
}
else
{
    var localConn = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? $"Server=localhost;Database={dbName};User=root;Password=dev;";
    var localCsb = new MySqlConnectionStringBuilder(localConn)
    {
        GuidFormat = MySqlGuidFormat.None
    };
    firmConnectionString = localCsb.ConnectionString;
    Console.WriteLine("FIRM: Using local connection string.");
}
var firmServerVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContextFactory<FirmDbContext>(options =>
    options.UseMySql(firmConnectionString, firmServerVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));

// FIP token store — read delegated Graph tokens written by FIP at login
var fipDbName = builder.Configuration["FIP_DB_NAME"] ?? "fip_dev";
var fipCsb = new MySqlConnectionStringBuilder
{
    Server = dbHost ?? "localhost",
    Port = uint.Parse(dbPort),
    Database = fipDbName,
    UserID = dbUser,
    Password = dbPass,
    ConnectionTimeout = 10,
    GuidFormat = MySqlGuidFormat.None
};
builder.Services.AddDbContextFactory<FipDbContext>(options =>
    options.UseMySql(fipCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));

// Application services
builder.Services.AddScoped<MeetingService>();
builder.Services.AddScoped<VpBotService>();
builder.Services.AddSingleton<S3Service>();
builder.Services.AddScoped<FirmKbService>();
builder.Services.AddScoped<CalendarService>();
builder.Services.AddSingleton<IOrgContextService, OrgContextService>();
builder.Services.AddSingleton<IUserWikiService, UserWikiService>();
builder.Services.AddScoped<FipTokenService>();
// Bot Framework
builder.Services.AddSingleton<IBotFrameworkHttpAdapter, AdapterWithErrorHandler>();
builder.Services.AddTransient<IBot, FirmBot>();
builder.Services.AddSingleton<IFirmBotService, FirmBotService>();
builder.Services.AddHttpClient();
// Named client for same-container (self) API calls — bypasses Cloudflare
builder.Services.AddHttpClient("local", client =>
{
    client.BaseAddress = new Uri("http://localhost:8080");
});

// AWS SDK services
builder.Services.AddAWSService<IAmazonECS>();
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<IAmazonBatch>();
builder.Services.AddScoped<IBatchTranscriptionService, BatchTranscriptionService>();
builder.Services.AddAWSService<Amazon.BedrockAgent.IAmazonBedrockAgent>();
builder.Services.AddAWSService<Amazon.BedrockRuntime.IAmazonBedrockRuntime>();

// Controllers for API endpoints
builder.Services.AddControllers();
builder.Services.AddMudServices();

// FIRM is a cookie consumer only — FAIT owns OIDC and sets the auth cookie
// If no valid cookie → redirect to FAIT for login
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
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService<DatabaseInitializationService>();
builder.Services.AddSingleton<TeamsGraphService>();
// builder.Services.AddHostedService(sp => sp.GetRequiredService<TeamsGraphService>()); // ADO#1352: disabled — FIRM reads via FipTokenService now
// builder.Services.AddHostedService<TranscriptPollingService>(); // ADO#1352: disabled

// DataProtection: shared key ring points to fred_dev (FAIT's DB) — same DataProtectionKeys table
// SharedKeyRingDbContext reads from fred_dev via FIP_KEYRING_DB_NAME env var
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
    ConnectionTimeout = 10
};

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));


// FIRM is a consumer — DisableAutomaticKeyGeneration so only FIP portal creates keys
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();

var app = builder.Build();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "firm", timestamp = DateTime.UtcNow })).AllowAnonymous();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Redirect unauthenticated users to FAIT for login — pass returnUrl so FAIT can redirect back
app.MapGet("/auth/redirect-to-login", (HttpContext ctx, IConfiguration config) =>
{
    var fipUrl = config["FIP__LoginUrl"]?.TrimEnd('/') ?? "https://fip.dev.fortressam.ai";
    var firmCallbackUrl = config["FIP__FirmCallbackUrl"]?.TrimEnd('/')
        ?? "https://firm.dev.fortressam.ai/auth/firm-session";
    var redirectUrl = $"{fipUrl}/auth/firm-callback?returnUrl={Uri.EscapeDataString(firmCallbackUrl)}";
    return Results.Redirect(redirectUrl);
}).AllowAnonymous().DisableAntiforgery();

// FIRM session endpoint — user arrives here from FIP portal with a valid shared cookie
app.MapGet("/auth/firm-session", async (HttpContext ctx) =>
{
    var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!authResult.Succeeded)
        return Results.Redirect("/");
    // Cookie is already set by FIP portal — just redirect to home
    return Results.Redirect("/meetings");
}).AllowAnonymous().DisableAntiforgery();

// /auth/ms-callback — Legacy FIRM OAuth endpoint. Disabled in ADO#1352 (tokens now via FIP at login).
app.MapGet("/auth/ms-callback", (HttpContext ctx) =>
{
    return Results.Content(
        "<html><body style='font-family:sans-serif;text-align:center;padding:60px;'>" +
        "<h1>Microsoft 365 connection is now handled via FIP login.</h1>" +
        "<p><a href='/meetings'>Back to Meetings</a></p>" +
        "</body></html>", "text/html");
}).AllowAnonymous().DisableAntiforgery();

// Logout: clear local cookie only (FIP portal owns OIDC sign-out)
app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).AllowAnonymous().DisableAntiforgery();

app.MapControllers();
app.MapRazorComponents<FortressIntelligenceRM.Web.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly);

app.Run();
