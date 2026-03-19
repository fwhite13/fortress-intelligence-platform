using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class SignalRecomputeService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<SignalRecomputeService> _logger;

    public SignalRecomputeService(IServiceScopeFactory services,
        ILogger<SignalRecomputeService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RecomputeAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalRecompute] Error during recompute run");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    private async Task RecomputeAllAsync()
    {
        using var scope = _services.CreateScope();
        var db       = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<SignalResolver>();

        var opps = await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Quotes)
            .Where(o => !o.IsClosed)
            .ToListAsync();

        int changed = 0;
        foreach (var opp in opps)
        {
            var (signal, reason) = resolver.Resolve(opp);
            if (opp.DominantSignal != signal || opp.DominantSignalReason != reason)
            {
                opp.DominantSignal       = signal;
                opp.DominantSignalReason = reason;
                opp.UpdatedAt            = DateTime.UtcNow;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("[SignalRecompute] Updated {Count}/{Total} opportunities",
                changed, opps.Count);
        }
    }
}
