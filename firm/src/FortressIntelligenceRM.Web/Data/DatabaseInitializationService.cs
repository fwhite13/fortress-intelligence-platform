using FortressIntelligenceRM.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using MySqlConnector;

namespace FortressIntelligenceRM.Web.Data;

public class DatabaseInitializationService : IHostedService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        IDbContextFactory<FirmDbContext> dbFactory,
        ILogger<DatabaseInitializationService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("FIRM: Starting database initialization...");
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                _logger.LogError("FIRM: Cannot connect to database — skipping initialization");
                return;
            }

            // Create all EF Core model tables
            try
            {
                var creator = db.Database.GetService<IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync(cancellationToken);
                _logger.LogInformation("FIRM: DB tables ensured via EF Core.");
            }
            catch (Exception efEx)
            {
                _logger.LogWarning("FIRM: CreateTablesAsync encountered errors (non-fatal): {Message}", efEx.Message);
            }

            // Ensure all FIRM tables explicitly with IF NOT EXISTS
            var extraTables = new[]
            {
                ("firm_users", @"CREATE TABLE IF NOT EXISTS firm_users (
                    id CHAR(36) NOT NULL PRIMARY KEY,
                    entra_oid VARCHAR(128) NOT NULL,
                    email VARCHAR(256) NOT NULL,
                    display_name VARCHAR(255) NOT NULL,
                    is_active TINYINT(1) NOT NULL DEFAULT 1,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    last_login_at DATETIME NULL,
                    UNIQUE INDEX idx_firm_users_oid (entra_oid),
                    UNIQUE INDEX idx_firm_users_email (email)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("firm_meetings", @"CREATE TABLE IF NOT EXISTS firm_meetings (
                    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    title VARCHAR(500) NULL,
                    platform VARCHAR(20) NOT NULL DEFAULT 'teams',
                    meeting_url VARCHAR(2000) NULL,
                    status VARCHAR(20) NOT NULL DEFAULT 'Joining',
                    error_message TEXT NULL,
                    scheduled_at DATETIME NULL,
                    started_at DATETIME NULL,
                    ended_at DATETIME NULL,
                    duration_seconds INT NULL,
                    audio_s3_key VARCHAR(1000) NULL,
                    transcript_s3_key VARCHAR(1000) NULL,
                    bot_task_arn VARCHAR(500) NULL,
                    created_by CHAR(36) NOT NULL,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_fm_created_by (created_by),
                    INDEX idx_fm_status (status),
                    INDEX idx_fm_created_at (created_at)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("firm_meeting_participants", @"CREATE TABLE IF NOT EXISTS firm_meeting_participants (
                    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    meeting_id BIGINT NOT NULL,
                    display_name VARCHAR(255) NOT NULL,
                    speaker_label VARCHAR(20) NULL,
                    email VARCHAR(255) NULL,
                    joined_at DATETIME NULL,
                    INDEX idx_fmp_meeting (meeting_id)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("firm_meeting_transcripts", @"CREATE TABLE IF NOT EXISTS firm_meeting_transcripts (
                    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    meeting_id BIGINT NOT NULL,
                    speaker_label VARCHAR(20) NULL,
                    speaker_name VARCHAR(255) NULL,
                    text TEXT NOT NULL,
                    start_time_ms BIGINT NULL,
                    end_time_ms BIGINT NULL,
                    is_partial TINYINT(1) NOT NULL DEFAULT 0,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    INDEX idx_fmt_meeting (meeting_id)
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"),
                ("firm_meeting_summaries", @"CREATE TABLE IF NOT EXISTS firm_meeting_summaries (
                    id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    meeting_id BIGINT NOT NULL UNIQUE,
                    summary_text TEXT NULL,
                    action_items_json JSON NULL,
                    key_decisions_json JSON NULL,
                    follow_ups_json JSON NULL,
                    model_used VARCHAR(100) NULL,
                    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                ) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci")
            };

            foreach (var (name, sql) in extraTables)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
                    _logger.LogInformation("FIRM: Table '{TableName}' ensured.", name);
                }
                catch (MySqlException ex) when (ex.Number == 1060 || ex.Number == 1061)
                {
                    _logger.LogInformation("FIRM: Table '{TableName}' already exists (expected).", name);
                }
                catch (Exception tableEx)
                {
                    _logger.LogWarning("FIRM: Table '{TableName}' creation note: {Message}", name, tableEx.Message);
                }
            }

            // Schema column additions — idempotent ALTER TABLE statements
            var alterStatements = new[]
            {
                "ALTER TABLE firm_users ADD COLUMN fait_user_id CHAR(36) NULL",
                "ALTER TABLE firm_meetings ADD COLUMN transcript_kb_pushed TINYINT(1) NOT NULL DEFAULT 0",
                "ALTER TABLE firm_meetings ADD COLUMN summary_kb_pushed TINYINT(1) NOT NULL DEFAULT 0"
            };

            foreach (var alterSql in alterStatements)
            {
                try
                {
                    await db.Database.ExecuteSqlRawAsync(alterSql, cancellationToken);
                    _logger.LogInformation("FIRM: Schema migration applied: {Sql}", alterSql);
                }
                catch (MySqlException ex) when (ex.Number == 1060 || ex.Number == 1061 || ex.Number == 1091)
                {
                    _logger.LogInformation("FIRM: Schema migration already applied (idempotent): {Sql}", alterSql);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("FIRM: Schema migration failed (non-fatal): {Message}", ex.Message);
                }
            }

            _logger.LogInformation("FIRM: Database initialization complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Database initialization failed — app will continue");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
