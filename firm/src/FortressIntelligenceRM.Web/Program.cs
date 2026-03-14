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
builder.Services.AddScoped<FirmKbService>();
builder.Services.AddHttpClient();

// AWS SDK services
builder.Services.AddAWSService<IAmazonECS>();
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddAWSService<Amazon.BedrockAgent.IAmazonBedrockAgent>();

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
    // Must match FAIT's cookie name and domain exactly for cross-subdomain cookie sharing
    options.Cookie.Name = ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth:CookieDomain"] ?? "";
    options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHostedService<DatabaseInitializationService>();

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

// Redirect unauthenticated users to FAIT for login — pass returnUrl so FAIT can redirect back
app.MapGet("/auth/redirect-to-login", ctx =>
{
    var faitLoginUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP:LoginUrl"]
        ?? "https://fait.dev.fortressam.ai/";
    // Pass returnUrl so FAIT can redirect back to FIRM after login
    var firmCallbackUrl = ctx.RequestServices.GetRequiredService<IConfiguration>()["FIP:FirmCallbackUrl"]
        ?? "https://meetings.dev.fortressam.ai/auth/firm-session";
    var redirectUrl = $"{faitLoginUrl.TrimEnd('/')}/auth/firm-callback?returnUrl={Uri.EscapeDataString(firmCallbackUrl)}";
    ctx.Response.Redirect(redirectUrl);
    return Task.CompletedTask;
});

// FIRM session endpoint — user arrives here from FAIT with a valid shared cookie
// Resolves local user record and fait_user_id, then redirects to /meetings
app.MapGet("/auth/firm-session", async ctx =>
{
    // If already authenticated via shared cookie, resolve user and go to meetings
    var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!authResult.Succeeded)
    {
        // Cookie not valid — back to login page
        ctx.Response.Redirect("/");
        return;
    }

    // Try to resolve fait_user_id if not already set
    var entraOid = authResult.Principal?.FindFirst("sub")?.Value
        ?? authResult.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var email = authResult.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? authResult.Principal?.FindFirst("email")?.Value
        ?? authResult.Principal?.FindFirst("preferred_username")?.Value;
    var displayName = authResult.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
        ?? authResult.Principal?.FindFirst("name")?.Value
        ?? email ?? "Unknown";

    if (!string.IsNullOrEmpty(email))
    {
        var dbFactory = ctx.RequestServices.GetRequiredService<IDbContextFactory<FirmDbContext>>();
        var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
        var logger = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
        var httpClientFactory = ctx.RequestServices.GetRequiredService<IHttpClientFactory>();

        await using var db = await dbFactory.CreateDbContextAsync();
        var firmUser = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (firmUser == null)
        {
            firmUser = new FortressIntelligenceRM.Web.Models.FirmUser
            {
                Id = Guid.NewGuid(),
                EntraOid = entraOid ?? "",
                Email = email,
                DisplayName = displayName,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            };
            db.Users.Add(firmUser);
        }
        else
        {
            firmUser.LastLoginAt = DateTime.UtcNow;
            firmUser.UpdatedAt = DateTime.UtcNow;
        }

        // Resolve FAIT user ID if not already stored
        if (string.IsNullOrEmpty(firmUser.FaitUserId) && !string.IsNullOrEmpty(entraOid))
        {
            try
            {
                var faitApiUrl = config["FIP:FaitApiUrl"] ?? "https://fait.dev.fortressam.ai";
                var sharedSecret = config["Firm:SharedSecret"] ?? "";
                var httpClient = httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Add("X-Firm-Secret", sharedSecret);
                var response = await httpClient.GetAsync($"{faitApiUrl}/api/firm/resolve-user?entraOid={Uri.EscapeDataString(entraOid)}");
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    var parsed = System.Text.Json.JsonDocument.Parse(body);
                    if (parsed.RootElement.TryGetProperty("userId", out var userIdEl))
                    {
                        firmUser.FaitUserId = userIdEl.GetString();
                        logger.LogInformation("FIRM: Resolved FAIT user ID {FaitUserId} for {Email}", firmUser.FaitUserId, email);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FIRM: Failed to resolve FAIT user ID for {Email} — KB push will be unavailable", email);
            }
        }

        await db.SaveChangesAsync();
    }

    ctx.Response.Redirect("/meetings");
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
