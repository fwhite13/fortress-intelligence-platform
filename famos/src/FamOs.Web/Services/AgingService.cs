using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

/// <summary>
/// Background service that runs every 15 minutes and sets DominantSignal
/// on opportunities that have been in their current stage too long.
/// Signals set here are overridden at the next manual stage transition.
/// </summary>
public class AgingService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<AgingService> _logger;

    public AgingService(IServiceScopeFactory services, ILogger<AgingService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial delay — let startup complete
        await Task.Delay(TimeSpan.FromSeconds(30), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAgingPassAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Aging] Error during aging pass");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    private async Task RunAgingPassAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        var opps = await db.Opportunities
            .Include(o => o.Submissions)
            .Where(o => !o.IsClosed)
            .ToListAsync();

        var now     = DateTime.UtcNow;
        var updated = 0;

        foreach (var opp in opps)
        {
            var since = opp.LastStageTransitionAt.HasValue
                ? (now - opp.LastStageTransitionAt.Value).TotalDays
                : (now - opp.CreatedAt).TotalDays;

            var newSignal = ComputeAgingSignal(opp, since);
            if (newSignal.HasValue && opp.DominantSignal != newSignal.Value)
            {
                opp.DominantSignal = newSignal.Value;
                opp.UpdatedAt      = now;
                updated++;
            }
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("[Aging] Updated signals on {Count} opportunities", updated);
        }
    }

    private static DominantSignal? ComputeAgingSignal(
        FamOs.Web.Data.Entities.Opportunity opp, double daysSinceTransition)
    {
        return opp.LifecycleStage switch
        {
            LifecycleStage.Intake when daysSinceTransition > 3
                => DominantSignal.FollowUpNeeded,

            LifecycleStage.UnderwritingPrep when daysSinceTransition > 5
                && !opp.Submissions.Any()
                => DominantSignal.WaitingOnUW,

            LifecycleStage.Marketed when daysSinceTransition > 7
                => DominantSignal.WaitingOnCarrier,

            LifecycleStage.QuotesReceived when daysSinceTransition > 3
                => DominantSignal.WaitingOnClient,

            LifecycleStage.ClientDecision when daysSinceTransition > 5
                => DominantSignal.AtRisk,

            LifecycleStage.Binding when daysSinceTransition > 3
                => DominantSignal.Urgent,

            _ => null   // No aging signal for this stage/duration combination
        };
    }
}
