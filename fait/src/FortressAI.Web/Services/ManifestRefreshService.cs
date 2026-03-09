namespace FortressAI.Web.Services;

public class ManifestRefreshService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ManifestRefreshService> _logger;
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    public ManifestRefreshService(IServiceProvider services, ILogger<ManifestRefreshService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for startup
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAllManifestsAsync(stoppingToken);
            await Task.Delay(RefreshInterval, stoppingToken);
        }
    }

    private async Task RefreshAllManifestsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IMcpRegistryService>();
            var servers = await registry.GetActiveServersAsync();

            foreach (var server in servers)
            {
                if (ct.IsCancellationRequested) break;
                // Skip servers that require user auth — tools are populated per-user at call time
                if (server.RequiresUserAuth)
                {
                    _logger.LogDebug("Skipping manifest refresh for {Slug} — requires user auth", server.Slug);
                    continue;
                }
                try
                {
                    await registry.RefreshToolManifestAsync(server.Id);
                    _logger.LogDebug("Refreshed tool manifest for server {ServerSlug}", server.Slug);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh manifest for server {ServerSlug} (non-fatal)", server.Slug);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ManifestRefreshService refresh cycle failed (non-fatal)");
        }
    }
}
