using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using MySqlConnector;
using Amazon.BedrockAgentRuntime;
using Amazon.BedrockRuntime;
using Amazon.Rekognition;
using MudBlazor.Services;
using FortressAI.Web.Data;
using FortressAI.Web.Services;
using FortressAI.Web.Hubs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.SignalR;
using FortressAI.Web.Auth;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using FortressAI.Web.Data.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Database — prefer env vars (ECS/Aurora) over appsettings
var dbHost = builder.Configuration["FORTRESS_DB_HOST"];
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "dev";
var dbName = builder.Configuration["FRED_DB_NAME"] ?? "fred_dev";
string fredConnectionString;
if (!string.IsNullOrEmpty(dbHost))
{
    // Use MySqlConnectionStringBuilder to safely handle special chars in password
    var csb = new MySqlConnectionStringBuilder
    {
        Server = dbHost,
        Port = uint.Parse(dbPort),
        Database = dbName,
        UserID = dbUser,
        Password = dbPass,
        ConnectionTimeout = 10,
        GuidFormat = MySqlGuidFormat.None
    };
    fredConnectionString = csb.ConnectionString;
    Console.WriteLine($"Using Aurora MySQL: {dbHost}/{dbName}");
}
else
{
    fredConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Server=localhost;Database=fred_dev;User=root;Password=dev;";
    Console.WriteLine("Using local connection string.");
}
var fredServerVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(fredConnectionString, fredServerVersion,
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));

// Application services
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<ProjectService>();
builder.Services.AddScoped<HelpService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<DocumentService>();
builder.Services.AddScoped<ChatAttachmentService>();
builder.Services.AddSingleton<BedrockService>();
builder.Services.AddScoped<AssistantConfigService>();
builder.Services.AddScoped<UserProvisioningService>();
builder.Services.AddScoped<BriefingService>();
builder.Services.AddScoped<BriefingGenerationService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("InternalPreview", c => { c.BaseAddress = new Uri("http://localhost:8080/"); });
builder.Services.AddScoped<MicrosoftTokenService>();
builder.Services.AddScoped<DevOpsConnectionService>();
builder.Services.AddScoped<DevOpsToolService>();
builder.Services.AddScoped<GraphTaskService>();
builder.Services.AddScoped<GraphCalendarService>();
builder.Services.AddScoped<PreMeetingBriefService>();
builder.Services.AddScoped<PostMeetingService>();
builder.Services.AddScoped<IScheduledTaskService, ScheduledTaskService>();
builder.Services.AddHostedService<ScheduledTaskBackgroundService>();
builder.Services.AddScoped<ITaskNotificationService, TaskNotificationService>();
builder.Services.AddScoped<IMemoryFileService, MemoryFileService>();
builder.Services.AddScoped<IWorkspaceFileService, WorkspaceFileService>();
builder.Services.AddScoped<IWorkspaceUploadService, WorkspaceUploadService>();
builder.Services.AddScoped<ArtifactPreviewService>();
builder.Services.AddScoped<ChatLayoutState>();
builder.Services.AddSingleton<IDocumentGeneratorService, WordDocumentGenerator>();
builder.Services.AddScoped<FeedbackDispatcher>();

// Knowledge Base
builder.Services.AddSingleton<IAmazonBedrockAgentRuntime>(sp =>
    new AmazonBedrockAgentRuntimeClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));

// KB Document upload (S3) and ingestion trigger (BedrockAgent admin)
builder.Services.AddSingleton<Amazon.S3.IAmazonS3>(sp =>
    new Amazon.S3.AmazonS3Client(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddSingleton<Amazon.BedrockAgent.IAmazonBedrockAgent>(sp =>
    new Amazon.BedrockAgent.AmazonBedrockAgentClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddSingleton<Amazon.SecretsManager.IAmazonSecretsManager>(sp =>
    new Amazon.SecretsManager.AmazonSecretsManagerClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));

builder.Services.AddScoped<KnowledgeBaseService>();
builder.Services.AddScoped<KbDocumentService>();
builder.Services.AddSingleton<KbSyncRetryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<KbSyncRetryService>());
builder.Services.AddScoped<ForgeService>();
builder.Services.AddScoped<ForgeQueryService>();
builder.Services.AddSingleton<KbQueryService>();
builder.Services.AddSingleton<IAmazonBedrockRuntime>(sp =>
    new AmazonBedrockRuntimeClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddSingleton<IAmazonRekognition>(sp =>
    new AmazonRekognitionClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddSingleton<ContentModerationService>();

// Phase 2: Email Intelligence services
builder.Services.AddScoped<GraphWebhookService>();
builder.Services.AddScoped<EmailAlertService>();
builder.Services.AddSingleton<EmailClassifierService>();

// Phase 5: Weekly Rollup
builder.Services.AddScoped<WeeklyRollupService>();

// SQS client (optional — only used if AWS:SQS:EmailEventsQueue is configured)
var sqsQueue = builder.Configuration["AWS:SQS:EmailEventsQueue"];
if (!string.IsNullOrEmpty(sqsQueue))
{
    builder.Services.AddSingleton<Amazon.SQS.IAmazonSQS>(sp =>
        new Amazon.SQS.AmazonSQSClient(
            Amazon.RegionEndpoint.GetBySystemName(
                builder.Configuration["AWS:Region"] ?? "us-east-1")));
}

// Add controllers for API endpoints (webhooks, email)
builder.Services.AddControllers();

builder.Services.AddSignalR();
builder.Services.AddMudServices();

// Authentication: pure cookie consumer — FIP portal owns Entra OIDC.
// FAIT reads the shared .FortressAI.Session cookie issued by FIP.
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
    options.Events.OnRedirectToLogin = ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/api"))
        {
            ctx.Response.StatusCode = 401;
            return Task.CompletedTask;
        }
        ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
})
.AddScheme<AppKeyAuthOptions, AppKeyAuthHandler>("AppKeyAuth", options =>
{
    options.ApiKey  = builder.Configuration["AppKeys:Haven"];
    options.ApiKeys = new List<string>
    {
        builder.Configuration["AppKeys:ExcelAddin"] ?? ""
    };
})
// NEW: Entra JWT Bearer for FfE
.AddJwtBearer("EntraBearer", options =>
{
    var tenantId = builder.Configuration["Azure:TenantId"]
                   ?? "d2bf3425-f8ab-451c-83bd-1e0ebd9508fe";
    var clientId = builder.Configuration["Azure:ClientId"]
                   ?? "887206bc-fac1-436a-a8ed-2150418d76c0";
    options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
    options.Audience  = $"api://{clientId}";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer   = true,
        ValidIssuer      = $"https://login.microsoftonline.com/{tenantId}/v2.0",
        ValidateAudience = true,
        ValidateLifetime = true,
        ClockSkew        = TimeSpan.FromMinutes(5),
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async ctx =>
        {
            // Map the Entra user to their FAIT AppUser.Id for personal KB scoping
            // Entra tokens use 'preferred_username' for email and 'oid' for the object ID
            var email = ctx.Principal?.FindFirst("preferred_username")?.Value
                        ?? ctx.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? "";
            if (string.IsNullOrEmpty(email)) return;

            var dbFactory = ctx.HttpContext.RequestServices
                .GetRequiredService<IDbContextFactory<FortressAI.Web.Data.AppDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var oidClaim = ctx.Principal?.FindFirst("oid")?.Value
                ?? ctx.Principal?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

            var user = await db.Users.FirstOrDefaultAsync(
                u => u.IsEntraUser && u.Email == email);

            if (user != null)
            {
                // Backfill EntraOid if missing (users created before ADO#1240)
                if (user.EntraOid == null && oidClaim != null)
                {
                    user.EntraOid = oidClaim;
                    await db.SaveChangesAsync();
                }
                // Inject the FAIT userId as NameIdentifier so controllers get the right userId
                var identity = ctx.Principal!.Identity as System.Security.Claims.ClaimsIdentity;
                var existing = identity?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
                if (existing != null) identity?.RemoveClaim(existing);
                identity?.AddClaim(new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier,
                    user.Id.ToString()));
            }
            // If user not found, leave claims as-is; whoami endpoint will provision them
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
    options.AddPolicy("AppKeyOnly", policy =>  // Keep for backward compat
        policy.AddAuthenticationSchemes("AppKeyAuth")
              .RequireAuthenticatedUser());
    options.AddPolicy("ExcelAddinAccess", policy =>  // NEW — used by HavenChat + ExcelAddin
        policy.AddAuthenticationSchemes("AppKeyAuth", "EntraBearer")
              .RequireAuthenticatedUser());
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<Amazon.CognitoIdentityProvider.IAmazonCognitoIdentityProvider>(sp =>
    new Amazon.CognitoIdentityProvider.AmazonCognitoIdentityProviderClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddScoped<IUserManagementService, UserManagementService>();

builder.Services.AddHostedService<DatabaseInitializationService>();

// Fargate agent runtime
builder.Services.AddSingleton<Amazon.ECS.IAmazonECS>(sp =>
    new Amazon.ECS.AmazonECSClient(
        Amazon.RegionEndpoint.GetBySystemName(
            builder.Configuration["AWS:Region"] ?? "us-east-1")));
builder.Services.AddSingleton<IUserAgentRuntime, FargateUserAgentRuntime>();

// MCP services
builder.Services.AddScoped<IMcpRegistryService, McpRegistryService>();
builder.Services.AddScoped<McpTokenRefreshService>();
builder.Services.AddScoped<IMcpConnectionService, McpConnectionService>();
builder.Services.AddScoped<IMcpToolService, McpToolService>();
builder.Services.AddSingleton<McpHttpTransport>();
builder.Services.AddSingleton<BraveSearchClient>();
builder.Services.AddSingleton<IWebFetchClient, WebFetchClient>();
builder.Services.AddHostedService<ManifestRefreshService>();
builder.Services.AddHostedService<BriefingSchedulerService>();
builder.Services.AddMemoryCache();

// Named HttpClient for WebFetch — redirects handled manually in WebFetchClient (SSRF re-validation)
builder.Services.AddHttpClient("WebFetch")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        AllowAutoRedirect = false,       // redirects handled manually in WebFetchClient
        MaxAutomaticRedirections = 3     // no-op with AllowAutoRedirect=false, documents intent
    });

// Named HttpClient for DevOps test connection — short timeout so bad org URL fails fast
builder.Services.AddHttpClient("devops-test", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Named HttpClient for Azure DevOps REST API calls (DevOpsToolService)
builder.Services.AddHttpClient("azure-devops", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Named HttpClient for MCP transport with 30s timeout
builder.Services.AddHttpClient("mcp-transport", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Named HttpClient for Microsoft Graph API calls (M365McpAdapter)
builder.Services.AddHttpClient("graph", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Named HttpClient for Fargate harness communication — long timeout for SSE streaming
builder.Services.AddHttpClient("HarnessClient", client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
});

// DataProtection: shared key ring via MySQL — same DataProtectionKeys table as FIP.
// FAIT is a consumer only — DisableAutomaticKeyGeneration so only FIP creates keys.
// FIP_KEYRING_DB_NAME env var = fait_dev (set in ECS task def).
var keyRingDbHost = builder.Configuration["FORTRESS_DB_HOST"];
var keyRingDbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var keyRingDbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var keyRingDbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "";
var keyRingDbName = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fait_dev";

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

// FAIT is a consumer — DisableAutomaticKeyGeneration so only FIP portal creates keys
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName(builder.Configuration["DataProtection:ApplicationName"] ?? "FortressAI")
    .DisableAutomaticKeyGeneration();

// ⚠️ TEST AUTH — DEVELOPMENT ONLY — MUST NOT REACH PRODUCTION
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSingleton<TestAuthService>();
    builder.Services.AddRateLimiter(options =>
    {
        options.AddFixedWindowLimiter("test-auth", policy =>
        {
            policy.PermitLimit = 10;
            policy.Window = TimeSpan.FromMinutes(1);
        });
    });
}

var app = builder.Build();

// Fail fast on missing PREVIEW_TOKEN_SECRET — do not wait for first request
var previewSecret = app.Configuration["PREVIEW_TOKEN_SECRET"];
if (string.IsNullOrWhiteSpace(previewSecret))
    throw new InvalidOperationException(
        "PREVIEW_TOKEN_SECRET must be configured before startup. Set this environment variable in the ECS task definition.");

// ⚠️ TEST AUTH — DEVELOPMENT ONLY
if (app.Environment.IsDevelopment())
{
    app.UseRateLimiter();
}

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

// Serve /excel-addin/ static files publicly (Office Add-in — no auth required)
// Must use MapGet + AllowAnonymous because FallbackPolicy=DefaultPolicy intercepts UseStaticFiles
app.MapGet("/excel-addin/{**path}", async (HttpContext ctx, string? path) =>
{
    var webRoot = ctx.RequestServices.GetRequiredService<IWebHostEnvironment>().WebRootPath;
    var filePath = string.IsNullOrEmpty(path)
        ? Path.Combine(webRoot, "excel-addin", "index.html")
        : Path.Combine(webRoot, "excel-addin", path.Replace("/", Path.DirectorySeparatorChar.ToString()));

    if (!File.Exists(filePath))
        return Results.NotFound();

    var contentType = Path.GetExtension(filePath) switch
    {
        ".html" => "text/html",
        ".js" => "application/javascript",
        ".css" => "text/css",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".json" => "application/json",
        ".xml" => "application/xml",
        _ => "application/octet-stream"
    };

    return Results.File(filePath, contentType);
}).AllowAnonymous();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Health endpoint (must be before other middleware)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow })).AllowAnonymous();

// FIP mode: redirect unauthenticated users to FIP portal for login
// LoginPath = /auth/redirect-to-login when FIP__LoginUrl is set
// Passes returnUrl so FIP redirects back to FAIT after authentication
app.MapGet("/auth/redirect-to-login", (HttpContext ctx) =>
{
    var config = ctx.RequestServices.GetRequiredService<IConfiguration>();
    var fipLoginUrl = config["FIP:LoginUrl"]?.TrimEnd('/') ?? "https://fip.dev.fortressam.ai";
    var faitCallbackUrl = config["FIP:FaitCallbackUrl"]?.TrimEnd('/') ?? "https://fait.fortressam.ai/auth/fait-session";
    var redirectUrl = $"{fipLoginUrl}/auth/firm-callback?returnUrl={Uri.EscapeDataString(faitCallbackUrl)}";
    return Results.Redirect(redirectUrl);
}).AllowAnonymous().DisableAntiforgery();

// FAIT session endpoint — user arrives here from FIP after successful Entra authentication
// The shared .FortressAI.Session cookie is already set by FIP — just validate and redirect to app
app.MapGet("/auth/fait-session", async (HttpContext ctx) =>
{
    var authResult = await ctx.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    if (!authResult.Succeeded)
        return Results.Redirect("/");
    // Cookie is already set by FIP portal — just redirect to home
    return Results.Redirect("/");
}).AllowAnonymous().DisableAntiforgery();

// FIRM auth callback — after user logs into FAIT, redirect back to FIRM
// Only redirects to *.fortressam.ai domains for safety
app.MapGet("/auth/firm-callback", (HttpContext ctx, IConfiguration config) =>
{
    var returnUrl = ctx.Request.Query["returnUrl"].FirstOrDefault();
    // Validate returnUrl — only allow redirects to fortressam.ai subdomains
    if (!string.IsNullOrEmpty(returnUrl) &&
        Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.EndsWith(".fortressam.ai", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("fortressam.ai", StringComparison.OrdinalIgnoreCase)))
    {
        ctx.Response.Redirect(returnUrl);
    }
    else
    {
        ctx.Response.Redirect("/");
    }
    return Task.CompletedTask;
}).AllowAnonymous().DisableAntiforgery();

// Microsoft OAuth callback endpoint
// Registered under both paths: /auth/microsoft-callback (legacy) and /auth/ms-callback (matches ECS MicrosoftGraph__RedirectUri)
Func<HttpContext, IDbContextFactory<AppDbContext>, IHttpClientFactory, IConfiguration, Task<IResult>> msCallbackHandler = async (ctx, dbFactory, httpFactory, config) =>
{
    var code = ctx.Request.Query["code"].ToString();
    var state = ctx.Request.Query["state"].ToString();
    var error = ctx.Request.Query["error"].ToString();

    if (!string.IsNullOrEmpty(error))
    {
        var errorDesc = ctx.Request.Query["error_description"].ToString();
        return Results.Content(
            "<html><body style='font-family:sans-serif;text-align:center;padding:60px;'>" +
            "<h1 style='color:#dc2626;'>Authentication Failed</h1>" +
            $"<p>{error}: {errorDesc}</p>" +
            "<p><a href='/settings'>Back to Settings</a></p>" +
            "</body></html>", "text/html");
    }

    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
    {
        return Results.Content(
            "<html><body style='font-family:sans-serif;text-align:center;padding:60px;'>" +
            "<h1 style='color:#dc2626;'>Invalid Response</h1>" +
            "<p>No authorization code received.</p>" +
            "<p><a href='/settings'>Back to Settings</a></p>" +
            "</body></html>", "text/html");
    }

    // Parse state: "userId:random"
    var stateParts = state.Split(':');
    if (stateParts.Length < 2 || !Guid.TryParse(stateParts[0], out var userId))
    {
        return Results.Content(
            "<html><body style='font-family:sans-serif;text-align:center;padding:60px;'>" +
            "<h1 style='color:#dc2626;'>Invalid State</h1>" +
            "<p><a href='/settings'>Back to Settings</a></p>" +
            "</body></html>", "text/html");
    }

    try
    {
        // Create a MicrosoftTokenService instance
        var logger = ctx.RequestServices.GetRequiredService<ILogger<MicrosoftTokenService>>();
        var tokenService = new MicrosoftTokenService(dbFactory, logger, config, httpFactory);
        // Use configured redirect URI — must match exactly what's registered in Azure app registration
        var redirectUri = config["MicrosoftGraph:RedirectUri"]
            ?? $"{ctx.Request.Scheme}://{ctx.Request.Host}/auth/microsoft-callback";
        var token = await tokenService.ExchangeCodeAsync(userId, code, redirectUri);

        var email = token.MicrosoftEmail ?? "user";
        return Results.Content(
            "<html><body style='font-family:sans-serif;text-align:center;padding:60px;'>" +
            "<h1 style='color:#059669;'>&#x2705; Microsoft 365 Connected!</h1>" +
            $"<p>Successfully connected as <strong>{email}</strong></p>" +
            "<p>Redirecting to Settings...</p>" +
            "<script>setTimeout(function(){ window.location.href = '/settings'; }, 2000);</script>" +
            "</body></html>", "text/html");
    }
    catch (Exception ex)
    {
        return Results.Content(
            "<html><body style='font-family:sans-serif;text-align:center;padding:60px;'>" +
            "<h1 style='color:#dc2626;'>Connection Failed</h1>" +
            $"<p>{ex.Message}</p>" +
            "<p><a href='/settings'>Back to Settings</a></p>" +
            "</body></html>", "text/html");
    }
};

app.MapGet("/auth/microsoft-callback", msCallbackHandler).AllowAnonymous().DisableAntiforgery();
app.MapGet("/auth/ms-callback", msCallbackHandler).AllowAnonymous().DisableAntiforgery();

app.MapGet("/api/agent/status", async (IUserAgentRuntime runtime, System.Security.Claims.ClaimsPrincipal user) =>
{
    var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();
    var session = await runtime.GetSessionAsync(userId);
    return Results.Ok(new { status = session?.Status.ToString() ?? "Stopped" });
}).RequireAuthorization();

// API endpoint for Lambda to get user access token
app.MapGet("/api/tokens/{userId}", async (HttpContext context, string userId, IDbContextFactory<AppDbContext> dbFactory, IHttpClientFactory httpFactory, IConfiguration config) =>
{
    if (!(context.User.Identity?.IsAuthenticated ?? false)) return Results.Unauthorized();
    var claimUserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (claimUserId != userId) return Results.Forbid();

    if (!Guid.TryParse(userId, out var userGuid))
        return Results.BadRequest("Invalid user ID");

    var logger = LoggerFactory.Create(b => b.AddConsole()).CreateLogger<MicrosoftTokenService>();
    var tokenService = new MicrosoftTokenService(dbFactory, logger, config, httpFactory);
    var accessToken = await tokenService.GetValidAccessTokenAsync(userGuid);

    if (accessToken == null)
        return Results.NotFound(new { error = "No valid token. User must re-authenticate." });

    return Results.Ok(new { accessToken });
}).RequireAuthorization();

// Internal endpoint for harness — get both ms365 access token and ADO PAT for a user
app.MapGet("/api/internal/user-tokens/{userId}", async (HttpContext context, string userId,
    DevOpsConnectionService devopsConn, MicrosoftTokenService msTokenSvc,
    IConfiguration config, IDbContextFactory<AppDbContext> dbFactory, ILogger<Program> logger) =>
{
    // Validate X-Internal-Token
    var expectedToken = config["INTERNAL_API_TOKEN"];
    if (string.IsNullOrEmpty(expectedToken))
    {
        logger.LogWarning("InternalToken validation: FAIL userId={UserId} — INTERNAL_API_TOKEN not configured (503)", userId);
        return Results.StatusCode(503);
    }

    var incomingToken = context.Request.Headers.TryGetValue("X-Internal-Token", out var header) ? header.ToString() : string.Empty;
    var maskedIncoming = incomingToken.Length >= 8 ? incomingToken[..8] + "..." : (incomingToken.Length > 0 ? "***" : "(empty)");

    if (incomingToken != expectedToken)
    {
        logger.LogWarning("InternalToken validation: FAIL userId={UserId} — token mismatch, incoming={MaskedToken}", userId, maskedIncoming);
        return Results.Unauthorized();
    }

    logger.LogInformation("InternalToken validation: PASS userId={UserId} token={MaskedToken}", userId, maskedIncoming);

    if (!Guid.TryParse(userId, out var userGuid))
    {
        logger.LogWarning("InternalToken user-tokens: invalid userId format userId={UserId}", userId);
        return Results.BadRequest(new { error = "Invalid userId" });
    }

    logger.LogInformation("InternalToken user-tokens: looking up tokens for userId={UserId}", userId);

    // Get MS365 access token
    string? ms365AccessToken = null;
    try { ms365AccessToken = await msTokenSvc.GetValidAccessTokenAsync(userGuid); }
    catch { /* non-fatal */ }

    // Get ADO PAT
    string? adoPat = null;
    try { adoPat = await devopsConn.GetDecryptedPatAsync(userGuid); }
    catch { /* non-fatal */ }

    return Results.Ok(new
    {
        ms365AccessToken,
        adoPersonalAccessToken = adoPat
    });
}).AllowAnonymous().DisableAntiforgery();

// ADO#4144 — persist working folder selection for a conversation
app.MapPost("/api/internal/conversation/{conversationId}/working-folder", async (
    HttpContext context,
    string conversationId,
    ChatService chatSvc,
    IConfiguration config) =>
{
    // Validate internal token
    var expectedToken = config["INTERNAL_API_TOKEN"] ?? "";
    var providedToken = context.Request.Headers["X-Internal-Token"].FirstOrDefault() ?? "";
    if (!string.IsNullOrEmpty(expectedToken) && providedToken != expectedToken)
        return Results.Unauthorized();

    if (!Guid.TryParse(conversationId, out var convId))
        return Results.BadRequest(new { error = "Invalid conversationId" });

    WorkingFolderRequest? body;
    try { body = await context.Request.ReadFromJsonAsync<WorkingFolderRequest>(); }
    catch { return Results.BadRequest(new { error = "Invalid JSON body" }); }

    Guid? folderId = null;
    if (!string.IsNullOrEmpty(body?.FolderId) && Guid.TryParse(body.FolderId, out var parsed))
        folderId = parsed;

    await chatSvc.UpdateConversationWorkingFolderAsync(convId, folderId);
    return Results.Ok(new { success = true });
}).AllowAnonymous().DisableAntiforgery();

// Internal endpoint for harness — get decrypted ADO PAT for a user
app.MapGet("/api/internal/devops-pat/{userId}", async (HttpContext context, string userId, DevOpsConnectionService devopsConn, IConfiguration config) =>
{
    // Validate X-Internal-Token
    var expectedToken = config["INTERNAL_API_TOKEN"];
    if (string.IsNullOrEmpty(expectedToken)) return Results.StatusCode(503);
    if (!context.Request.Headers.TryGetValue("X-Internal-Token", out var header) || header.ToString() != expectedToken)
        return Results.Unauthorized();

    if (!Guid.TryParse(userId, out var userGuid))
        return Results.BadRequest(new { error = "Invalid userId" });

    var pat = await devopsConn.GetDecryptedPatAsync(userGuid);
    if (pat == null)
        return Results.NotFound(new { error = "No ADO connection found for user" });

    return Results.Ok(new { pat });
}).AllowAnonymous().DisableAntiforgery();

// Manual briefing generation trigger — invokes briefing-builder Lambda
app.MapPost("/api/briefing/generate", async (HttpContext ctx, BriefingGenerationService briefingGen, IHubContext<DashboardHub> hubContext) =>
{
    // Authentication check
    if (!(ctx.User.Identity?.IsAuthenticated ?? false))
        return Results.Unauthorized();

    var userIdStr = ctx.Request.Query["userId"].ToString();

    // Authorization check - can only generate own briefing
    var claimUserId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (claimUserId != userIdStr)
        return Results.Forbid();

    if (!Guid.TryParse(userIdStr, out var userId))
        return Results.BadRequest("Invalid userId");

    var (success, briefing, error) = await briefingGen.GenerateBriefingAsync(userId);

    if (!success)
        return Results.Problem($"Briefing generation failed: {error}");

    // Push via SignalR so connected dashboard tabs update immediately
    if (briefing != null)
    {
        await hubContext.Clients.Group($"user-{userId}").SendAsync("ReceiveBriefing", briefing.Content);
    }

    return Results.Ok(new { message = "Briefing generated", briefingId = briefing?.Id });
}).RequireAuthorization();

// Auth endpoints
// /auth/login and /auth/login-entra — redirects to FIP (FAIT is a pure cookie consumer)
app.MapGet("/auth/login", ctx => { ctx.Response.Redirect("/auth/redirect-to-login"); return Task.CompletedTask; })
    .AllowAnonymous();
app.MapGet("/auth/login-entra", ctx => { ctx.Response.Redirect("/auth/redirect-to-login"); return Task.CompletedTask; })
    .AllowAnonymous();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).AllowAnonymous().DisableAntiforgery();

// Submit feedback (authenticated user)
app.MapPost("/api/feedback", async (
    [FromBody] FeedbackRequest feedbackRequest,
    IDbContextFactory<AppDbContext> dbFactory,
    FeedbackDispatcher feedbackDispatcher,
    System.Security.Claims.ClaimsPrincipal user,
    CancellationToken ct) =>
{
    var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var submission = new FeedbackSubmission
    {
        UserId = userId,
        Type = feedbackRequest.Type,
        Description = feedbackRequest.Description,
        PageUrl = feedbackRequest.PageUrl,
        Status = "pending",
    };

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    db.FeedbackSubmissions.Add(submission);
    await db.SaveChangesAsync(ct);

    _ = feedbackDispatcher.DispatchToJarvisAsync(submission);

    return Results.Ok(new { submissionId = submission.Id });
}).AllowAnonymous().DisableAntiforgery();

// Jarvis callback — update feedback status
app.MapPost("/api/feedback/{id}/status", async (
    string id,
    [FromBody] FeedbackStatusUpdate statusUpdate,
    IDbContextFactory<AppDbContext> dbFactory,
    IHubContext<DashboardHub> hub,
    IConfiguration config,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var expectedToken = config["Feedback:InternalToken"];
    if (string.IsNullOrEmpty(expectedToken))
        return Results.Unauthorized();

    var providedToken = httpContext.Request.Headers["Authorization"].FirstOrDefault()
        ?.Replace("Bearer ", "", StringComparison.OrdinalIgnoreCase);
    if (providedToken != expectedToken) return Results.Unauthorized();

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var submission = await db.FeedbackSubmissions.FindAsync(new object[] { id }, ct);
    if (submission == null) return Results.NotFound();

    submission.Status = statusUpdate.Status;
    submission.AdoWiId = statusUpdate.AdoWiId;
    submission.TriageResult = statusUpdate.Message;
    submission.TriagedAt = DateTime.UtcNow;
    await db.SaveChangesAsync(ct);

    var userMessage = statusUpdate.Status switch
    {
        "dispatched" => $"Got it — this looks like a bug. It's been filed as ADO#{statusUpdate.AdoWiId} and is already being worked on.",
        "escalated" => "Thanks — this one needs a closer look. Fred will review it shortly.",
        _ => statusUpdate.Message ?? "Your feedback has been received.",
    };

    await hub.Clients.Group($"user-{submission.UserId}").SendAsync("ReceiveFeedbackResult", new
    {
        submissionId = id,
        status = statusUpdate.Status,
        message = userMessage,
        adoWiId = statusUpdate.AdoWiId,
    }, ct);

    return Results.Ok();
}).AllowAnonymous().DisableAntiforgery();

// Internal MCP endpoint for Brave Search — token-authenticated (used by harness)
app.MapPost("/internal/mcp/brave", async (HttpContext context, BraveSearchClient braveClient, IConfiguration config) =>
{
    if (!IsInternalAuthorized(context, config)) return Results.Unauthorized();

    using var reader = new StreamReader(context.Request.Body);
    var raw = await reader.ReadToEndAsync();

    JsonElement root;
    try
    {
        using var doc = JsonDocument.Parse(raw);
        root = doc.RootElement.Clone();
    }
    catch (JsonException)
    {
        return Results.BadRequest("Invalid JSON body");
    }

    // MCP JSON-RPC envelope: { method: "tools/call", params: { name, arguments } }
    var methodProp = root.TryGetProperty("method", out var m) ? m.GetString() : null;
    if (methodProp != "tools/call") return Results.BadRequest("Only tools/call supported");

    if (!root.TryGetProperty("params", out var paramsEl))
        return Results.BadRequest("Missing 'params'");
    if (!paramsEl.TryGetProperty("name", out var nameProp))
        return Results.BadRequest("Missing 'params.name'");
    var toolName = nameProp.GetString();
    if (toolName != "web_search") return Results.BadRequest($"Unknown tool: {toolName}");

    if (!paramsEl.TryGetProperty("arguments", out var args))
        return Results.BadRequest("Missing 'params.arguments'");
    var query = args.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
    var count = args.TryGetProperty("count", out var c) ? c.GetInt32() : 5;

    try
    {
        var results = await braveClient.SearchAsync(query, count);
        var formatted = braveClient.FormatResults(results);
        return Results.Ok(new { content = new[] { new { type = "text", text = formatted } } });
    }
    catch (Exception)
    {
        return Results.Problem("Brave search failed", statusCode: 500);
    }
}).AllowAnonymous().DisableAntiforgery();

// Internal MCP endpoint for Web Fetch — token-authenticated (used by harness)
app.MapPost("/internal/mcp/webfetch", async (HttpContext context, IWebFetchClient webFetchClient, IConfiguration config) =>
{
    if (!IsInternalAuthorized(context, config)) return Results.Unauthorized();

    using var reader = new StreamReader(context.Request.Body);
    var raw = await reader.ReadToEndAsync();

    JsonElement root;
    try
    {
        using var doc = JsonDocument.Parse(raw);
        root = doc.RootElement.Clone();
    }
    catch (JsonException)
    {
        return Results.BadRequest("Invalid JSON body");
    }

    // MCP JSON-RPC envelope: { method: "tools/call", params: { name, arguments } }
    var methodProp = root.TryGetProperty("method", out var m) ? m.GetString() : null;
    if (methodProp != "tools/call") return Results.BadRequest("Only tools/call supported");

    if (!root.TryGetProperty("params", out var paramsEl))
        return Results.BadRequest("Missing 'params'");
    if (!paramsEl.TryGetProperty("name", out var nameProp))
        return Results.BadRequest("Missing 'params.name'");
    var toolName = nameProp.GetString();
    if (toolName != "web_fetch") return Results.BadRequest($"Unknown tool: {toolName}");

    if (!paramsEl.TryGetProperty("arguments", out var args))
        return Results.BadRequest("Missing 'params.arguments'");
    var url = args.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
    if (string.IsNullOrWhiteSpace(url))
        return Results.BadRequest("Missing 'arguments.url'");

    var result = await webFetchClient.FetchAsync(url);

    string text;
    if (!result.Success)
    {
        text = $"Error fetching {url}: {result.ErrorMessage}";
    }
    else
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrEmpty(result.Title))
            sb.AppendLine($"# {result.Title}");
        sb.AppendLine($"URL: {url}");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(result.MarkdownContent))
            sb.AppendLine(result.MarkdownContent);
        if (result.IsJsRendered)
            sb.AppendLine("\nNote: This page may use JavaScript rendering — some content may not be captured.");
        text = sb.ToString();
    }

    return Results.Ok(new { content = new[] { new { type = "text", text } } });
}).AllowAnonymous().DisableAntiforgery();

app.MapControllers();

app.MapRazorComponents<FortressAI.Web.Components.App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly);
app.MapHub<DashboardHub>("/hubs/dashboard");
app.MapHub<CCProgressHub>("/hubs/cc-progress");

app.Run();

bool IsInternalAuthorized(HttpContext ctx, IConfiguration cfg)
{
    var token = cfg["INTERNAL_API_TOKEN"];
    if (string.IsNullOrEmpty(token)) return false;
    return ctx.Request.Headers.TryGetValue("X-Internal-Token", out var h) && h.ToString() == token;
}

record WorkingFolderRequest(string? FolderId);

record FeedbackRequest(
    string Type,
    string Description,
    string? PageUrl,
    string? ScreenshotBase64
);

record FeedbackStatusUpdate(
    string Status,
    int? AdoWiId,
    string? Message
);

