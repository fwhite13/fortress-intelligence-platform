using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using MySqlConnector;
using MudBlazor.Services;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Services;
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
        ConnectionTimeout = 10
    };
    firmConnectionString = csb.ConnectionString;
    Console.WriteLine($"FIRM: Using Aurora MySQL: {dbHost}/{dbName}");
}
else
{
    firmConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? $"Server=localhost;Database={dbName};User=root;Password=dev;";
    Console.WriteLine("FIRM: Using local connection string.");
}
var firmServerVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContextFactory<FirmDbContext>(options =>
    options.UseMySql(firmConnectionString, firmServerVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));

// Application services
builder.Services.AddScoped<MeetingService>();
builder.Services.AddScoped<VpBotService>();
builder.Services.AddScoped<S3Service>();
builder.Services.AddHttpClient();

// AWS SDK services
builder.Services.AddAWSService<IAmazonECS>();
builder.Services.AddAWSService<IAmazonS3>();

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
    options.LoginPath = new Microsoft.AspNetCore.Http.PathString("/auth/redirect-to-login");
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    // Must match FAIT cookie domain exactly for cross-subdomain sharing
    var cookieDomain = builder.Configuration["Auth:CookieDomain"];
    if (!string.IsNullOrEmpty(cookieDomain))
        options.Cookie.Domain = cookieDomain;
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService<DatabaseInitializationService>();

// DataProtection: persist keys to SAME table as FAIT (DataProtectionKeys) for shared cookie ring
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<FirmDbContext>()
    .SetApplicationName("FortressAI");

var app = builder.Build();

// Health endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "firm", timestamp = DateTime.UtcNow }));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();

// Redirect unauthenticated users to FAIT for login — FIRM has no auth flow of its own
app.MapGet("/auth/redirect-to-login", ctx =>
{
    var faitLoginUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP:LoginUrl"]
        ?? "https://fait.dev.fortressam.ai/";
    ctx.Response.Redirect(faitLoginUrl);
    return Task.CompletedTask;
});

// Logout: clear local cookie only (FAIT owns OIDC sign-out)
app.MapGet("/auth/logout", async ctx =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    ctx.Response.Redirect("/");
});

app.MapControllers();
app.MapRazorComponents<FortressIntelligenceRM.Web.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
