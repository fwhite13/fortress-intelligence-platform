using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;
using Amazon.S3;
using Amazon.BedrockRuntime;
using Amazon.Extensions.NETCore.Setup;
using System.Text;
using System.Text.Json;
using FamOs.Web;
using FamOs.Web.Data;
using FamOs.Web.Data.Seeds;
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
builder.Services.AddAWSService<Amazon.BedrockRuntime.IAmazonBedrockRuntime>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<UwCompletenessService>();
builder.Services.AddScoped<OpportunitySearchService>();

// ── Application Services ──
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<SignalResolver>();
builder.Services.AddScoped<LifecycleCommandService>();
builder.Services.AddScoped<IUploadLifecycleService>(sp => sp.GetRequiredService<LifecycleCommandService>());
builder.Services.AddScoped<OpportunityService>();
builder.Services.AddScoped<IAmsService, AmsServiceStub>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<UserAffinityService>();
builder.Services.AddScoped<TeamNoteService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddSingleton<IAccountSyncService, AccountSyncService>();
builder.Services.AddHostedService(sp => (AccountSyncService)sp.GetRequiredService<IAccountSyncService>());

// Quote Comparison services
builder.Services.AddScoped<IQuoteComparisonService, QuoteComparisonService>();
builder.Services.AddScoped<IPackageService, PackageService>();
builder.Services.AddScoped<ICoverageGapService, CoverageGapService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<ICarrierNoteService, CarrierNoteService>();
builder.Services.AddScoped<IIncumbentPolicyService, IncumbentPolicyService>();

builder.Services.Configure<AffinityConfig>(
    builder.Configuration.GetSection("AffinityConfig"));

// ── Fortress API client (QuoteScraperService) ──
var fortressBase = builder.Configuration["FortressApi:Endpoint"]
        ?? builder.Configuration["FortressApi:BaseUrl"]
        ?? "https://api.fortressam.ai";
builder.Services.AddHttpClient("FortressApi", c =>
{
    c.BaseAddress = new Uri(fortressBase);
    // When using ALB direct URL (bypassing Cloudflare), the Classic LB needs
    // the original hostname to route to the correct backend virtual host.
    c.DefaultRequestHeaders.Host = "api.fortressam.ai";
    c.DefaultRequestHeaders.Add("apiKey",
        builder.Configuration["FortressApi:ApiKey"]
            ?? builder.Configuration["FortressApi:Key"]
            ?? "246191f33f470f136ebb800516f8e10f");
    c.DefaultRequestHeaders.Add("apiSecret",
        builder.Configuration["FortressApi:ApiSecret"]
            ?? builder.Configuration["FortressApi:Secret"]
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

        // WI939: Fix submissions.Status column type mismatch (longtext → int)
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE submissions MODIFY COLUMN Status INT NOT NULL DEFAULT 0");
            logger.LogInformation("WI939: submissions.Status migrated to INT");
        }
        catch (Exception ex)
        {
            logger.LogWarning("WI939: submissions.Status MODIFY skipped (already INT or failed): {Msg}", ex.Message);
        }

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

        // ADO#986 — team notes table
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS team_notes (
                id             INT AUTO_INCREMENT PRIMARY KEY,
                author_id      VARCHAR(255) NOT NULL,
                note_text      TEXT NOT NULL,
                opportunity_id CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                team_tag       VARCHAR(20) NOT NULL DEFAULT 'TIG',
                created_at     DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                INDEX idx_team_notes_opp (opportunity_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        // Quote Comparison — ALTER TABLE for quotes and accounts
        await TryAddColumnAsync("ALTER TABLE quotes ADD COLUMN line_of_business_id CHAR(36) NULL");
        await TryAddColumnAsync("ALTER TABLE quotes ADD COLUMN tenant_id INT NOT NULL DEFAULT 0");
        await TryAddColumnAsync("ALTER TABLE accounts ADD COLUMN is_renewal TINYINT(1) NOT NULL DEFAULT 0");
        await TryAddColumnAsync("ALTER TABLE accounts ADD COLUMN program_vertical_id CHAR(36) NULL");

        // ADO#1016 — HubSpot field mapping columns
        await TryAddColumnAsync("ALTER TABLE accounts ADD COLUMN account_status VARCHAR(20) NULL");
        await TryAddColumnAsync("ALTER TABLE accounts ADD COLUMN primary_coverage VARCHAR(100) NULL");
        await TryAddColumnAsync("ALTER TABLE accounts ADD COLUMN primary_carrier VARCHAR(100) NULL");
        await TryAddColumnAsync("ALTER TABLE accounts ADD COLUMN policy_expires_at DATETIME NULL");
        await TryAddColumnAsync("ALTER TABLE accounts ADD COLUMN primary_deal_id VARCHAR(50) NULL");

        // Quote Comparison — new tables
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS program_verticals (
                id                CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                tenant_id         INT NOT NULL DEFAULT 0,
                name              VARCHAR(100) NOT NULL DEFAULT '',
                slug              VARCHAR(50) NOT NULL DEFAULT '',
                is_active         TINYINT(1) NOT NULL DEFAULT 1,
                fait_preset_chips LONGTEXT NULL,
                created_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at        DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE INDEX idx_pv_tenant_slug (tenant_id, slug)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS lines_of_business (
                id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                program_vertical_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id            INT NOT NULL DEFAULT 0,
                slug                 VARCHAR(50) NOT NULL DEFAULT '',
                name                 VARCHAR(100) NOT NULL DEFAULT '',
                icon                 VARCHAR(20) NOT NULL DEFAULT '',
                meta_description     VARCHAR(255) NULL,
                display_order        INT NOT NULL DEFAULT 0,
                is_active            TINYINT(1) NOT NULL DEFAULT 1,
                field_definitions    LONGTEXT NULL,
                created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE INDEX idx_lob_tenant_slug (tenant_id, slug),
                INDEX idx_lob_vertical (program_vertical_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS requirements (
                id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                program_vertical_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id            INT NOT NULL DEFAULT 0,
                slug                 VARCHAR(100) NOT NULL DEFAULT '',
                label                VARCHAR(255) NOT NULL DEFAULT '',
                group_name           VARCHAR(100) NOT NULL DEFAULT '',
                line_of_business_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                display_order        INT NOT NULL DEFAULT 0,
                is_active            TINYINT(1) NOT NULL DEFAULT 1,
                created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE INDEX idx_req_tenant_slug (tenant_id, slug),
                INDEX idx_req_vertical (program_vertical_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS packages (
                id                       CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                account_id               CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id                INT NOT NULL DEFAULT 0,
                label                    VARCHAR(10) NOT NULL DEFAULT 'A',
                status                   VARCHAR(20) NOT NULL DEFAULT 'draft',
                total_premium            DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                created_by_user_id       CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                last_modified_by_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                created_at               DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at               DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_pkg_account (account_id, tenant_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS package_selections (
                id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                package_id           CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                line_of_business_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                quote_id             CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                is_auto_bundle       TINYINT(1) NOT NULL DEFAULT 0,
                tenant_id            INT NOT NULL DEFAULT 0,
                created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE INDEX idx_pkgsel_pkg_lob (package_id, line_of_business_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS incumbent_policies (
                id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                account_id           CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                line_of_business_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id            INT NOT NULL DEFAULT 0,
                carrier_name         VARCHAR(191) NOT NULL DEFAULT '',
                policy_number        VARCHAR(100) NULL,
                annual_premium       DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                effective_date       DATE NULL,
                expiration_date      DATE NULL,
                vals                 LONGTEXT NULL,
                source_type          VARCHAR(20) NOT NULL DEFAULT 'manual',
                scraper_run_id       VARCHAR(100) NULL,
                is_overridden        TINYINT(1) NOT NULL DEFAULT 0,
                overridden_by_user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                overridden_at        DATETIME NULL,
                created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE INDEX idx_ip_account_lob (account_id, line_of_business_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS coverage_removal_acknowledgments (
                id                       CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                account_id               CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                package_id               CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id                INT NOT NULL DEFAULT 0,
                acknowledged_by_user_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                acknowledged_at          DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                coverage_description     VARCHAR(255) NOT NULL DEFAULT '',
                line_of_business_id      CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                incumbent_field_key      VARCHAR(100) NOT NULL DEFAULT '',
                incumbent_value          VARCHAR(255) NOT NULL DEFAULT '',
                proposed_value           VARCHAR(255) NULL,
                change_type              VARCHAR(20) NOT NULL DEFAULT 'removed',
                INDEX idx_cra_account_at (account_id, acknowledged_at),
                INDEX idx_cra_user_at (acknowledged_by_user_id, acknowledged_at)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS carrier_notes (
                id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                account_id           CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                quote_id             CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id            INT NOT NULL DEFAULT 0,
                note_text            LONGTEXT NOT NULL,
                created_by_user_id   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                updated_by_user_id   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                created_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_cn_account_quote (account_id, quote_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS comparison_drafts (
                id                        CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                account_id                CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id                 INT NOT NULL DEFAULT 0,
                user_id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                active_requirement_slugs  LONGTEXT NULL,
                package_a_selections      LONGTEXT NULL,
                package_b_selections      LONGTEXT NULL,
                show_incumbent            TINYINT(1) NOT NULL DEFAULT 0,
                collapsed_blocks          LONGTEXT NULL,
                saved_at                  DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE INDEX idx_cd_account_user (account_id, user_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS benchmark_premiums (
                id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                program_vertical_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                line_of_business_id  CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                tenant_id            INT NOT NULL DEFAULT 0,
                annual_premium       DECIMAL(12,2) NOT NULL DEFAULT 0.00,
                effective_date       DATE NOT NULL,
                source               VARCHAR(50) NOT NULL DEFAULT 'manual',
                notes                LONGTEXT NULL,
                updated_by_user_id   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
                updated_at           DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                INDEX idx_bp_tenant_vertical_lob (tenant_id, program_vertical_id, line_of_business_id)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS carrier_bundle_rules (
                id                   CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL PRIMARY KEY,
                tenant_id            INT NOT NULL DEFAULT 0,
                carrier_name         VARCHAR(191) NOT NULL DEFAULT '',
                primary_line_slug    VARCHAR(50) NOT NULL DEFAULT '',
                required_line_slug   VARCHAR(50) NOT NULL DEFAULT '',
                is_active            TINYINT(1) NOT NULL DEFAULT 1,
                notes                LONGTEXT NULL,
                INDEX idx_cbr_tenant_carrier (tenant_id, carrier_name, primary_line_slug)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """);

        // ADO#1019 — quote scraper persist-first redesign
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE submissions ADD COLUMN fortress_request_id VARCHAR(200) NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("fortress_request_id column already exists"); }

        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE submissions ADD COLUMN scraper_error TEXT NULL"); }
        catch (MySqlException ex) when (ex.Number == 1060) { logger.LogDebug("scraper_error column already exists"); }

        // ADO#979 — make tasks.OpportunityId nullable (general tasks)
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE tasks MODIFY COLUMN OpportunityId CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NULL");
            logger.LogInformation("ADO#979: tasks.OpportunityId made nullable");
        }
        catch (Exception ex)
        {
            logger.LogWarning("ADO#979: tasks OpportunityId MODIFY skipped: {Msg}", ex.Message);
        }

        // ADO#1112: Carrier × coverage line model
        await TryAddColumnAsync("ALTER TABLE submissions ADD COLUMN CoverageLine VARCHAR(50) NULL");
        await TryAddColumnAsync("ALTER TABLE submissions ADD COLUMN LineStatus TINYINT NOT NULL DEFAULT 0");
        await TryAddColumnAsync("ALTER TABLE quotes ADD COLUMN CoverageLine VARCHAR(50) NULL");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[FAM OS] Database initialization failed");
    }

    // WI972: Backfill OwnerUserId — empty string treated as unowned, breaks task filter
    try
    {
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE opportunities SET OwnerUserId = NULL WHERE OwnerUserId = ''");
        logger.LogInformation("WI972: Backfilled empty OwnerUserId to NULL");
    }
    catch (Exception ex)
    {
        logger.LogWarning("WI972: OwnerUserId backfill skipped: {Msg}", ex.Message);
    }

    // ADO#1033: Backfill missing quotes rows + fix tenant_id=0 on existing quotes
    try
    {
        await db.Database.ExecuteSqlRawAsync(@"
            -- Fix Northland tenant_id=0 (invisible behind HasQueryFilter)
            UPDATE quotes SET tenant_id = 1 WHERE tenant_id = 0;

            -- Backfill quotes for completed submissions that have no quotes row
            INSERT IGNORE INTO quotes (Id, OpportunityId, SubmissionId, CarrierName, PremiumAmount, CoverageDetails, IsRecommended, ReceivedAt, tenant_id)
            SELECT
                UUID(),
                s.OpportunityId,
                s.Id,
                s.CarrierName,
                0,
                s.CoverageTypes,
                0,
                NOW(6),
                1
            FROM submissions s
            LEFT JOIN quotes q ON q.SubmissionId = s.Id
            WHERE q.Id IS NULL
              AND s.Status IN (2, 7);
        ");
        logger.LogInformation("ADO#1033: Backfill migration complete");
    }
    catch (Exception ex)
    {
        logger.LogWarning("ADO#1033: Backfill migration failed (non-fatal): {Error}", ex.Message);
    }
}

// ADO#1034: Fix Progressive scrape-with-no-results + delete Northland test data
{
    using var scope1034 = app.Services.CreateScope();
    var factory1034 = scope1034.ServiceProvider.GetRequiredService<IDbContextFactory<FamOsDbContext>>();
    await using var db = await factory1034.CreateDbContextAsync();
    try
    {
        await db.Database.ExecuteSqlRawAsync(@"
            -- Mark submissions as Error where scraper returned null results (status=2 but no real data)
            -- NOTE: Northland row 88e5e8ae was NOT a test row — it was the real quote. DELETE removed.
            -- Only mark Error if there is no associated quote with a real premium
            UPDATE submissions s
            SET s.Status = 7,
                s.scraper_error = 'Scraper completed but no extraction results were returned. Click Resubmit to try again.',
                s.UpdatedAt = NOW()
            WHERE s.Status = 2
              AND (s.QuoteResultJson LIKE '%""results"":null%' OR s.QuoteResultJson IS NULL)
              AND NOT EXISTS (
                SELECT 1 FROM quotes q WHERE q.SubmissionId = s.Id AND q.PremiumAmount > 0
              );
        ");
        logger.LogInformation("ADO#1034: Progressive + Northland migration complete");
    }
    catch (Exception ex)
    {
        logger.LogWarning("ADO#1034: Migration failed (non-fatal): {Error}", ex.Message);
    }
}

// ADO#1035: Fix Sleeping→Error incorrectly set for Progressive Commercial
{
    using var scope1035 = app.Services.CreateScope();
    var factory1035 = scope1035.ServiceProvider.GetRequiredService<IDbContextFactory<FamOsDbContext>>();
    await using var db = await factory1035.CreateDbContextAsync();
    try
    {
        await db.Database.ExecuteSqlRawAsync(@"
            UPDATE submissions
            SET Status = 0,
                scraper_error = NULL,
                UpdatedAt = NOW()
            WHERE Id = '69091da0-249e-11f1-9fed-0ebd3e72fbbb'
              AND Status = 7;
        ");
        // Status=0 (Pending) so user can Resubmit — the scrape returned Sleeping which means no data yet
        logger.LogInformation("ADO#1035: Progressive submission reset to Pending");
    }
    catch (Exception ex)
    {
        logger.LogWarning("ADO#1035: Migration failed (non-fatal): {Error}", ex.Message);
    }
}

// Quote Comparison seed data
{
    using var seedScope = app.Services.CreateScope();
    var seedFactory = seedScope.ServiceProvider.GetRequiredService<IDbContextFactory<FamOsDbContext>>();
    await using var seedDb = await seedFactory.CreateDbContextAsync();
    try
    {
        await QuoteComparisonSeed.SeedAsync(seedDb);
        Console.WriteLine("[FAM OS] Quote comparison seed data checked.");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Quote comparison seed failed (may be first run before tables exist)");
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
app.UseAuthentication();
app.UseAuthorization();

// QA bypass — FAMOS_QA_BYPASS=true env var required (works in any environment)
// MUST be after UseAuthorization() so the bypass identity is not clobbered by the cookie auth check
if (Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true")
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
                new System.Security.Claims.Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "00000000-0000-0000-0000-000000000001"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "QA Tester"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "qa-bypass-user"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "qa@fortressam.ai"),
            };
            var identity = new System.Security.Claims.ClaimsIdentity(claims, "QABypass");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
        }
        await next();
    });
}

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

// QA login — issues a real auth cookie for Blazor Server bypass
app.MapGet("/qa/login", async (HttpContext ctx) =>
{
    if (!(Environment.GetEnvironmentVariable("FAMOS_QA_BYPASS") == "true" &&
          ctx.Request.Query["token"] == "natasha-qa-token-famos-dev"))
    {
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new System.Security.Claims.Claim("preferred_username", "qa@fortressam.ai"),
        new System.Security.Claims.Claim("name", "QA Tester"),
        new System.Security.Claims.Claim("oid", "00000000-0000-0000-0000-000000000001"),
        new System.Security.Claims.Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", "00000000-0000-0000-0000-000000000001"),
        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "QA Tester"),
        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "qa-bypass-user"),
        new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, "qa@fortressam.ai"),
    };
    var identity = new System.Security.Claims.ClaimsIdentity(claims, "QABypass");
    var principal = new System.Security.Claims.ClaimsPrincipal(identity);

    await ctx.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

    var returnUrl = ctx.Request.Query["returnUrl"].FirstOrDefault() ?? "/";
    return Results.Redirect(returnUrl);
}).AllowAnonymous().DisableAntiforgery();

// ── FAIT AI Assistant endpoint ──
app.MapPost("/api/fait/ask", async (FaitAskRequest req, IAmazonBedrockRuntime bedrock, ILogger<Program> log) =>
{
    try
    {
        var systemPrompt = BuildFaitSystemPrompt(req);
        var payload = JsonSerializer.Serialize(new
        {
            anthropic_version = "bedrock-2023-05-31",
            max_tokens = 1024,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = req.Question } }
        });
        var invokeReq = new Amazon.BedrockRuntime.Model.InvokeModelRequest
        {
            ModelId = "anthropic.claude-3-5-sonnet-20240620-v1:0",
            Body = new MemoryStream(Encoding.UTF8.GetBytes(payload)),
            ContentType = "application/json"
        };
        var response = await bedrock.InvokeModelAsync(invokeReq);
        using var reader = new StreamReader(response.Body);
        var raw = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
        return Results.Ok(new { answer = text });
    }
    catch (Exception ex)
    {
        log.LogError(ex, "FAIT ask endpoint failed");
        return Results.Problem("AI assistant temporarily unavailable.");
    }
}).RequireAuthorization().DisableAntiforgery();

static string BuildFaitSystemPrompt(FaitAskRequest req)
{
    var sb = new StringBuilder();
    sb.AppendLine("You are FAIT, an AI insurance assistant for FAM OS. You help agents analyze carrier quotes and make package decisions.");
    sb.AppendLine("Be concise, specific, and actionable. Focus on coverage gaps, value, and risk.");
    if (req.CheckedRequirements?.Any() == true)
        sb.AppendLine($"Active requirements: {string.Join(", ", req.CheckedRequirements)}");
    if (req.SelectedPackageA?.Any() == true)
        sb.AppendLine($"Package A lines: {string.Join(", ", req.SelectedPackageA.Keys)}");
    if (req.SelectedPackageB?.Any() == true)
        sb.AppendLine($"Package B lines: {string.Join(", ", req.SelectedPackageB.Keys)}");
    return sb.ToString();
}

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

record FaitAskRequest(
    string Question,
    Guid AccountId,
    Dictionary<string, Guid>? SelectedPackageA,
    Dictionary<string, Guid>? SelectedPackageB,
    List<string>? CheckedRequirements
);


