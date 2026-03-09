using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;
using FortressFormTools.Data;
using FortressFormTools.Data.Entities;
using MudBlazor.Services;
using FortressFormTools.Web.Services;
using FortressFormTools.Web.Components;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// ── Kestrel: allow large PDF uploads (50 MB) ──
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
});

// ── Blazor Server ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── MudBlazor ──
builder.Services.AddMudServices();

// ── Controllers (API endpoints) ──
builder.Services.AddControllers();

// ── Authentication (Cognito OIDC — matching FIRM pattern) ──
var cognitoAuthority = builder.Configuration["Auth:CognitoAuthority"];
var cognitoClientId = builder.Configuration["Auth:CognitoClientId"];
var cognitoClientSecret = builder.Configuration["Auth:CognitoClientSecret"];

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

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── EF Core (Aurora MySQL) ──
// If FORTRESS_DB_HOST is set (ECS/production), build connection string from individual vars.
// Individual env vars take priority over appsettings.json ConnectionStrings (secrets injection pattern).
var dbHost = builder.Configuration["FORTRESS_DB_HOST"];
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "dev";
var dbName = builder.Configuration["FORMIQ_DB_NAME"] ?? "formiq_dev";
string connectionString;
if (!string.IsNullOrEmpty(dbHost))
{
    // Production: use MySqlConnectionStringBuilder to safely handle special chars in password
    var csb = new MySqlConnectionStringBuilder
    {
        Server = dbHost,
        Port = uint.Parse(dbPort),
        Database = dbName,
        UserID = dbUser,
        Password = dbPass,
        ConnectionTimeout = 10
    };
    connectionString = csb.ConnectionString;
    Console.WriteLine($"Using Aurora MySQL: {dbHost}/{dbName}");
}
else
{
    // Local dev: fall back to appsettings.json connection string
    connectionString = builder.Configuration.GetConnectionString("Default")
        ?? builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Server=localhost;Database=formiq_dev;User=root;Password=dev;";
    Console.WriteLine("Using local connection string.");
}
// Use hardcoded MySQL 8.0 version — DO NOT use AutoDetect (it connects to DB at startup registration time)
var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(connectionString, serverVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));

// ── Data Protection: persist keys to DB so antiforgery tokens survive container restarts ──
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("FortressFormTools");

// ── Default HttpClient for Blazor component API calls ──
var internalBaseUrl = builder.Environment.IsDevelopment()
    ? "http://localhost:5200/"
    : "http://localhost:8080/";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(internalBaseUrl) });

// ── HttpClient for Fortress API ──
builder.Services.AddHttpClient("FortressApi", client =>
{
    var config = builder.Configuration.GetSection("FortressApi");
    client.BaseAddress = new Uri(config["Endpoint"] ?? "https://api.fortressam.ai");
    client.DefaultRequestHeaders.Add("apiKey", config["ApiKey"] ?? "");
    client.DefaultRequestHeaders.Add("apiSecret", config["ApiSecret"] ?? "");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// ── Application Services ──
builder.Services.AddScoped<IFortressProjectsClient, FortressProjectsClient>();
builder.Services.AddScoped<FormExtractionService>();
builder.Services.AddScoped<CrossReferenceService>();
builder.Services.AddScoped<GeneratorService>();

// ── AWS S3 (for PDF storage — uses ECS task role in production) ──
var s3BucketName = builder.Configuration["S3:BucketName"];
if (!string.IsNullOrEmpty(s3BucketName))
{
    var s3Region = builder.Configuration["S3:Region"] ?? "us-east-1";
    builder.Services.AddSingleton<IAmazonS3>(sp =>
        new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(s3Region)));
    Console.WriteLine($"S3 storage enabled: bucket={s3BucketName}, region={s3Region}");
}
else
{
    Console.WriteLine("S3 storage not configured — using local file storage.");
}

// ── AWS Bedrock Runtime (for Claude AI calls via SDK — uses ECS task role in production) ──
builder.Services.AddSingleton<Amazon.BedrockRuntime.IAmazonBedrockRuntime>(sp =>
    new Amazon.BedrockRuntime.AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.USEast1));

// ── Background processing ──
builder.Services.AddSingleton<ExtractionBackgroundService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ExtractionBackgroundService>());

var app = builder.Build();
var logger = app.Logger;

// ── Database migration — run in background so HTTP server starts immediately ──
// This prevents health check failures when DB is slow to connect on first boot.
_ = Task.Run(async () =>
{
    await Task.Delay(5000); // brief delay to let DI container finish initialization
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    try
    {
        // Check if tables already exist
        bool tablesExist = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM FormLibraries LIMIT 1");
            tablesExist = true;
        }
        catch (Exception ex)
        {
            // Table doesn't exist (expected on first run) or query failed — safe to assume migration needed
            logger.LogDebug(ex, "Table probe failed — assuming tables don't exist");
        }

        if (!tablesExist)
        {
            try
            {
                var creator = db.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync();
                Console.WriteLine("Database tables created successfully.");
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number == 1050)
            {
                // Table already exists — safe to ignore on re-run
                logger.LogDebug("CreateTablesAsync: some tables already exist (1050), continuing");
            }
        }
        else
        {
            Console.WriteLine("DB tables already exist.");
        }

        // Ensure form_projects table exists (idempotent — safe on existing DBs where EnsureCreated is a no-op)
        // Must run BEFORE ALTER TABLE statements that reference ProjectId on related tables.
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS form_projects (
                    Id INT NOT NULL AUTO_INCREMENT,
                    Name VARCHAR(200) NOT NULL,
                    Vertical VARCHAR(50) NOT NULL DEFAULT 'general',
                    Description VARCHAR(500) NULL,
                    Status VARCHAR(20) NOT NULL DEFAULT 'draft',
                    CreatedBy VARCHAR(100) NULL,
                    CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    UpdatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (Id),
                    INDEX idx_form_projects_status (Status),
                    INDEX idx_form_projects_created (CreatedAt)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
            Console.WriteLine("form_projects table ensured.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create form_projects table — cannot continue startup");
            throw;
        }

        // Ensure FormFieldCodes table exists (Sprint 3 — cross-reference engine)
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS FormFieldCodes (
                    Id INT NOT NULL AUTO_INCREMENT,
                    ProjectId INT NOT NULL,
                    FieldCode VARCHAR(100) NOT NULL,
                    FieldLabel VARCHAR(300) NOT NULL,
                    FieldType VARCHAR(50) NOT NULL DEFAULT 'text',
                    IsSensitive TINYINT(1) NOT NULL DEFAULT 0,
                    IsShared TINYINT(1) NOT NULL DEFAULT 0,
                    PanelId VARCHAR(100) NULL,
                    CarrierSources LONGTEXT NULL,
                    IsRequired TINYINT(1) NOT NULL DEFAULT 0,
                    SortOrder INT NOT NULL DEFAULT 0,
                    SectionName VARCHAR(200) NULL,
                    CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (Id),
                    INDEX idx_FormFieldCodes_project (ProjectId),
                    UNIQUE INDEX idx_FormFieldCodes_code (ProjectId, FieldCode)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
            Console.WriteLine("FormFieldCodes table ensured.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create FormFieldCodes table");
            throw;
        }

        // Ensure DataProtectionKeys table exists (needed for antiforgery key persistence)
        try
        {
            await db.Database.ExecuteSqlRawAsync(@"
                CREATE TABLE IF NOT EXISTS DataProtectionKeys (
                    Id INT NOT NULL AUTO_INCREMENT,
                    FriendlyName LONGTEXT NULL,
                    Xml LONGTEXT NULL,
                    PRIMARY KEY (Id)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
            Console.WriteLine("DataProtectionKeys table ensured.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create DataProtectionKeys table");
            throw;
        }

        // Add ProjectId columns to existing tables (idempotent)
        var alterStatements = new[]
        {
            "ALTER TABLE FormLibraries ADD COLUMN ProjectId INT NULL",
            "ALTER TABLE FormLibraries ADD INDEX idx_FormLibraries_project (ProjectId)",
            "ALTER TABLE QuestionSets ADD COLUMN ProjectId INT NULL",
            "ALTER TABLE QuestionSets ADD INDEX idx_QuestionSets_project (ProjectId)",
            "ALTER TABLE FormLibraries ADD COLUMN DocumentType VARCHAR(50) NULL",
            "ALTER TABLE FormLibraries ADD COLUMN ApprovedAt DATETIME(6) NULL",
        };

        foreach (var sql in alterStatements)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number == 1060 || ex.Number == 1061)
            {
                // Column/index already exists — safe to ignore
                logger.LogDebug("ALTER TABLE already applied (idempotent): {Message}", ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Schema migration failed for: {Sql}", sql);
                throw;
            }
        }
    }
    catch (Exception ex)
    {
        logger.LogCritical(ex, "DATABASE INITIALIZATION FAILED — app will not function correctly");
        throw;
    }
});
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "uploads"));

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

app.MapControllers();

// ── Health check endpoint (ALB health probe — no DB dependency) ──
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })).AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
}).AllowAnonymous();

app.MapGet("/auth/login", async (HttpContext ctx) =>
{
    await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme, new AuthenticationProperties
    {
        RedirectUri = "/"
    });
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine("═══════════════════════════════════════════════════");
Console.WriteLine("  Fortress Form Tools — Form Intelligence Platform");
Console.WriteLine("  Running at: http://localhost:5200");
Console.WriteLine("═══════════════════════════════════════════════════");

app.Run();
