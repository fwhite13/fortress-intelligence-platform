using Microsoft.EntityFrameworkCore;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.Entities;

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

            // Seed NexusAdmin role for Fred White
            const string fredUpn = "fwhite@fortressaffinitygroup.com";
            var hasAdminRole = await db.NexusUserRoles
                .AnyAsync(r => r.UserUpn == fredUpn && r.Role == NexusRoles.Admin, cancellationToken);
            if (!hasAdminRole)
            {
                db.NexusUserRoles.Add(new NexusUserRole
                {
                    UserUpn = fredUpn,
                    Role = NexusRoles.Admin,
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "system-seed"
                });
                await db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("[NEXUS] Seeded NexusAdmin role for {Upn}", fredUpn);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NEXUS] EF Core migration failed on startup — DB may be unavailable or schema mismatch.");
            // Non-fatal: app continues but DB features will fail
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
