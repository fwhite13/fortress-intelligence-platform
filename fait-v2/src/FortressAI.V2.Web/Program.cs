using Amazon.ECS;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using MudBlazor.Services;
using Serilog;
using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Components;
using FortressAI.V2.Web.Components.Hubs;
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

// FIP shared cookie consumer — no independent OIDC
// fip.fortressam.ai owns Entra auth; fait-v2 reads the shared .FortressAI.Session cookie
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
    options.Cookie.Name = builder.Configuration["Auth:CookieName"] ?? ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});

// DataProtection: shared key ring points to fred_dev (FIP portal's DB)
// fait-v2 is a consumer — DisableAutomaticKeyGeneration so only FIP portal creates keys
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
    ConnectionTimeout = 10,
    GuidFormat = MySqlConnector.MySqlGuidFormat.None   // MANDATORY — matches existing FIRM pattern
};

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "FortressAI")
    .DisableAutomaticKeyGeneration(); // fait-v2 is a consumer — FIP portal creates keys

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// MudBlazor
builder.Services.AddMudServices();

// AWS S3 (workspace bucket)
builder.Services.AddAWSService<IAmazonS3>();

// AWS ECS (per-user Fargate runtime)
builder.Services.AddSingleton<IAmazonECS>(sp => new AmazonECSClient(Amazon.RegionEndpoint.USEast1));
builder.Services.AddHttpClient("HarnessClient");
// FaitV2DbContext — main app DB (fait_v2_dev on Aurora MySQL)
// Built from FORTRESS_DB_* env vars, consistent with keyring and fipPortal patterns
var faitV2Csb = new MySqlConnector.MySqlConnectionStringBuilder
{
    Server = keyRingDbHost ?? "localhost",
    Port = uint.Parse(keyRingDbPort),
    Database = builder.Configuration["FORTRESS_DB_NAME"] ?? "fait_v2_dev",
    UserID = keyRingDbUser,
    Password = keyRingDbPass,
    ConnectionTimeout = 10,
    GuidFormat = MySqlConnector.MySqlGuidFormat.None   // MANDATORY — matches existing patterns
};
builder.Services.AddDbContextFactory<FaitV2DbContext>(options =>
    options.UseMySql(
        faitV2Csb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(3)
    ));
builder.Services.AddScoped<IUserAgentRuntime, FargateUserAgentRuntime>();

// User provisioning
builder.Services.AddScoped<IUserProvisioningService, UserProvisioningService>();
// Memory file service
builder.Services.AddScoped<IMemoryFileService, MemoryFileService>();

// FIP portal DB — read delegated Entra tokens written at login (fip_dev.user_microsoft_tokens)
var fipPortalDbName = builder.Configuration["FIP_DB_NAME"] ?? "fip_dev";
var fipPortalCsb = new MySqlConnector.MySqlConnectionStringBuilder
{
    Server = keyRingDbHost ?? "localhost",
    Port = uint.Parse(keyRingDbPort),
    Database = fipPortalDbName,
    UserID = keyRingDbUser,
    Password = keyRingDbPass,
    ConnectionTimeout = 10,
    GuidFormat = MySqlConnector.MySqlGuidFormat.None
};
builder.Services.AddDbContextFactory<FipPortalDbContext>(options =>
    options.UseMySql(fipPortalCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

// FORGE KB / fip-mcp integration
builder.Services.AddHttpClient("FipMcpClient");
builder.Services.AddScoped<IFipTokenProvider, FipTokenProvider>();
builder.Services.AddScoped<IForgeKbService, ForgeKbService>();

// Design Agent
builder.Services.AddScoped<IDesignAgentService, DesignAgentService>();

// Connector management
builder.Services.AddScoped<IConnectorService, ConnectorService>();

// CC execution service (child process orchestration)
builder.Services.AddScoped<ICCExecutionService, FargateCCExecutionService>();

// Project service (FAIT v1 carry-over)
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<ProjectStateService>();

var app = builder.Build();

// Seed mcp_servers with forge-kb entry (idempotent)
using (var seedScope = app.Services.CreateScope())
{
    var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<FaitV2DbContext>>();
    var cfg = seedScope.ServiceProvider.GetRequiredService<IConfiguration>();
    var seedLogger = seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await using var seedDb = await dbFactory.CreateDbContextAsync();
    var endpointUrl = cfg["FipMcp:EndpointUrl"] ?? "https://api.fortressam.ai/mcp";
    var exists = await seedDb.McpServers.AnyAsync(s => s.Name == "forge-kb");
    if (!exists)
    {
        seedDb.McpServers.Add(new FortressAI.V2.Web.Data.Models.McpServer
        {
            Id = Guid.NewGuid().ToString(),
            Name = "forge-kb",
            EndpointUrl = endpointUrl,
            AuthType = "none",
            DefaultRead = true,
            DefaultWrite = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await seedDb.SaveChangesAsync();
        seedLogger.LogInformation("Seeded forge-kb mcp_servers entry");
    }
    else
    {
        // Update endpoint URL and auth_type if config changed
        var server = await seedDb.McpServers.FirstAsync(s => s.Name == "forge-kb");
        bool changed = false;
        if (server.EndpointUrl != endpointUrl)
        {
            server.EndpointUrl = endpointUrl;
            changed = true;
        }
        if (server.AuthType != "none")
        {
            server.AuthType = "none";
            changed = true;
        }
        if (changed)
        {
            await seedDb.SaveChangesAsync();
            seedLogger.LogInformation("Updated forge-kb mcp_servers entry");
        }
    }
}

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

// CC progress hub
app.MapHub<CCProgressHub>("/hubs/cc-progress");

// Health endpoint — public
app.MapGet("/health", () => "OK").AllowAnonymous();

// Stub: returns Starting until a future WI wires IUserAgentRuntime.GetSessionAsync()
app.MapGet("/api/agent/status", () => Results.Ok(new { status = "Starting" })).AllowAnonymous();

// Redirect unauthenticated users to FIP portal for login
app.MapGet("/auth/redirect-to-login", (IConfiguration cfg, HttpContext ctx) =>
{
    var fipUrl = cfg["FIP__LoginUrl"]?.TrimEnd('/') ?? "https://fip.dev.fortressam.ai";
    var returnUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/";
    return Results.Redirect($"{fipUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
}).AllowAnonymous();

// Blazor components — all routes require auth
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly)
   .RequireAuthorization();

app.Run();
