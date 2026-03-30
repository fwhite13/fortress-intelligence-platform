using MySqlConnector;

namespace FortressNexus.Web.Services;

public class DatabaseInitializationService : IHostedService
{
    private readonly ILogger<DatabaseInitializationService> _logger;
    private readonly IConfiguration _configuration;

    public DatabaseInitializationService(
        ILogger<DatabaseInitializationService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("NEXUS DatabaseInitializationService starting...");

        var dbHost = _configuration["NEXUS_DB_HOST"]
            ?? _configuration["FORTRESS_DB_HOST"]
            ?? "localhost";
        var dbUser = _configuration["NEXUS_DB_USER"]
            ?? _configuration["FORTRESS_DB_USER"]
            ?? "root";
        var dbPassword = _configuration["NEXUS_DB_PASSWORD"]
            ?? _configuration["FORTRESS_DB_PASS"]
            ?? "dev";

        var csb = new MySqlConnectionStringBuilder
        {
            Server = dbHost,
            Port = 3306,
            Database = "nexus_db",
            UserID = dbUser,
            Password = dbPassword,
            GuidFormat = MySqlGuidFormat.None,
            AllowPublicKeyRetrieval = true,
            SslMode = MySqlSslMode.None,
            ConnectionTimeout = 10
        };

        try
        {
            await using var conn = new MySqlConnection(csb.ConnectionString);
            await conn.OpenAsync(cancellationToken);
            _logger.LogInformation("Connected to nexus_db successfully.");

            var tables = new[]
            {
                ("uploaded_files", @"CREATE TABLE IF NOT EXISTS uploaded_files (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    original_file_name VARCHAR(255) NOT NULL,
                    content_type VARCHAR(100) NOT NULL,
                    file_size_bytes BIGINT NOT NULL,
                    s3_key VARCHAR(500) NOT NULL,
                    s3_bucket VARCHAR(100) NOT NULL,
                    uploaded_by VARCHAR(100) NOT NULL,
                    uploaded_at DATETIME NOT NULL DEFAULT UTC_TIMESTAMP(),
                    processed_text LONGTEXT NULL,
                    INDEX idx_uploaded_by (uploaded_by)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"),
                ("submissions", @"CREATE TABLE IF NOT EXISTS submissions (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    title VARCHAR(200) NOT NULL,
                    feature_area VARCHAR(100) NULL,
                    narrative_text LONGTEXT NOT NULL,
                    mockup_file_id INT NOT NULL,
                    submitted_by VARCHAR(100) NOT NULL,
                    submitted_at DATETIME NOT NULL DEFAULT UTC_TIMESTAMP(),
                    status ENUM('Draft','AwaitingReview','Approved','ArtifactsCreated') NOT NULL DEFAULT 'Draft',
                    active_spec_document_id INT NULL,
                    FOREIGN KEY (mockup_file_id) REFERENCES uploaded_files(id),
                    INDEX idx_submitted_by (submitted_by),
                    INDEX idx_status (status)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"),
                ("spec_documents", @"CREATE TABLE IF NOT EXISTS spec_documents (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    submission_id INT NOT NULL,
                    version INT NOT NULL DEFAULT 1,
                    content LONGTEXT NOT NULL,
                    generated_at DATETIME NOT NULL DEFAULT UTC_TIMESTAMP(),
                    generated_by VARCHAR(100) NOT NULL,
                    edited_content LONGTEXT NULL,
                    edited_at DATETIME NULL,
                    edited_by VARCHAR(100) NULL,
                    is_approved TINYINT(1) NOT NULL DEFAULT 0,
                    approved_at DATETIME NULL,
                    approved_by VARCHAR(100) NULL,
                    prompt_tokens_used INT NOT NULL DEFAULT 0,
                    completion_tokens_used INT NOT NULL DEFAULT 0,
                    FOREIGN KEY (submission_id) REFERENCES submissions(id),
                    INDEX idx_submission_id (submission_id)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"),
                ("artifact_sets", @"CREATE TABLE IF NOT EXISTS artifact_sets (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    spec_document_id INT NOT NULL,
                    ado_organization VARCHAR(200) NOT NULL,
                    ado_project_name VARCHAR(200) NOT NULL,
                    ado_project_id VARCHAR(50) NULL,
                    process_template_type_id VARCHAR(50) NOT NULL,
                    created_at DATETIME NOT NULL DEFAULT UTC_TIMESTAMP(),
                    created_by VARCHAR(100) NOT NULL,
                    status ENUM('Pending','InProgress','Success','PartialFailure','Failed') NOT NULL DEFAULT 'Pending',
                    error_detail LONGTEXT NULL,
                    FOREIGN KEY (spec_document_id) REFERENCES spec_documents(id)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4"),
                ("work_item_records", @"CREATE TABLE IF NOT EXISTS work_item_records (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    artifact_set_id INT NOT NULL,
                    ado_work_item_id INT NOT NULL,
                    ado_work_item_url VARCHAR(500) NOT NULL,
                    work_item_type VARCHAR(50) NOT NULL,
                    title VARCHAR(500) NOT NULL,
                    status ENUM('Created','Failed') NOT NULL DEFAULT 'Created',
                    error_detail VARCHAR(1000) NULL,
                    FOREIGN KEY (artifact_set_id) REFERENCES artifact_sets(id)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4")
            };

            foreach (var (tableName, ddl) in tables)
            {
                try
                {
                    await using var cmd = new MySqlCommand(ddl, conn);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    _logger.LogInformation("Table ensured: {Table}", tableName);
                }
                catch (MySqlException ex) when (ex.Number == 1060)
                {
                    // Column already exists — idempotent, safe to ignore
                    _logger.LogDebug("Table {Table}: column already exists (1060), continuing.", tableName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create table {Table}", tableName);
                }
            }

            _logger.LogInformation("NEXUS database initialization complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NEXUS database initialization failed — cannot connect to nexus_db. App will start but DB-dependent features will fail.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
