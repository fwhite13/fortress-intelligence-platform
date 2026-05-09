using FortressAI.Web.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace FortressAI.Web.Services;

public class DatabaseInitializationService : IHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<DatabaseInitializationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IServiceScopeFactory _scopeFactory;

    public DatabaseInitializationService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<DatabaseInitializationService> logger,
        IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider,
        IServiceScopeFactory scopeFactory)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _configuration = configuration;
        _dataProtectionProvider = dataProtectionProvider;
        _scopeFactory = scopeFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting database initialization...");

            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            // Verify connectivity before proceeding
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                _logger.LogError("Cannot connect to database — skipping initialization");
                return;
            }

            // Always create all EF Core model tables (CREATE TABLE IF NOT EXISTS semantics)
            // Wrapped in its own try-catch so a throw (e.g. table already exists) does NOT
            // propagate to the outer catch and abort the hardcoded extraTables loop below.
            try
            {
                var creator = db.Database.GetService<IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync(cancellationToken);
                _logger.LogInformation("DB tables ensured via EF Core.");
            }
            catch (Exception efEx)
            {
                _logger.LogWarning("CreateTablesAsync encountered errors (non-fatal, hardcoded tables will handle it): {Message}", efEx.Message);
            }

            // Always ensure hardcoded tables (all use IF NOT EXISTS)
            var extraTables = new[]
            {
                ("applied_migrations", @"CREATE TABLE IF NOT EXISTS applied_migrations (
                    name VARCHAR(100) PRIMARY KEY,
                    applied_at DATETIME(6) NOT NULL DEFAULT NOW(6)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("user_assistant_config", @"CREATE TABLE IF NOT EXISTS user_assistant_config (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    UserId CHAR(36) NOT NULL,
                    AssistantName VARCHAR(100) NOT NULL DEFAULT 'Assistant',
                    AvatarId VARCHAR(50) NOT NULL DEFAULT 'shield',
                    ColorHex VARCHAR(10) NOT NULL DEFAULT '#d4af37',
                    PersonalityPreset VARCHAR(20) NOT NULL DEFAULT 'friendly',
                    CreatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6),
                    UpdatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6),
                    UNIQUE INDEX IX_user_assistant_config_UserId (UserId)
                )"),
                ("briefing_history", @"CREATE TABLE IF NOT EXISTS briefing_history (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    UserId CHAR(36) NOT NULL,
                    BriefingDate DATE NOT NULL,
                    Content TEXT NOT NULL,
                    EmailSummary TEXT,
                    calendar_events TEXT,
                    CreatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6),
                    INDEX IX_briefing_history_UserId_BriefingDate (UserId, BriefingDate)
                )"),
                ("user_briefing_schedule", @"CREATE TABLE IF NOT EXISTS user_briefing_schedule (
                    UserId CHAR(36) PRIMARY KEY,
                    DeliveryTimeUtc TIME NOT NULL DEFAULT '13:00:00',
                    EmailDigestEnabled TINYINT(1) NOT NULL DEFAULT 0
                )"),
                ("user_microsoft_tokens", @"CREATE TABLE IF NOT EXISTS user_microsoft_tokens (
                    UserId CHAR(36) PRIMARY KEY,
                    AccessToken TEXT NOT NULL,
                    RefreshToken TEXT NOT NULL,
                    ExpiresAt TIMESTAMP(6) NOT NULL,
                    MicrosoftEmail VARCHAR(255),
                    CreatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6),
                    UpdatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6)
                )"),
                ("user_devops_connections", @"CREATE TABLE IF NOT EXISTS user_devops_connections (
                    user_id CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
                    org_url VARCHAR(512) NOT NULL,
                    pat_encrypted LONGTEXT NOT NULL,
                    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (user_id),
                    CONSTRAINT fk_devops_conn_user FOREIGN KEY (user_id) REFERENCES users (Id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;"),
                ("graph_subscriptions", @"CREATE TABLE IF NOT EXISTS graph_subscriptions (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    UserId CHAR(36) NOT NULL,
                    SubscriptionId VARCHAR(255) NOT NULL,
                    ClientState VARCHAR(255) NOT NULL,
                    ExpiresAt TIMESTAMP(6) NOT NULL,
                    CreatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6),
                    INDEX idx_graph_sub_user (UserId),
                    INDEX idx_graph_sub_expiry (ExpiresAt)
                )"),
                ("email_alerts", @"CREATE TABLE IF NOT EXISTS email_alerts (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    UserId CHAR(36) NOT NULL,
                    MessageId VARCHAR(255) NOT NULL,
                    SenderEmail VARCHAR(255) NOT NULL,
                    Subject TEXT NOT NULL,
                    Importance VARCHAR(10) NOT NULL DEFAULT 'LOW',
                    Summary TEXT,
                    SuggestedResponse TEXT,
                    Dismissed TINYINT(1) NOT NULL DEFAULT 0,
                    CreatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6),
                    INDEX idx_email_alerts_user (UserId),
                    INDEX idx_email_alerts_dismissed (Dismissed)
                )"),
                ("email_log", @"CREATE TABLE IF NOT EXISTS email_log (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    UserId CHAR(36) NOT NULL,
                    MessageId VARCHAR(255) NOT NULL,
                    SenderEmail VARCHAR(255) NOT NULL,
                    Subject TEXT NOT NULL,
                    Importance VARCHAR(10) NOT NULL DEFAULT 'LOW',
                    ReceivedAt TIMESTAMP(6) NOT NULL,
                    CreatedAt TIMESTAMP(6) DEFAULT CURRENT_TIMESTAMP(6),
                    INDEX idx_email_log_user (UserId)
                )"),
                ("DataProtectionKeys", @"CREATE TABLE IF NOT EXISTS DataProtectionKeys (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    FriendlyName TEXT NULL,
                    Xml TEXT NULL
                )"),
                ("task_cache", @"CREATE TABLE IF NOT EXISTS task_cache (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    UserId CHAR(36) NOT NULL,
                    TaskId VARCHAR(255) NOT NULL,
                    Title VARCHAR(500) NOT NULL,
                    DueDate DATETIME(6) NULL,
                    PercentComplete INT NOT NULL DEFAULT 0,
                    Priority INT NOT NULL DEFAULT 5,
                    PlanTitle VARCHAR(255) NULL,
                    BucketName VARCHAR(255) NULL,
                    LastFetchedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    CreatedAt DATETIME(6) NOT NULL,
                    UNIQUE INDEX idx_task_cache_user_taskid (UserId, TaskId),
                    INDEX idx_task_cache_user_due (UserId, DueDate)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("calendar_cache", @"CREATE TABLE IF NOT EXISTS calendar_cache (
                    Id INT PRIMARY KEY AUTO_INCREMENT,
                    UserId CHAR(36) NOT NULL,
                    EventId VARCHAR(255) NOT NULL,
                    Subject VARCHAR(500) NOT NULL,
                    StartTime DATETIME(6) NOT NULL,
                    EndTime DATETIME(6) NOT NULL,
                    Location VARCHAR(500) NULL,
                    OnlineMeetingUrl TEXT NULL,
                    AttendeesJson TEXT NULL,
                    Category VARCHAR(100) NULL,
                    LastFetchedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    CreatedAt DATETIME(6) NOT NULL,
                    UNIQUE INDEX idx_calendar_cache_user_eventid (UserId, EventId),
                    INDEX idx_calendar_cache_user_start (UserId, StartTime)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("post_meeting_notes", @"CREATE TABLE IF NOT EXISTS post_meeting_notes (
                    Id CHAR(36) NOT NULL,
                    UserId CHAR(36) NOT NULL,
                    EventId VARCHAR(255) NOT NULL,
                    EventSubject VARCHAR(500) NOT NULL,
                    MeetingEndTime DATETIME(6) NOT NULL,
                    Notes TEXT NOT NULL,
                    Summary TEXT NULL,
                    CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                    PRIMARY KEY (Id),
                    INDEX idx_post_meeting_user_event (UserId, EventId)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("kb_entries", @"CREATE TABLE IF NOT EXISTS kb_entries (
    Id INT NOT NULL AUTO_INCREMENT,
    UserId CHAR(36) NOT NULL,
    TeamId INT NULL,
    Tier TINYINT NOT NULL DEFAULT 0,
    Title VARCHAR(500) NOT NULL,
    Content TEXT NOT NULL,
    Tags VARCHAR(500) NULL,
    SourceUrl VARCHAR(1000) NULL,
    CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UpdatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (Id),
    INDEX idx_kb_entries_user (UserId),
    INDEX idx_kb_entries_user_tier (UserId, Tier)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"),
                ("kb_teams", @"CREATE TABLE IF NOT EXISTS kb_teams (
    Id INT NOT NULL AUTO_INCREMENT,
    CreatorId CHAR(36) NOT NULL,
    Name VARCHAR(200) NOT NULL,
    Description VARCHAR(1000) NULL,
    CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (Id),
    INDEX idx_kb_teams_creator (CreatorId)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"),
                ("kb_team_members", @"CREATE TABLE IF NOT EXISTS kb_team_members (
    Id INT NOT NULL AUTO_INCREMENT,
    TeamId INT NOT NULL,
    UserId CHAR(36) NOT NULL,
    Role TINYINT NOT NULL DEFAULT 0,
    JoinedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (Id),
    UNIQUE INDEX idx_kb_team_members_unique (TeamId, UserId),
    INDEX idx_kb_team_members_user (UserId)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"),
                ("mcp_servers", @"CREATE TABLE IF NOT EXISTS mcp_servers (
    id CHAR(36) NOT NULL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    slug VARCHAR(50) NOT NULL,
    description TEXT,
    icon_url VARCHAR(500),
    transport_type VARCHAR(20) NOT NULL DEFAULT 'http',
    endpoint_url VARCHAR(500),
    auth_type VARCHAR(20) NOT NULL DEFAULT 'none',
    auth_config JSON,
    tool_manifest JSON,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    requires_user_auth TINYINT(1) NOT NULL DEFAULT 0,
    system_api_key TEXT,
    oauth_client_secret TEXT,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uq_slug (slug)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("user_mcp_tokens", @"CREATE TABLE IF NOT EXISTS user_mcp_tokens (
    id CHAR(36) NOT NULL PRIMARY KEY,
    user_id CHAR(36) NOT NULL,
    server_id CHAR(36) NOT NULL,
    access_token TEXT NOT NULL,
    refresh_token TEXT,
    token_expires_at DATETIME(6),
    scopes VARCHAR(1000),
    external_user_id VARCHAR(255),
    external_email VARCHAR(255),
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uq_user_server (user_id, server_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("conversation_mcp_servers", @"CREATE TABLE IF NOT EXISTS conversation_mcp_servers (
    id CHAR(36) NOT NULL PRIMARY KEY,
    conversation_id CHAR(36) NOT NULL,
    server_id CHAR(36) NOT NULL,
    enabled TINYINT(1) NOT NULL DEFAULT 1,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY uq_conv_server (conversation_id, server_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("mcp_tool_call_log", @"CREATE TABLE IF NOT EXISTS mcp_tool_call_log (
    id CHAR(36) NOT NULL PRIMARY KEY,
    user_id CHAR(36) NOT NULL,
    conversation_id CHAR(36) NOT NULL,
    message_id CHAR(36),
    server_id CHAR(36) NOT NULL,
    tool_name VARCHAR(100) NOT NULL,
    input_json JSON,
    output_json JSON,
    status VARCHAR(20) NOT NULL,
    error_message TEXT,
    latency_ms INT,
    created_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    INDEX idx_user_created (user_id, created_at),
    INDEX idx_conversation (conversation_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("conversation_team_kbs", @"CREATE TABLE IF NOT EXISTS conversation_team_kbs (
    conversation_id CHAR(36) NOT NULL,
    team_id INT NOT NULL,
    enabled_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (conversation_id, team_id)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("user_module_permissions", @"CREATE TABLE IF NOT EXISTS user_module_permissions (
    id INT NOT NULL AUTO_INCREMENT,
    user_id CHAR(36) NOT NULL,
    module VARCHAR(50) NOT NULL,
    permission VARCHAR(50) NOT NULL,
    granted TINYINT(1) NOT NULL DEFAULT 1,
    granted_at DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    granted_by_user_id CHAR(36) NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_user_module_permission (user_id, module, permission)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci")
                ,("chat_attachments", @"CREATE TABLE IF NOT EXISTS chat_attachments (
    Id CHAR(36) PRIMARY KEY,
    ConversationId CHAR(36) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    MessageId CHAR(36) NOT NULL,
    UserId CHAR(36) NOT NULL,
    Filename VARCHAR(255) NOT NULL,
    ContentType VARCHAR(100) NOT NULL,
    S3Key VARCHAR(500) NOT NULL,
    SizeBytes BIGINT NOT NULL,
    TokenEstimate INT NULL,
    CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    INDEX idx_chat_attachments_conv (ConversationId),
    CONSTRAINT FK_chat_attachments_conversations_ConversationId
        FOREIGN KEY (ConversationId) REFERENCES conversations(Id) ON DELETE CASCADE
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci")
                ,("user_sessions", @"CREATE TABLE IF NOT EXISTS user_sessions (
    id VARCHAR(36) NOT NULL PRIMARY KEY,
    user_id VARCHAR(36) NOT NULL,
    started_at DATETIME(6) NOT NULL,
    last_active_at DATETIME(6) NOT NULL,
    ended_at DATETIME(6) NULL,
    task_arn VARCHAR(500) NULL,
    private_ip VARCHAR(45) NULL,
    fargate_status VARCHAR(20) NULL,
    fargate_session_id VARCHAR(200) NULL,
    task_definition_revision VARCHAR(100) NULL,
    created_at DATETIME(6) NOT NULL,
    updated_at DATETIME(6) NOT NULL,
    INDEX ix_user_sessions_user_id (user_id),
    INDEX ix_user_sessions_last_active_at (last_active_at)
) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci")
            };

            foreach (var (name, sql) in extraTables)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                    _logger.LogInformation("Table '{TableName}' ensured.", name);
                }
                catch (Exception tableEx)
                {
                    _logger.LogWarning("Table '{TableName}' creation note: {Message}", name, tableEx.Message);
                }
            }

            // FULLTEXT index — separate try-catch so failure never propagates
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER TABLE messages ADD FULLTEXT INDEX idx_messages_content (Content)", cancellationToken);
                _logger.LogInformation("FULLTEXT index created on messages.Content.");
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyName
                                            || ex.Message.Contains("Duplicate key name", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("FULLTEXT index already exists (expected)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create FULLTEXT index (non-fatal)");
            }

            // Schema migrations — idempotent ALTER TABLE statements
            var alterStatements = new[]
            {
                "ALTER TABLE mcp_servers ADD COLUMN oauth_client_secret TEXT NULL",
                "ALTER TABLE mcp_servers ADD COLUMN rate_limit_per_minute INT NOT NULL DEFAULT 30",
                // Drop legacy single-team column — superseded by conversation_team_kbs join table
                // Catches 1091 (unknown column) for idempotency; Aurora MySQL does not support DROP COLUMN IF EXISTS
                "ALTER TABLE conversations DROP COLUMN EnableTeamKbId",
                "ALTER TABLE users ADD COLUMN is_active TINYINT(1) NOT NULL DEFAULT 1",
                "ALTER TABLE users ADD COLUMN is_entra_user TINYINT(1) NOT NULL DEFAULT 0",
                "ALTER TABLE users ADD COLUMN entra_oid VARCHAR(255) NULL",
                // Fix mcp_tool_call_log JSON columns — MySQL JSON rejects non-JSON plain text output
                "ALTER TABLE mcp_tool_call_log MODIFY COLUMN input_json LONGTEXT",
                "ALTER TABLE mcp_tool_call_log MODIFY COLUMN output_json LONGTEXT",
                // Token tracking columns for chat messages (idempotent — 1060 catch handles duplicate)
                "ALTER TABLE messages ADD COLUMN TokensIn INT NULL",
                "ALTER TABLE messages ADD COLUMN TokensOut INT NULL",
                // Project document RAG / S3 ingestion tracking
                "ALTER TABLE project_documents ADD COLUMN S3Key VARCHAR(512) NULL",
                "ALTER TABLE project_documents ADD COLUMN IngestionStatus VARCHAR(20) NOT NULL DEFAULT 'none'",
                "ALTER TABLE project_documents ADD COLUMN IngestedAt DATETIME(6) NULL",
                "ALTER TABLE user_assistant_config ADD COLUMN firm_auto_transcript TINYINT(1) NOT NULL DEFAULT 0",
                "ALTER TABLE user_assistant_config ADD COLUMN firm_auto_summary TINYINT(1) NOT NULL DEFAULT 0",
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_completed_at DATETIME(6) NULL",
                "ALTER TABLE users ADD COLUMN IF NOT EXISTS onboarding_step INT NULL"
            };

            foreach (var alterSql in alterStatements)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
                    _logger.LogInformation("Schema migration applied: {Sql}", alterSql);
                }
                catch (MySqlConnector.MySqlException ex) when (ex.Number == 1060 || ex.Number == 1061 || ex.Number == 1091)
                {
                    // 1060 = duplicate column, 1061 = duplicate index, 1091 = can't drop non-existent column — all idempotent
                    _logger.LogInformation("Schema migration already applied (idempotent): {Sql}", alterSql);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Schema migration failed: {Sql}", alterSql);
                    throw;
                }
            }

            // KB S3Key dedup + unique constraint — prevents duplicate S3Key rows in project_documents
            // Step 1: clean up any existing duplicate rows (keep most recent per S3Key)
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    DELETE p1 FROM project_documents p1
                    INNER JOIN project_documents p2
                    WHERE p1.S3Key = p2.S3Key
                    AND p1.UploadedAt < p2.UploadedAt
                    AND p1.Id != p2.Id", cancellationToken);
                _logger.LogInformation("KB S3Key dedup cleanup executed (idempotent)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KB S3Key dedup cleanup failed (non-fatal) — unique constraint add may fail if duplicates remain");
            }

            // Step 2: add unique constraint on S3Key (idempotent — 1061 = constraint already exists)
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    ALTER TABLE project_documents
                    ADD CONSTRAINT uq_project_documents_s3key UNIQUE (S3Key)", cancellationToken);
                _logger.LogInformation("Added unique constraint uq_project_documents_s3key on project_documents.S3Key");
            }
            catch (MySqlConnector.MySqlException ex) when (ex.Number == 1061)
            {
                _logger.LogDebug("uq_project_documents_s3key constraint already exists (idempotent)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add uq_project_documents_s3key unique constraint (non-fatal)");
            }

            // Seed Brave Search MCP server
            try
            {
                var braveId = "00000000-0000-0000-0000-000000000001";

                var braveEndpointUrl = "http://localhost:8080/internal/mcp/brave";

                var braveManifest = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new
                    {
                        Name = "web_search",
                        Description = "Search the web for current, relevant information",
                        InputSchema = System.Text.Json.JsonDocument.Parse(@"{
                          ""type"": ""object"",
                          ""properties"": {
                            ""query"": { ""type"": ""string"", ""description"": ""The search query"" },
                            ""count"": { ""type"": ""integer"", ""description"": ""Number of results (1-10)"", ""default"": 5 }
                          },
                          ""required"": [""query""]
                        }").RootElement
                    }
                });
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO mcp_servers (id, name, slug, description, transport_type, endpoint_url,
                        auth_type, requires_user_auth, is_active, tool_manifest, created_at, updated_at)
                    VALUES ({0}, 'Brave Web Search', 'brave', 'Search the web using Brave Search',
                        'http', {1}, 'api_key', 0, 1, {2},
                        NOW(6), NOW(6))
                    ON DUPLICATE KEY UPDATE
                        endpoint_url = VALUES(endpoint_url),
                        updated_at = NOW(6)
                    """,
                    braveId, braveEndpointUrl, braveManifest);
                _logger.LogInformation("Seeded Brave Search MCP server (endpoint: {Url}).", braveEndpointUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Brave Search seed (non-fatal): {Message}", ex.Message);
            }

            // Seed Azure DevOps MCP server
            // Auth type: "devops_pat" — McpToolService passes userId as X-API-Key;
            // DevOpsMcpAdapter resolves the user's PAT from DevOpsConnectionService.
            // requires_user_auth = 0 (auto-available) but GetConversationToolsAsync
            // gates on DevOpsConnectionService.IsConnectedAsync so only connected users see it.
            try
            {
                var devOpsId = "00000000-0000-0000-0000-000000000002";
                var devOpsEndpointUrl = "http://localhost:8080/internal/mcp/devops";
                var devOpsManifest = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new { Name = "list_devops_projects",  Description = "List all Azure DevOps projects in the user's organization",                            InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{},""required"":[]}").RootElement },
                    new { Name = "get_work_item",         Description = "Get details of a specific Azure DevOps work item by ID",                               InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""id"":{""type"":""integer"",""description"":""Work item ID""}},""required"":[""id""]}").RootElement },
                    new { Name = "query_work_items",      Description = "Query Azure DevOps work items using WIQL or a natural language description",            InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""wiql"":{""type"":""string"",""description"":""WIQL query or natural language description""},""project"":{""type"":""string"",""description"":""Project name (optional)""}},""required"":[""wiql""]}").RootElement },
                    new { Name = "list_repositories",     Description = "List Git repositories in an Azure DevOps project",                                    InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string"",""description"":""Project name""}},""required"":[""project""]}").RootElement },
                    new { Name = "list_pipelines",        Description = "List build/release pipelines in an Azure DevOps project",                             InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string"",""description"":""Project name""}},""required"":[""project""]}").RootElement },
                    new { Name = "trigger_pipeline",      Description = "Trigger a pipeline run in Azure DevOps",                                              InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string""},""pipeline_id"":{""type"":""integer""},""parameters"":{""type"":""object"",""additionalProperties"":{""type"":""string""}}},""required"":[""project"",""pipeline_id""]}").RootElement },
                    new { Name = "create_work_item",      Description = "Create a new work item in Azure DevOps. Params: {project: string, type: string, title: string, description?: string, assignedTo?: string, areaPath?: string, iterationPath?: string, tags?: string}", InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string""},""type"":{""type"":""string"",""description"":""Work item type e.g. Task, Bug, User Story""},""title"":{""type"":""string""},""description"":{""type"":""string""},""assignedTo"":{""type"":""string""},""areaPath"":{""type"":""string""},""iterationPath"":{""type"":""string""},""tags"":{""type"":""string""}},""required"":[""project"",""type"",""title""]}").RootElement },
                    new { Name = "update_work_item",      Description = "Update fields on an existing work item by ID. Params: {id: number, state?: string, title?: string, description?: string, assignedTo?: string, priority?: number, tags?: string}",                       InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""id"":{""type"":""integer""},""state"":{""type"":""string""},""title"":{""type"":""string""},""description"":{""type"":""string""},""assignedTo"":{""type"":""string""},""priority"":{""type"":""integer""},""tags"":{""type"":""string""}},""required"":[""id""]}").RootElement },
                    new { Name = "add_work_item_comment", Description = "Add a comment to a work item. Params: {project: string, id: number, comment: string}",                                                                                                                  InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string""},""id"":{""type"":""integer""},""comment"":{""type"":""string""}},""required"":[""project"",""id"",""comment""]}").RootElement },
                    new { Name = "create_branch",         Description = "Create a new branch from a base ref in a repository. Params: {project: string, repo: string, branchName: string, baseBranch?: string}",                                                                InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string""},""repo"":{""type"":""string""},""branchName"":{""type"":""string""},""baseBranch"":{""type"":""string"",""default"":""main""}},""required"":[""project"",""repo"",""branchName""]}").RootElement },
                    new { Name = "create_pull_request",   Description = "Create a pull request. Params: {project: string, repo: string, title: string, sourceBranch: string, targetBranch: string, description?: string, reviewers?: string[]}",                                 InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string""},""repo"":{""type"":""string""},""title"":{""type"":""string""},""sourceBranch"":{""type"":""string""},""targetBranch"":{""type"":""string""},""description"":{""type"":""string""},""reviewers"":{""type"":""array"",""items"":{""type"":""string""}}},""required"":[""project"",""repo"",""title"",""sourceBranch"",""targetBranch""]}").RootElement },
                    new { Name = "update_pull_request",   Description = "Update a pull request — complete, abandon, add reviewers, or update description. Params: {project: string, repo: string, pullRequestId: number, status?: string, description?: string, reviewers?: string[]}", InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""project"":{""type"":""string""},""repo"":{""type"":""string""},""pullRequestId"":{""type"":""integer""},""status"":{""type"":""string"",""enum"":[""active"",""completed"",""abandoned""]},""description"":{""type"":""string""},""reviewers"":{""type"":""array"",""items"":{""type"":""string""}}},""required"":[""project"",""repo"",""pullRequestId""]}").RootElement }
                });
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO mcp_servers (id, name, slug, description, transport_type, endpoint_url,
                        auth_type, requires_user_auth, is_active, tool_manifest, created_at, updated_at)
                    VALUES ({0}, 'Azure DevOps', 'devops', 'Work items, repos, and pipelines via Azure DevOps REST API',
                        'http', {1}, 'devops_pat', 0, 1, {2},
                        NOW(6), NOW(6))
                    ON DUPLICATE KEY UPDATE
                        endpoint_url = VALUES(endpoint_url),
                        auth_type = VALUES(auth_type),
                        requires_user_auth = VALUES(requires_user_auth),
                        tool_manifest = VALUES(tool_manifest),
                        updated_at = NOW(6)
                    """,
                    devOpsId, devOpsEndpointUrl, devOpsManifest);
                _logger.LogInformation("Seeded Azure DevOps MCP server (endpoint: {Url}).", devOpsEndpointUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Azure DevOps seed (non-fatal): {Message}", ex.Message);
            }

            // Seed Microsoft 365 MCP server
            // Auth type: "m365_token" — McpToolService passes userId as X-API-Key;
            // M365McpAdapter resolves the user's Graph access token from MicrosoftTokenService.
            // requires_user_auth = 0 (auto-available) but GetConversationToolsAsync
            // gates on UserMicrosoftTokens token existence so only connected users see it.
            try
            {
                var m365Id = "00000000-0000-0000-0000-000000000003";
                var m365EndpointUrl = "http://localhost:8080/internal/mcp/m365";
                var m365Manifest = System.Text.Json.JsonSerializer.Serialize(new[]
                {
                    new { Name = "list_emails",           Description = "List recent emails from inbox",                                                        InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""top"":{""type"":""integer"",""default"":10},""filter"":{""type"":""string""}}}").RootElement },
                    new { Name = "get_email",             Description = "Get full content of a specific email by ID",                                           InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""messageId"":{""type"":""string""}},""required"":[""messageId""]}").RootElement },
                    new { Name = "send_email",            Description = "Send an email",                                                                        InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""to"":{""type"":""string""},""subject"":{""type"":""string""},""body"":{""type"":""string""}},""required"":[""to"",""subject"",""body""]}").RootElement },
                    new { Name = "list_calendar_events",  Description = "List upcoming calendar events",                                                        InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""top"":{""type"":""integer"",""default"":10},""startDateTime"":{""type"":""string""},""endDateTime"":{""type"":""string""}}}").RootElement },
                    new { Name = "create_calendar_event", Description = "Create a calendar event",                                                              InputSchema = System.Text.Json.JsonDocument.Parse(@"{""type"":""object"",""properties"":{""subject"":{""type"":""string""},""start"":{""type"":""string""},""end"":{""type"":""string""},""body"":{""type"":""string""},""attendees"":{""type"":""array"",""items"":{""type"":""string""}}},""required"":[""subject"",""start"",""end""]}").RootElement }
                });
                await db.Database.ExecuteSqlRawAsync(
                    """
                    INSERT INTO mcp_servers (id, name, slug, description, transport_type, endpoint_url,
                        auth_type, requires_user_auth, is_active, tool_manifest, created_at, updated_at)
                    VALUES ({0}, 'Microsoft 365', 'm365', 'Email and calendar access via Microsoft Graph',
                        'http', {1}, 'm365_token', 0, 1, {2},
                        NOW(6), NOW(6))
                    ON DUPLICATE KEY UPDATE
                        endpoint_url = VALUES(endpoint_url),
                        auth_type = VALUES(auth_type),
                        requires_user_auth = VALUES(requires_user_auth),
                        tool_manifest = VALUES(tool_manifest),
                        updated_at = NOW(6)
                    """,
                    m365Id, m365EndpointUrl, m365Manifest);
                _logger.LogInformation("Seeded Microsoft 365 MCP server (endpoint: {Url}).", m365EndpointUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Microsoft 365 seed (non-fatal): {Message}", ex.Message);
            }

            // One-time cleanup: delete ghost conversations (created on page load with no messages)
            try
            {
                await db.Database.ExecuteSqlRawAsync(@"
                    DELETE FROM conversations
                    WHERE Id NOT IN (SELECT DISTINCT ConversationId FROM messages)
                    AND CreatedAt < DATE_SUB(NOW(), INTERVAL 5 MINUTE)",
                    cancellationToken);
                _logger.LogInformation("Ghost conversation cleanup completed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ghost conversation cleanup failed — non-fatal");
            }

            // Project clean-slate migration v1 — runs once, guarded by applied_migrations table
            try
            {
                const string migrationName = "project-clean-slate-v1";
                // Use ADO.NET directly — EF Core SqlQueryRaw<int> wraps in subquery which breaks MySQL (t.Value)
                // NOTE: do NOT wrap in `using` — EF Core owns the connection lifecycle
                var conn = db.Database.GetDbConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await conn.OpenAsync(cancellationToken);
                int alreadyRan;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM applied_migrations WHERE name = @name";
                    var param = cmd.CreateParameter();
                    param.ParameterName = "@name";
                    param.Value = migrationName;
                    cmd.Parameters.Add(param);
                    alreadyRan = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }

                if (alreadyRan == 0)
                {
                    _logger.LogInformation("Running project clean slate migration...");

                    // Delete project-linked messages first (FK dependency), then conversations, then project data
                    await db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM messages WHERE ConversationId IN (SELECT Id FROM conversations WHERE ProjectId IS NOT NULL)",
                        cancellationToken);
                    await db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM conversations WHERE ProjectId IS NOT NULL",
                        cancellationToken);
                    await db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM project_documents",
                        cancellationToken);
                    await db.Database.ExecuteSqlRawAsync(
                        "DELETE FROM projects",
                        cancellationToken);
                    await db.Database.ExecuteSqlRawAsync(
                        "INSERT INTO applied_migrations (name) VALUES ('project-clean-slate-v1')",
                        cancellationToken);

                    _logger.LogInformation("Project clean slate migration executed — all project data wiped");

                    // Delete all project-related S3 objects (kb-docs/project/ prefix)
                    try
                    {
                        using var s3Scope = _scopeFactory.CreateScope();
                        var s3 = s3Scope.ServiceProvider.GetRequiredService<Amazon.S3.IAmazonS3>();
                        var listReq = new Amazon.S3.Model.ListObjectsV2Request
                        {
                            BucketName = "fortress-tools",
                            Prefix = "kb-docs/project/"
                        };
                        Amazon.S3.Model.ListObjectsV2Response listResp;
                        do
                        {
                            listResp = await s3.ListObjectsV2Async(listReq, cancellationToken);
                            foreach (var obj in listResp.S3Objects)
                            {
                                await s3.DeleteObjectAsync(new Amazon.S3.Model.DeleteObjectRequest
                                {
                                    BucketName = "fortress-tools",
                                    Key = obj.Key
                                }, cancellationToken);
                            }
                            listReq.ContinuationToken = listResp.NextContinuationToken;
                        } while (listResp.IsTruncated);
                        _logger.LogInformation("Project clean slate: S3 objects deleted from kb-docs/project/");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Project clean slate: S3 cleanup failed (non-fatal)");
                    }

                    // Trigger Bedrock re-ingestion to clear deleted docs from vector index
                    try
                    {
                        using var kbScope = _scopeFactory.CreateScope();
                        var kbDocumentService = kbScope.ServiceProvider.GetRequiredService<KbDocumentService>();
                        await kbDocumentService.StartProjectIngestionAsync();
                        _logger.LogInformation("Project clean slate: Bedrock KB re-ingestion triggered");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Project clean slate: Bedrock re-ingestion failed (non-fatal)");
                    }
                }
                else
                {
                    _logger.LogInformation("Project clean slate migration already applied — skipping");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Project clean slate migration failed (non-fatal)");
            }

            // KB team rename migration — rename kb_projects→kb_teams, kb_project_members→kb_team_members,
            // and rename project_id→team_id columns in kb_team_members and kb_entries
            try
            {
                const string kbRenameMigration = "kb-team-rename-v1";
                // NOTE: do NOT wrap in `using` — EF Core owns the connection lifecycle
                var conn2 = db.Database.GetDbConnection();
                if (conn2.State != System.Data.ConnectionState.Open)
                    await conn2.OpenAsync(cancellationToken);
                int kbRenameAlreadyRan;
                using (var cmd = conn2.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM applied_migrations WHERE name = @name";
                    var param = cmd.CreateParameter();
                    param.ParameterName = "@name";
                    param.Value = kbRenameMigration;
                    cmd.Parameters.Add(param);
                    kbRenameAlreadyRan = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }

                if (kbRenameAlreadyRan == 0)
                {
                    _logger.LogInformation("Running KB team rename migration...");

                    // Rename tables (idempotent — 1050=table exists, 1146=table doesn't exist)
                    var renameSqls = new[]
                    {
                        "ALTER TABLE kb_projects RENAME TO kb_teams",
                        "ALTER TABLE kb_project_members RENAME TO kb_team_members",
                    };
                    foreach (var sql in renameSqls)
                    {
                        try { await db.Database.ExecuteSqlRawAsync(sql, cancellationToken); }
                        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1050 || ex.Number == 1146)
                        { /* table already renamed or doesn't exist — idempotent */ }
                    }

                    // Column renames — per-statement try-catch for idempotency
                    // 1054=unknown column, 1060=duplicate column, 1091=can't drop non-existent
                    var alterSqls = new[]
                    {
                        // kb_team_members: rename project_id → team_id
                        "ALTER TABLE kb_team_members CHANGE COLUMN project_id team_id INT NOT NULL",
                        // kb_entries: rename project_id → team_id
                        "ALTER TABLE kb_entries CHANGE COLUMN project_id team_id INT",
                    };
                    foreach (var sql in alterSqls)
                    {
                        try { await db.Database.ExecuteSqlRawAsync(sql, cancellationToken); }
                        catch (MySqlConnector.MySqlException ex) when (ex.Number == 1054 || ex.Number == 1060 || ex.Number == 1091)
                        { /* column already renamed — idempotent */ }
                    }

                    // Record migration as applied
                    using (var cmd = conn2.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO applied_migrations (name, applied_at) VALUES (@name, NOW())";
                        var param = cmd.CreateParameter();
                        param.ParameterName = "@name";
                        param.Value = kbRenameMigration;
                        cmd.Parameters.Add(param);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    _logger.LogInformation("KB team rename migration completed");
                }
                else
                {
                    _logger.LogInformation("KB team rename migration already applied — skipping");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "KB team rename migration failed (non-fatal)");
            }

            // Migration: make project_documents.ProjectId nullable for personal/team KB tracking
            try
            {
                const string migName = "kb-documents-nullable-projectid-v1";
                var conn3 = db.Database.GetDbConnection();
                if (conn3.State != System.Data.ConnectionState.Open)
                    await conn3.OpenAsync(cancellationToken);
                int alreadyRan;
                using (var cmd = conn3.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM applied_migrations WHERE name = @name";
                    var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = migName;
                    cmd.Parameters.Add(p);
                    alreadyRan = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
                }
                if (alreadyRan == 0)
                {
                    // Make ProjectId nullable for personal/team/corp KB upload tracking.
                    // Must drop FK first — Aurora won't MODIFY a column referenced by a FK.
                    // Drop is idempotent (1091 = constraint doesn't exist).
                    try { await db.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE project_documents DROP FOREIGN KEY FK_project_documents_projects_ProjectId",
                        cancellationToken); }
                    catch (MySqlConnector.MySqlException ex) when (ex.Number == 1091 || ex.Number == 1025)
                    { /* FK already dropped or doesn't exist — idempotent */ }

                    // MODIFY COLUMN to nullable (idempotent — succeeds silently if already nullable)
                    try { await db.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE project_documents MODIFY COLUMN ProjectId char(36) NULL", cancellationToken); }
                    catch (MySqlConnector.MySqlException ex) when (ex.Number == 1060 || ex.Number == 1091)
                    { /* already nullable */ }

                    // Recreate FK as nullable-compatible (ON DELETE SET NULL)
                    try { await db.Database.ExecuteSqlRawAsync(
                        "ALTER TABLE project_documents ADD CONSTRAINT FK_project_documents_projects_ProjectId " +
                        "FOREIGN KEY (ProjectId) REFERENCES projects(Id) ON DELETE SET NULL", cancellationToken); }
                    catch (MySqlConnector.MySqlException ex) when (ex.Number == 1826 || ex.Number == 1061)
                    { /* FK already exists */ }

                    using (var cmd = conn3.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO applied_migrations (name, applied_at) VALUES (@name, NOW())";
                        var p = cmd.CreateParameter(); p.ParameterName = "@name"; p.Value = migName;
                        cmd.Parameters.Add(p);
                        await cmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    _logger.LogInformation("kb-documents-nullable-projectid-v1 migration complete");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "kb-documents-nullable-projectid-v1 migration failed (non-fatal)");
            }

            // Note: RepairPersonalKbMetadataAsync removed — structural isolation in Phase 2b
            // eliminates the need for metadata repair (each KB type now in its own dedicated Bedrock KB).

            _logger.LogInformation("Database initialization complete");
        }
        catch (Exception ex)
        {
            // CRITICAL: Log but do NOT rethrow.
            // An unhandled exception from IHostedService.StartAsync() will crash the host.
            _logger.LogError(ex, "Database initialization failed — app will continue without complete schema");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Database initialization service stopping");
        return Task.CompletedTask;
    }
}
