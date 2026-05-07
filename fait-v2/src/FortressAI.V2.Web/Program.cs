using Amazon.ECS;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using MudBlazor.Services;
using Serilog;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Components;
using FortressAI.V2.Web.Components.Hubs;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
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

// Workspace explorer
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();

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

// Push a message from an external FIP app (e.g. FIRM) into the user's FAIT v2 inbox
app.MapPost("/api/agent/push-message", async (
    PushMessageRequest request,
    IDbContextFactory<FaitV2DbContext> dbFactory,
    HttpContext httpContext,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
        return Results.Unauthorized();

    var callerOid = httpContext.User.FindFirst("oid")?.Value
                  ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

    if (string.IsNullOrEmpty(callerOid))
        return Results.Unauthorized();

    await using var db = await dbFactory.CreateDbContextAsync(ct);

    var user = await db.Users
        .FirstOrDefaultAsync(u => u.EntraOid == callerOid, ct);

    if (user == null)
        return Results.BadRequest(new { error = "User does not have a FAIT v2 account provisioned" });

    var formattedContent =
        $"📋 **Meeting Summary: {request.Title}**\n" +
        $"*{request.MeetingDate:MMM dd, yyyy}*\n\n" +
        $"{request.Summary}\n\n" +
        $"---\n" +
        $"*Pushed from FIRM. Use this context to discuss the meeting with your assistant.*";

    if (!string.IsNullOrEmpty(request.Transcript))
    {
        formattedContent +=
            $"\n\n---\n**Transcript excerpt:**\n\n{request.Transcript}";
    }

    db.PushedMessages.Add(new FortressAI.V2.Web.Data.Models.PushedMessage
    {
        Id = Guid.NewGuid().ToString(),
        UserId = user.Id,
        Source = request.Source,
        Title = request.Title,
        Content = formattedContent,
        ExternalId = request.MeetingId,
        MeetingDate = request.MeetingDate,
        CreatedAt = DateTime.UtcNow
    });
    await db.SaveChangesAsync(ct);

    logger.LogInformation("Pushed FIRM meeting {MeetingId} to FAIT v2 user {UserId}", request.MeetingId, user.Id);

    return Results.Ok(new { success = true, message = "Message pushed to FAIT v2 assistant" });
}).RequireAuthorization();

// Redirect unauthenticated users to FIP portal for login
app.MapGet("/auth/redirect-to-login", (IConfiguration cfg, HttpContext ctx) =>
{
    var fipUrl = cfg["FIP__LoginUrl"]?.TrimEnd('/') ?? "https://fip.dev.fortressam.ai";
    var returnUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host}/";
    return Results.Redirect($"{fipUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}");
}).AllowAnonymous();

// Submit feedback (authenticated user)
app.MapPost("/api/feedback", async (
    [FromBody] FeedbackRequest feedbackRequest,
    IDbContextFactory<FaitV2DbContext> dbFactory,
    IAmazonS3 s3,
    IConfiguration config,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var userId = GetUserId(httpContext);
    if (userId == null) return Results.Unauthorized();

    var submission = new FeedbackSubmission
    {
        UserId = userId,
        Type = feedbackRequest.Type,
        Description = feedbackRequest.Description,
        PageUrl = feedbackRequest.PageUrl,
        Status = "pending",
    };

    if (feedbackRequest.ScreenshotBase64 != null)
    {
        var key = $"workspaces/system/feedback/{submission.Id}/screenshot.png";
        var bytes = Convert.FromBase64String(feedbackRequest.ScreenshotBase64);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = config["AWS:S3Bucket"] ?? "fortress-tools",
            Key = key,
            InputStream = new MemoryStream(bytes),
            ContentType = "image/png",
        }, ct);
        submission.ScreenshotS3Key = key;
    }

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    db.FeedbackSubmissions.Add(submission);
    await db.SaveChangesAsync(ct);

    _ = DispatchToJarvisAsync(submission, config);

    return Results.Ok(new { submissionId = submission.Id });
}).RequireAuthorization();

// Jarvis callback — update feedback status and push result via SignalR
app.MapPost("/api/feedback/{id}/status", async (
    string id,
    [FromBody] FeedbackStatusUpdate statusUpdate,
    IDbContextFactory<FaitV2DbContext> dbFactory,
    IHubContext<CCProgressHub> hub,
    IConfiguration config,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    var expectedToken = config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token";
    var providedToken = httpContext.Request.Headers["X-Internal-Token"].FirstOrDefault();
    if (providedToken != expectedToken) return Results.Unauthorized();

    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var submission = await db.FeedbackSubmissions.FindAsync([id], ct);
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

    await hub.Clients.User(submission.UserId).SendAsync("ReceiveFeedbackResult", new
    {
        submissionId = id,
        status = statusUpdate.Status,
        message = userMessage,
        adoWiId = statusUpdate.AdoWiId,
    }, ct);

    return Results.Ok();
}).WithMetadata(new AllowAnonymousAttribute());

// Blazor components — all routes require auth
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly)
   .RequireAuthorization();

app.Run();

static string? GetUserId(HttpContext ctx)
{
    if (!(ctx.User.Identity?.IsAuthenticated ?? false)) return null;
    var oid = ctx.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
           ?? ctx.User.FindFirst("oid")?.Value;
    return string.IsNullOrEmpty(oid) ? null : oid;
}

static async Task DispatchToJarvisAsync(FeedbackSubmission submission, IConfiguration config)
{
    var ocBaseUrl = config["OpenClaw:BaseUrl"] ?? "http://localhost:3001";
    var ocToken = config["OpenClaw:ApiToken"];

    var screenshotLine = submission.ScreenshotS3Key != null
        ? $"**Screenshot:** s3://{submission.ScreenshotS3Key}"
        : "";

    var payload = new
    {
        sessionKey = "agent:main:main",
        message = $$"""
        ## FEEDBACK: {{submission.Type.ToUpper()}} from FAIT v2

        **Submission ID:** {{submission.Id}}
        **User ID:** {{submission.UserId}}
        **Page:** {{submission.PageUrl ?? "unknown"}}
        **Type:** {{submission.Type}}

        **Description:**
        {{submission.Description}}

        {{screenshotLine}}

        **Triage instructions:**
        - Auto-dispatch if this is a clear UI bug, broken element, wrong data, or regression
        - Escalate to Fred if this involves auth/permissions, data integrity, scope-expanding features, or active WI duplicates
        - After triage, call back: POST https://fait-v2.dev.fortressam.ai/api/feedback/{{submission.Id}}/status
          with headers: X-Internal-Token: fait-v2-internal-feedback-token
          with body: { "status": "dispatched"|"escalated", "adoWiId": "XXXX" (if dispatched), "message": "..." }
        """,
    };

    try
    {
        using var http = new HttpClient();
        if (!string.IsNullOrEmpty(ocToken))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ocToken);
        await http.PostAsJsonAsync($"{ocBaseUrl}/api/sessions/send", payload);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[feedback] Failed to dispatch to Jarvis: {ex.Message}");
    }
}

public record PushMessageRequest(
    string Source,
    string Title,
    string Summary,
    string? Transcript,
    string MeetingId,
    DateTime MeetingDate
);

public record FeedbackRequest(
    string Type,
    string Description,
    string? PageUrl,
    string? ScreenshotBase64
);

public record FeedbackStatusUpdate(
    string Status,
    string? AdoWiId,
    string? Message
);
