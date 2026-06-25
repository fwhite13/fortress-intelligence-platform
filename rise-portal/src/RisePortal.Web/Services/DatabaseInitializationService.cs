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
                ("DataProtectionKeys", @"CREATE TABLE IF NOT EXISTS DataProtectionKeys (
                    Id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    FriendlyName TEXT NULL,
                    Xml LONGTEXT NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"),

                ("user_microsoft_tokens", @"CREATE TABLE IF NOT EXISTS user_microsoft_tokens (
                    entra_oid VARCHAR(128) NOT NULL PRIMARY KEY,
                    access_token TEXT NOT NULL,
                    refresh_token TEXT,
                    token_type VARCHAR(50) DEFAULT 'Bearer',
                    expires_at DATETIME NOT NULL,
                    scopes TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"),

                // migration: 20260625_RiseAdminAndCardAccess
                ("rise_users", @"CREATE TABLE IF NOT EXISTS rise_users (
                    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    entra_oid VARCHAR(200) NOT NULL,
                    email VARCHAR(200) NULL,
                    display_name VARCHAR(200) NULL,
                    first_login DATETIME NULL,
                    last_login DATETIME NULL,
                    UNIQUE KEY uq_rise_users_oid (entra_oid)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"),

                ("rise_app_cards", @"CREATE TABLE IF NOT EXISTS rise_app_cards (
                    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    name VARCHAR(100) NOT NULL,
                    display_name VARCHAR(200) NULL,
                    url VARCHAR(500) NULL,
                    icon VARCHAR(50) NULL,
                    description TEXT NULL,
                    restricted TINYINT(1) NOT NULL DEFAULT 0,
                    active TINYINT(1) NOT NULL DEFAULT 1,
                    sort_order INT NOT NULL DEFAULT 0,
                    created_at DATETIME NULL,
                    UNIQUE KEY uq_rise_app_cards_name (name)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"),

                ("rise_app_card_access", @"CREATE TABLE IF NOT EXISTS rise_app_card_access (
                    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    app_card_id INT NOT NULL,
                    entra_oid VARCHAR(200) NOT NULL,
                    email VARCHAR(200) NULL,
                    display_name VARCHAR(200) NULL,
                    granted_at DATETIME NULL,
                    granted_by_oid VARCHAR(200) NULL,
                    UNIQUE KEY uq_rise_card_access (app_card_id, entra_oid),
                    CONSTRAINT fk_rise_card_access_card FOREIGN KEY (app_card_id) REFERENCES rise_app_cards (id)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"),

                ("rise_admin_users", @"CREATE TABLE IF NOT EXISTS rise_admin_users (
                    id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    entra_oid VARCHAR(200) NOT NULL,
                    email VARCHAR(200) NULL,
                    display_name VARCHAR(200) NULL,
                    created_at DATETIME NULL,
                    UNIQUE KEY uq_rise_admin_users_oid (entra_oid)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci"),
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

            await SeedDataAsync(conn, cancellationToken);

            _logger.LogInformation("RISE: Database initialization complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RISE: Database initialization failed — app will continue");
        }
    }

    private async Task SeedDataAsync(MySqlConnection conn, CancellationToken cancellationToken)
    {
        var seeds = new[]
        {
            ("rise_app_cards seed",
             @"INSERT IGNORE INTO rise_app_cards (name, display_name, url, icon, description, restricted, active, sort_order, created_at) VALUES
               ('notetaker',         'Refuge Notetaker',  'https://notetaker.refugems.ai',         '📝', 'AI meeting notes and transcription', 0, 1, 10,  NOW()),
               ('conference-room',   'Conference Room',   'https://rooms.refugems.ai/MicrosoftIdentity/Account/SignIn?returnUrl=%2F', '📅', 'Book conference rooms', 0, 1, 20, NOW()),
               ('driver-monitoring', 'Driver Monitoring', 'https://driver-monitoring.refugems.ai', '🕐', 'QTS driver time and fuel analysis',  1, 1, 30,  NOW())"),

            ("rise_admin_users seed",
             @"INSERT IGNORE INTO rise_admin_users (entra_oid, email, display_name, created_at) VALUES
               ('c9c1b329-2eb4-4788-8b4c-c7229a1bac3d', 'fwhite@refugems.com', 'Fred White', NOW())"),

            ("rise_app_card_access seed",
             @"INSERT IGNORE INTO rise_app_card_access (app_card_id, entra_oid, email, display_name, granted_at, granted_by_oid)
               SELECT id, 'c9c1b329-2eb4-4788-8b4c-c7229a1bac3d', 'fwhite@refugems.com', 'Fred White', NOW(), 'c9c1b329-2eb4-4788-8b4c-c7229a1bac3d'
               FROM rise_app_cards WHERE name = 'driver-monitoring'"),
        };

        foreach (var (label, sql) in seeds)
        {
            try
            {
                await using var cmd = new MySqlCommand(sql, conn);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
                _logger.LogInformation("RISE: Seed '{Label}' applied.", label);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("RISE: Seed '{Label}' note: {Message}", label, ex.Message);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
