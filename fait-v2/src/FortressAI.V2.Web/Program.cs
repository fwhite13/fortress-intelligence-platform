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

// AWS Bedrock (KB direct access)
builder.Services.AddAWSService<Amazon.BedrockAgentRuntime.IAmazonBedrockAgentRuntime>();
builder.Services.AddAWSService<Amazon.BedrockAgent.IAmazonBedrockAgent>();

// AWS ECS (per-user Fargate runtime)
builder.Services.AddSingleton<IAmazonECS>(sp => new AmazonECSClient(Amazon.RegionEndpoint.USEast1));
builder.Services.AddAWSService<Amazon.BedrockRuntime.IAmazonBedrockRuntime>();
builder.Services.AddHttpClient("HarnessClient");
builder.Services.AddHttpClient("BraveSearchClient");
builder.Services.AddHttpClient("DevOpsTestClient");
builder.Services.AddHttpClient("MicrosoftGraphClient");
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
builder.Services.AddScoped<IProvisioningStatusService, ProvisioningStatusService>();
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

// FORGE KB — direct Bedrock integration (replaces fip-mcp)
builder.Services.AddScoped<IForgeKbService, ForgeKbService>();

// Brave Search — direct API (replaces fip-mcp web-search)
builder.Services.AddScoped<IBraveSearchService, BraveSearchService>();

// ADO — direct PAT-based connection (replaces fip-mcp ado)
builder.Services.AddScoped<IDevOpsConnectionService, DevOpsConnectionService>();

// MS365 — direct Graph API via delegated tokens (replaces fip-mcp ms365)
builder.Services.AddScoped<IMicrosoftTokenService, MicrosoftTokenService>();

// Design Agent
builder.Services.AddScoped<IDesignAgentService, DesignAgentService>();

// Connector management
builder.Services.AddScoped<IConnectorService, ConnectorService>();

// Context envelope service (system CLAUDE.md + per-user envelope assembly)
builder.Services.AddScoped<IContextEnvelopeService, ContextEnvelopeService>();

// CC execution service (child process orchestration)
builder.Services.AddScoped<ICCExecutionService, FargateCCExecutionService>();

// Project service (FAIT v1 carry-over)
builder.Services.AddScoped<IProjectService, ProjectService>();
builder.Services.AddScoped<ProjectStateService>();
builder.Services.AddScoped<IV1ProjectQueryService, V1ProjectQueryService>();

// Workspace explorer
builder.Services.AddScoped<IWorkspaceService, WorkspaceService>();

// Artifact service (CC-generated artifact metadata)
builder.Services.AddScoped<IArtifactService, ArtifactService>();

// Plugin agent service (admin-provisioned skill agents)
builder.Services.AddScoped<IPluginAgentService, PluginAgentService>();

// Scheduled task service + background poller
builder.Services.AddScoped<IScheduledTaskService, ScheduledTaskService>();
builder.Services.AddHostedService<ScheduledTaskBackgroundService>();

// §15 — Conversation history + compaction pipeline
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<ICompactionService, CompactionService>();
builder.Services.AddSingleton<IRAGWriteService, RAGWriteService>();
builder.Services.AddSingleton<IRAGReadService, RAGReadService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IConversationTitleService, ConversationTitleService>();
builder.Services.AddSingleton<ITaskListNotifier, TaskListNotifier>();

// KB management services
builder.Services.AddScoped<KbDocumentService>();
builder.Services.AddScoped<KbForgeService>();
builder.Services.AddSingleton<KbSyncRetryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<KbSyncRetryService>());

var app = builder.Build();

// Seed mcp_servers with all MCP tool groups (idempotent)
using (var seedScope = app.Services.CreateScope())
{
    var dbFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<FaitV2DbContext>>();
    var cfg = seedScope.ServiceProvider.GetRequiredService<IConfiguration>();
    var seedLogger = seedScope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    await using var seedDb = await dbFactory.CreateDbContextAsync();

    var toolGroups = new[]
    {
        new { Name = "forge-kb",   AuthType = "none",        DefaultRead = true,  DefaultWrite = false },
        new { Name = "ms365",      AuthType = "oauth_entra", DefaultRead = true,  DefaultWrite = false },
        new { Name = "ado",        AuthType = "oauth_entra", DefaultRead = true,  DefaultWrite = false },
        new { Name = "web-search", AuthType = "none",        DefaultRead = true,  DefaultWrite = false },
    };

    foreach (var tg in toolGroups)
    {
        var existing = await seedDb.McpServers.FirstOrDefaultAsync(s => s.Name == tg.Name);
        if (existing == null)
        {
            seedDb.McpServers.Add(new FortressAI.V2.Web.Data.Models.McpServer
            {
                Id = Guid.NewGuid().ToString(),
                Name = tg.Name,
                EndpointUrl = string.Empty,
                AuthType = tg.AuthType,
                DefaultRead = tg.DefaultRead,
                DefaultWrite = tg.DefaultWrite,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            seedLogger.LogInformation("Seeded mcp_servers entry: {Name}", tg.Name);
        }
        else
        {
            bool changed = false;
            if (existing.AuthType != tg.AuthType) { existing.AuthType = tg.AuthType; changed = true; }
            if (changed)
            {
                seedLogger.LogInformation("Updated mcp_servers entry: {Name}", tg.Name);
            }
        }
    }
    await seedDb.SaveChangesAsync();

    // Seed Marketing plugin agent
    var marketingPlugin = await seedDb.AgentPlugins.FirstOrDefaultAsync(p => p.Name == "Marketing Assistant");
    if (marketingPlugin == null)
    {
        seedDb.AgentPlugins.Add(new FortressAI.V2.Web.Data.Models.AgentPlugin
        {
            Id = "plugin-marketing",
            Name = "Marketing Assistant",
            Description = "Specialized assistant for insurance marketing copy, campaign briefs, and brand-consistent content",
            AllowedRoles = "[\"user\",\"admin\"]",
            AllowedMcpServers = "[]",
            IsActive = true,
            CreatedBy = "system",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await seedDb.SaveChangesAsync();
        seedLogger.LogInformation("Seeded Marketing plugin agent");
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

// Status endpoint — authenticated clients only (debugging/external use)
app.MapGet("/api/agent/status", async (
    HttpContext ctx,
    IDbContextFactory<FaitV2DbContext> dbFactory,
    CancellationToken ct) =>
{
    var entraOid = ctx.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                ?? ctx.User.FindFirst("oid")?.Value;
    if (string.IsNullOrEmpty(entraOid))
        return Results.Ok(new { status = "Error", message = "Not authenticated" });

    try
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.EntraOid == entraOid, ct);

        if (user?.OnboardingCompletedAt != null)
            return Results.Ok(new { status = "Running" });
        else
            return Results.Ok(new { status = "Provisioning" });
    }
    catch (Exception)
    {
        return Results.Ok(new { status = "Error", message = "Status check failed" });
    }
}).RequireAuthorization();

// Artifact download redirect — resolves presigned URL server-side (auth required)
app.MapGet("/api/artifacts/{artifactId}/download", async (
    string artifactId,
    HttpContext httpContext,
    IArtifactService artifactSvc,
    IUserService userSvc) =>
{
    var entraOid = httpContext.User.FindFirst("oid")?.Value
                ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    if (string.IsNullOrEmpty(entraOid)) return Results.Unauthorized();

    var user = await userSvc.GetByEntraOidAsync(entraOid);
    if (user == null) return Results.Unauthorized();

    try
    {
        var url = await artifactSvc.GetDownloadUrlAsync(user.Id, artifactId);
        return Results.Redirect(url);
    }
    catch (InvalidOperationException)
    {
        return Results.NotFound();
    }
}).RequireAuthorization();

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

// FIRM → inject meeting context into user's active assistant conversation
app.MapPost("/api/assistant/inject", async (
    HttpContext httpContext,
    [FromBody] AssistantInjectRequest req,
    IDbContextFactory<FaitV2DbContext> dbFactory,
    IConversationService convService,
    IConfiguration config,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    // Shared-secret guard — FIRM must send X-Firm-Secret matching FirmIntegration:SharedSecret
    // Fails CLOSED: if config key is missing, endpoint is locked down entirely
    var expectedSecret = config["FirmIntegration:SharedSecret"];
    if (string.IsNullOrEmpty(expectedSecret))
    {
        logger.LogError("AssistantInject: FirmIntegration:SharedSecret not configured — rejecting all requests");
        return Results.Unauthorized();
    }
    var providedSecret = httpContext.Request.Headers["X-Firm-Secret"].FirstOrDefault();
    if (providedSecret != expectedSecret)
    {
        logger.LogWarning("AssistantInject: rejected — missing or invalid X-Firm-Secret");
        return Results.Unauthorized();
    }
    if (string.IsNullOrEmpty(req.EntraOid)) return Results.BadRequest("entraOid required");
    await using var db = await dbFactory.CreateDbContextAsync(ct);
    var user = await db.Users.FirstOrDefaultAsync(u => u.EntraOid == req.EntraOid, ct);
    if (user == null) return Results.NotFound("User not found");
    var conv = await convService.GetOrCreateActiveConversationAsync(user.Id);
    var systemNote = $"[Context from {req.SourceType ?? "external"}: {req.Title ?? "Untitled"}]\n{req.Content}";
    await convService.AppendMessageAsync(conv.Id, "system", systemNote, systemNote.Length / 4);
    logger.LogInformation("AssistantInject: user={UserId} source={SourceType} id={SourceId}", user.Id, req.SourceType, req.SourceId);
    return Results.Ok(new { injected = true });
}).AllowAnonymous();

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

// Intervention request — harness POSTs here to trigger ApprovalDialog on client
app.MapPost("/api/intervention/request", async (
    [FromBody] InterventionRequestBody request,
    IHubContext<CCProgressHub> hub,
    IConfiguration config,
    HttpContext httpContext,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    // Shared-secret guard — harness must send X-Internal-Token (same pattern as memory search)
    var expectedSecret = config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token";
    var providedSecret = httpContext.Request.Headers["X-Internal-Token"].FirstOrDefault();
    if (string.IsNullOrEmpty(providedSecret) || providedSecret != expectedSecret)
    {
        logger.LogWarning("InterventionRequest: rejected — missing or invalid X-Internal-Token");
        return Results.Unauthorized();
    }

    if (string.IsNullOrEmpty(request.UserId) || string.IsNullOrEmpty(request.InterventionId))
        return Results.BadRequest(new { error = "userId and interventionId required" });

    var interventionRequest = new CCInterventionRequest(
        InterventionId: request.InterventionId,
        TaskId: request.TaskId ?? "",
        ActionType: request.ActionType ?? "unknown",
        ActionSummary: request.ActionSummary ?? "Perform action",
        ActionDetails: request.ActionDetails
    );

    await hub.Clients.User(request.UserId)
        .SendAsync("ReceiveInterventionRequired", interventionRequest, ct);

    logger.LogInformation("InterventionRequest: sent to userId={UserId}, interventionId={Id}, actionType={ActionType}",
        request.UserId, request.InterventionId, request.ActionType);

    return Results.Ok(new { ok = true });
}).AllowAnonymous(); // guarded by X-Internal-Token header, not auth cookie

// Memory search endpoint (for agent-harness search_memory tool)
app.MapPost("/api/memory/search", async (
    [FromBody] MemorySearchRequest searchRequest,
    IRAGReadService ragReadService,
    HttpContext httpContext,
    IConfiguration config,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    // Internal harness calls use X-Internal-Token shared secret (AllowAnonymous)
    // Browser/component calls use standard cookie auth
    var internalToken = config["Harness:InternalApiToken"];
    var providedToken = httpContext.Request.Headers["X-Internal-Token"].FirstOrDefault();
    string? userId;

    if (!string.IsNullOrEmpty(providedToken) && !string.IsNullOrEmpty(internalToken))
    {
        // Harness path — validate shared secret (only if Harness:InternalApiToken is configured)
        if (providedToken != internalToken)
        {
            logger.LogWarning("MemorySearch: rejected invalid X-Internal-Token");
            return Results.Unauthorized();
        }
        // userId must be in request body for harness calls
        if (string.IsNullOrEmpty(searchRequest.UserId))
            return Results.BadRequest("userId required for harness calls");
        userId = searchRequest.UserId;
    }
    else
    {
        // Browser path — require cookie auth
        userId = GetUserId(httpContext);
        if (userId == null) return Results.Unauthorized();
    }

    var results = await ragReadService.SearchAsync(
        userId,
        searchRequest.Query,
        searchRequest.TopK ?? 5,
        ct);

    return Results.Ok(results.Select(r => new
    {
        topicSlug = r.TopicSlug,
        source = r.Source,
        content = r.Content,
        similarity = r.Similarity,
    }));
}).AllowAnonymous();

// Memory write endpoint (ADO#3093 — preference detection from harness)
app.MapPost("/api/memory/write", async (
    [FromBody] MemoryWriteRequest writeRequest,
    IRAGWriteService ragWriteService,
    HttpContext httpContext,
    IConfiguration config,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    // Dual-auth: X-Internal-Token (harness) or cookie auth (browser)
    var internalToken = config["Harness:InternalApiToken"];
    var providedToken = httpContext.Request.Headers["X-Internal-Token"].FirstOrDefault();
    string? userId;

    if (!string.IsNullOrEmpty(providedToken) && !string.IsNullOrEmpty(internalToken))
    {
        if (providedToken != internalToken)
        {
            logger.LogWarning("MemoryWrite: rejected invalid X-Internal-Token");
            return Results.Unauthorized();
        }
        if (string.IsNullOrEmpty(writeRequest.UserId))
            return Results.BadRequest(new { error = "userId required for harness calls" });
        userId = writeRequest.UserId;
    }
    else
    {
        userId = GetUserId(httpContext);
        if (userId == null) return Results.Unauthorized();
    }

    if (string.IsNullOrEmpty(writeRequest.TopicSlug))
        return Results.BadRequest(new { error = "topicSlug required" });
    if (string.IsNullOrEmpty(writeRequest.Content))
        return Results.BadRequest(new { error = "content required" });

    var chunk = new MemoryChunk(
        UserId: userId,
        TopicSlug: writeRequest.TopicSlug,
        Content: writeRequest.Content,
        Source: writeRequest.Source ?? "api",
        CreatedAt: DateTimeOffset.UtcNow
    );

    await ragWriteService.WriteFactAsync(chunk, ct);

    logger.LogInformation("MemoryWrite: wrote chunk for userId={UserId}, topicSlug={TopicSlug}, source={Source}",
        userId, writeRequest.TopicSlug, writeRequest.Source);

    return Results.Ok(new { ok = true });
}).AllowAnonymous(); // guarded by X-Internal-Token or cookie auth

// §6.1 — serve agent soul content for harness pluginAgentId switching
app.MapGet("/api/agents/{pluginAgentId}/soul", async (
    string pluginAgentId,
    HttpContext httpContext,
    IConfiguration config,
    IPluginAgentService pluginAgentService,
    CancellationToken ct) =>
{
    // Dual-auth: X-Internal-Token (harness) or cookie auth (browser)
    var internalToken = config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token";
    var providedToken = httpContext.Request.Headers["X-Internal-Token"].FirstOrDefault();
    if (!string.IsNullOrEmpty(providedToken))
    {
        if (providedToken != internalToken) return Results.Unauthorized();
    }
    else
    {
        if (GetUserId(httpContext) == null) return Results.Unauthorized();
    }
    var plugin = await pluginAgentService.GetPluginByIdAsync(pluginAgentId, ct);
    if (plugin == null) return Results.NotFound(new { error = "Agent not found" });
    var content = await pluginAgentService.GetSkillsContentAsync(plugin, ct);
    return Results.Ok(new { content });
}).AllowAnonymous(); // guarded by X-Internal-Token or cookie auth

// Blazor components — all routes require auth
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode()
   .AddAdditionalAssemblies(typeof(FipShared.Components.FipNavBar).Assembly)
   .RequireAuthorization();

app.Run();

static string? GetUserId(HttpContext ctx)
{
    if (!(ctx.User.Identity?.IsAuthenticated ?? false)) return null;
    var oid = ctx.User.FindFirst("oid")?.Value
           ?? ctx.User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
    return string.IsNullOrEmpty(oid) ? null : oid;
}

static async Task DispatchToJarvisAsync(FeedbackSubmission submission, IConfiguration config)
{
    var ocBaseUrl = config["OpenClaw:BaseUrl"] ?? "http://localhost:3001";
    var ocToken = config["OpenClaw:ApiToken"];

    var internalToken = config["Feedback:InternalToken"] ?? "fait-v2-internal-feedback-token";

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
          with headers: X-Internal-Token: {{internalToken}}
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

public record AssistantInjectRequest(
    string EntraOid,
    string Content,
    string? SourceType,
    string? SourceId,
    string? Title
);

public record MemorySearchRequest(string Query, int? TopK, string? UserId = null);

public record MemoryWriteRequest(string TopicSlug, string Content, string? UserId = null, string? Source = null);

public record InterventionRequestBody(
    string UserId,
    string InterventionId,
    string? TaskId,
    string? ActionType,
    string? ActionSummary,
    string? ActionDetails
);
