using MySqlConnector;

namespace RisePortal.Web.Services;

public class DatabaseInitializationService : IHostedService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        IConfiguration config,
        ILogger<DatabaseInitializationService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("RISE: Starting database initialization...");
            var connectionString = _config.GetConnectionString("RnFip");
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogWarning("RISE: No connection string configured — skipping initialization");
                return;
            }

            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);

            var tables = new[]
            {
                ("user_microsoft_tokens", @"CREATE TABLE IF NOT EXISTS user_microsoft_tokens (
                    entra_oid VARCHAR(128) NOT NULL PRIMARY KEY,
                    access_token TEXT NOT NULL,
                    refresh_token TEXT,
                    token_type VARCHAR(50) DEFAULT 'Bearer',
                    expires_at DATETIME NOT NULL,
                    scopes TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci")
            };

            foreach (var (name, sql) in tables)
            {
                try
                {
                    await using var cmd = new MySqlCommand(sql, conn);
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                    _logger.LogInformation("RISE: Table '{TableName}' ensured.", name);
                }
                catch (MySqlException ex) when (ex.Number == 1060 || ex.Number == 1061)
                {
                    _logger.LogInformation("RISE: Table '{TableName}' already exists (expected).", name);
                }
                catch (Exception tableEx)
                {
                    _logger.LogWarning("RISE: Table '{TableName}' creation note: {Message}", name, tableEx.Message);
                }
            }

            _logger.LogInformation("RISE: Database initialization complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RISE: Database initialization failed — app will continue");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
