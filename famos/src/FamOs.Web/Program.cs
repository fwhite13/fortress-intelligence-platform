using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;
using Amazon.S3;
using Amazon.Extensions.NETCore.Setup;
using FamOs.Web;
using FamOs.Web.Data;
using FamOs.Web.Domain;
using FamOs.Web.Services;
using FamOs.Web.Components;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── MudBlazor ──
builder.Services.AddMudServices();

// ── Authentication ──
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
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── EF Core (Aurora MySQL) ──
var dbHost = builder.Configuration["FORTRESS_DB_HOST"];
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "dev";
var dbName = builder.Configuration["FAMOS_DB_NAME"] ?? "famos_dev";

string connectionString;
if (!string.IsNullOrEmpty(dbHost))
{
    var csb = new MySqlConnectionStringBuilder
    {
        Server            = dbHost,
        Port              = uint.Parse(dbPort),
        Database          = dbName,
        UserID            = dbUser,
        Password          = dbPass,
        ConnectionTimeout = 10
    };
    connectionString = csb.ConnectionString;
    Console.WriteLine($"Using Aurora MySQL: {dbHost}/{dbName}");
}
else
{
    connectionString = builder.Configuration.GetConnectionString("Default")
        ?? "Server=localhost;Database=famos_dev;User=root;Password=dev;";
    Console.WriteLine("Using local connection string.");
}

var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContextFactory<FamOsDbContext>(options =>
    options.UseMySql(connectionString, serverVersion,
        mysql => mysql.EnableRetryOnFailure(3)));

// ── Data Protection: shared key ring ──
var keyRingHost = builder.Configuration["FORTRESS_DB_HOST"];
var keyRingDb   = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fip_keyring";
var keyRingCsb  = new MySqlConnectionStringBuilder
{
    Server            = keyRingHost ?? "localhost",
    Port              = uint.Parse(builder.Configuration["FORTRESS_DB_PORT"] ?? "3306"),
    Database          = keyRingDb,
    UserID            = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql",
    Password          = builder.Configuration["FORTRESS_DB_PASS"] ?? "",
    ConnectionTimeout = 10
};

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();

// AWS S3 — uses ECS task role (no explicit credentials needed)
builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
builder.Services.AddAWSService<IAmazonS3>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<UwCompletenessService>();
builder.Services.AddScoped<OpportunitySearchService>();

// ── Application Services ──
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<SignalResolver>();
builder.Services.AddScoped<LifecycleCommandService>();
builder.Services.AddScoped<OpportunityService>();
builder.Services.AddScoped<IAmsService, AmsServiceStub>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<UserAffinityService>();
builder.Services.AddSingleton<IAccountSyncService, AccountSyncService>();
builder.Services.AddHostedService(sp => (AccountSyncService)sp.GetRequiredService<IAccountSyncService>());

builder.Services.Configure<AffinityConfig>(
    builder.Configuration.GetSection("AffinityConfig"));

// ── Fortress API client (QuoteScraperService) ──
var fortressBase = builder.Configuration["FortressApi:BaseUrl"] ?? "https://api.fortressam.ai";
builder.Services.AddHttpClient("FortressApi", c =>
{
    c.BaseAddress = new Uri(fortressBase);
    c.DefaultRequestHeaders.Add("X-Api-Key",
        builder.Configuration["FortressApi:Key"] ?? "246191f33f470f136ebb800516f8e10f");
    c.DefaultRequestHeaders.Add("X-Api-Secret",
        builder.Configuration["FortressApi:Secret"]
            ?? "77a883a60a2d941b0c1f038881150141dd3655f449c5dadf97e6ffb7066faf4d");
});
builder.Services.AddScoped<IQuoteScraperService, QuoteScraperService>();

// ── HubSpot API client — real service when key configured, stub otherwise ──
var hubspotKey = builder.Configuration["HubSpot:ServiceKey"];
builder.Services.AddHttpClient("HubSpot", c =>
{
    c.BaseAddress = new Uri("https://api.hubapi.com");
    if (!string.IsNullOrEmpty(hubspotKey))
        c.DefaultRequestHeaders.Add("Authorization", $"Bearer {hubspotKey}");
});
if (!string.IsNullOrEmpty(hubspotKey))
    builder.Services.AddScoped<IHubSpotService, HubSpotService>();
else
    builder.Services.AddScoped<IHubSpotService, HubSpotServiceStub>();

// ── Background Services ──
builder.Services.AddHostedService<OutboxProcessorService>();
builder.Services.AddHostedService<SignalRecomputeService>();
builder.Services.AddHostedService<AgingService>();

// ── Internal HTTP client ──
var internalBase = "http://localhost:8080/";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(internalBase) });

var app = builder.Build();
var logger = app.Logger;

// ── Database initialization (blocking — must complete before hosted services run) ──
{
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FamOsDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    try
    {
        bool tablesExist = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM opportunities LIMIT 1");
            tablesExist = true;
        }
        catch
        {
            // Expected on first run
        }

        if (!tablesExist)
        {
            try
            {
                var creator = db.Database.GetService<Microsoft.EntityFrameworkCore.Storage.IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync();
                Console.WriteLine("[FAM OS] Database tables created.");
            }
            catch (MySqlException ex) when (ex.Number == 1050)
            {
                logger.LogDebug("CreateTablesAsync: tables already exist (1050), continuing");
            }
        }
        else
        {
            Console.WriteLine("[FAM OS] DB tables already exist.");
        }

        // Sprint 4: add intake_responses_json column if missing (Aurora MySQL compatible — no IF NOT EXISTS)
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE opportunities ADD COLUMN intake_responses_json MEDIUMTEXT NULL");
        }
        catch (MySqlException ex) when (ex.Number == 1060)
        {
            // 1060 = Duplicate column name — column already exists, safe to continue
            logger.LogDebug("intake_responses_json column already exists (1060), continuing");
        }

        // Sprint 5: Part C — PascalCase to match EF model (snake_case renamed in Aurora 2026-03-19)
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE opportunities ADD COLUMN CloseReason INT NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("CloseReason column already exists"); }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE opportunities ADD COLUMN CloseNotes LONGTEXT NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("CloseNotes column already exists"); }

        // Sprint 5: Part E
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE opportunities ADD COLUMN LastStageTransitionAt DATETIME NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("LastStageTransitionAt column already exists"); }

        // Sprint 5: Part A (Submission table enhancements — PascalCase)
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE submissions ADD COLUMN CoverageTypes VARCHAR(200) NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("CoverageTypes column already exists"); }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE submissions ADD COLUMN SubmittedAt DATETIME NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("SubmittedAt column already exists"); }
        // RespondedAt already exists from Sprint 1 — skip
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE submissions ADD COLUMN QuoteResultJson MEDIUMTEXT NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("QuoteResultJson column already exists"); }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE submissions ADD COLUMN Notes LONGTEXT NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("Notes column already exists"); }
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE submissions ADD COLUMN UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("UpdatedAt column already exists"); }

        // Sprint 6 — new tables (contacts, opportunity_documents)
        // Note: CHAR(36) FK columns must use ascii/ascii_general_ci to match opportunities.Id collation (Aurora MySQL 5.7-compat)
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS contacts (
                id            CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                opportunity_id CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                first_name    VARCHAR(100) NOT NULL DEFAULT '',
                last_name     VARCHAR(100) NOT NULL DEFAULT '',
                title         VARCHAR(100) NULL,
                email         VARCHAR(200) NULL,
                phone         VARCHAR(50) NULL,
                contact_type  INT NOT NULL DEFAULT 0,
                notes         LONGTEXT NULL,
                created_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at    DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_contacts_opp (opportunity_id),
                FOREIGN KEY (opportunity_id) REFERENCES opportunities(Id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS opportunity_documents (
                id              CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                opportunity_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                file_name       VARCHAR(255) NOT NULL DEFAULT '',
                file_type       VARCHAR(100) NULL,
                s3_key          VARCHAR(500) NOT NULL DEFAULT '',
                document_category INT NOT NULL DEFAULT 6,
                uploaded_at     DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                uploaded_by     VARCHAR(200) NULL,
                INDEX idx_docs_opp (opportunity_id),
                FOREIGN KEY (opportunity_id) REFERENCES opportunities(Id) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        // Sprint 6 — new column on opportunities (PascalCase for existing table)
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE opportunities ADD COLUMN PrimaryContactId CHAR(36) NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("PrimaryContactId column already exists"); }

        // Sprint 7 — proposal enhancements
        async Task TryAddColumnAsync(string sql) {
            try { await db.Database.ExecuteSqlRawAsync(sql); }
            catch (MySqlException ex) when (ex.Number == 1060) { /* already exists */ }
        }

        await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN carrier_name VARCHAR(200) NULL");
        await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN coverage_types VARCHAR(200) NULL");
        await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN proposal_date DATETIME NULL");
        await TryAddColumnAsync("ALTER TABLE proposals ADD COLUMN notes LONGTEXT NULL");

        // Sprint 7 — policy_shadow_records enhancements
        await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN policy_number VARCHAR(100) NULL");
        await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN expiration_date DATE NULL");
        await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN coverage_type VARCHAR(100) NULL");
        await TryAddColumnAsync("ALTER TABLE policy_shadow_records ADD COLUMN bound_at DATETIME NULL");

        // Sprint 7 — opportunity bind tracking
        await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN bind_confirmation_number VARCHAR(100) NULL");
        await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN bind_request_submitted_at DATETIME NULL");

        // Sprint 8 — affinity_id on opportunities
        await TryAddColumnAsync("ALTER TABLE opportunities ADD COLUMN affinity_id VARCHAR(50) NOT NULL DEFAULT 'tig'");

        // Sprint 8 — accounts cache table (CREATE TABLE IF NOT EXISTS — new table, no data)
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS accounts (
                id              CHAR(36) NOT NULL PRIMARY KEY,
                affinity_id     VARCHAR(50) NOT NULL DEFAULT '',
                company_name    VARCHAR(255) NOT NULL DEFAULT '',
                hubspot_id      VARCHAR(50) NULL,
                city            VARCHAR(100) NULL,
                state           VARCHAR(10) NULL,
                active_opp_count INT NOT NULL DEFAULT 0,
                last_synced_at  DATETIME NULL,
                created_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at      DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_accounts_affinity (affinity_id),
                INDEX idx_accounts_name (company_name)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[FAM OS] Database initialization failed");
    }
}

// ── Middleware pipeline ──
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

// QA bypass — dev/staging only (FAMOS_QA_BYPASS=true env var required)
if (app.Environment.IsDevelopment() ||
    Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Headers.ContainsKey("X-QA-Bypass") &&
            context.Request.Headers["X-QA-Bypass"] == "natasha-qa-token-famos-dev")
        {
            var claims = new[]
            {
                new System.Security.Claims.Claim("preferred_username", "qa@fortressam.ai"),
                new System.Security.Claims.Claim("name", "QA Tester"),
                new System.Security.Claims.Claim("oid", "00000000-0000-0000-0000-000000000001"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "QA Tester"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "qa-bypass-user"),
            };
            var identity = new System.Security.Claims.ClaimsIdentity(claims, "QABypass");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Health check ──
app.MapGet("/health", () => Results.Ok(new {
    status    = "healthy",
    service   = "famos",
    timestamp = DateTime.UtcNow
})).AllowAnonymous();

app.MapGet("/qa/status", () => Results.Ok(new {
    qaBypass    = true,
    environment = "dev",
    timestamp   = DateTime.UtcNow,
    message     = "QA bypass active"
})).AllowAnonymous();

// ── Auth redirect helper ──
app.MapGet("/auth/redirect-to-login", (HttpContext ctx) =>
{
    var faitUrl = builder.Configuration["FIP:LoginUrl"] ?? "https://fait.dev.fortressam.ai/";
    var returnUrl = Uri.EscapeDataString(ctx.Request.Headers.Referer.FirstOrDefault() ?? "/");
    return Results.Redirect($"{faitUrl}?returnUrl={returnUrl}");
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var faitUrl = builder.Configuration["FIP:LoginUrl"] ?? "https://fait.dev.fortressam.ai/";
    return Results.Redirect(faitUrl);
}).AllowAnonymous().DisableAntiforgery();

// ── Blazor Server ──
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
