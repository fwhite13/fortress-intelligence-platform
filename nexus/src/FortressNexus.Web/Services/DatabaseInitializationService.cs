using Microsoft.EntityFrameworkCore;
using FortressNexus.Web.Data;

namespace FortressNexus.Web.Services;

/// <summary>
/// Runs EF Core migrations on startup so the database schema is always current.
/// Uses NexusDbContext (reads FIP_DB_NAME env var — correct production database).
/// </summary>
public class DatabaseInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializationService> _logger;

    public DatabaseInitializationService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("[NEXUS] Running EF Core migrations on startup...");
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NexusDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("[NEXUS] EF Core migrations complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NEXUS] EF Core migration failed on startup — DB may be unavailable or schema mismatch.");
            // Non-fatal: app continues but DB features will fail
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
