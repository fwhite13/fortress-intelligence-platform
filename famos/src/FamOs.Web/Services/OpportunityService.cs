using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class OpportunityService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;
    private readonly LifecycleCommandService _lifecycle;
    private readonly ILogger<OpportunityService> _logger;

    public OpportunityService(IDbContextFactory<FamOsDbContext> dbFactory,
        LifecycleCommandService lifecycle, ILogger<OpportunityService> logger)
    {
        _dbFactory = dbFactory;
        _lifecycle = lifecycle;
        _logger    = logger;
    }

    public async Task<List<Opportunity>> GetPipelineAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Quotes)
            .Where(o => !o.IsClosed)
            .OrderBy(o => o.UrgencyScore).ThenBy(o => o.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Dictionary<LifecycleStage, List<Opportunity>>> GetPipelineByStageAsync()
    {
        var all = await GetPipelineAsync();
        return all
            .GroupBy(o => o.LifecycleStage)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<Opportunity?> GetByIdAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Submissions)
            .Include(o => o.Quotes)
            .Include(o => o.Proposals)
            .Include(o => o.PolicyShadow)
            .Include(o => o.Activities.OrderByDescending(a => a.OccurredAt).Take(50))
            .Include(o => o.Tasks.Where(t => t.Status == "open"))
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Guid> CreateOpportunityAsync(string name, string ownerUserId,
        decimal? estimatedPremium, DateOnly? effectiveDateTarget)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var opp = new Opportunity
        {
            Name                 = name,
            LifecycleStage       = LifecycleStage.Intake,
            DominantSignal       = DominantSignal.WaitingOnClient,
            DominantSignalReason = "Awaiting required intake information",
            OwnerUserId          = ownerUserId,
            EstimatedPremium     = estimatedPremium,
            EffectiveDateTarget  = effectiveDateTarget,
        };
        db.Opportunities.Add(opp);

        db.Activities.Add(new Activity {
            OpportunityId = opp.Id,
            EventType     = "opportunity_created",
            Description   = $"Opportunity created: {name}",
            ActorUserId   = ownerUserId,
        });

        await db.SaveChangesAsync();
        _logger.LogInformation("[FAM OS] Opportunity created: {Id} — {Name}", opp.Id, name);
        return opp.Id;
    }

    public async Task<DashboardSummary> GetDashboardSummaryAsync(string? ownerUserId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.Opportunities.Where(o => !o.IsClosed);
        if (!string.IsNullOrEmpty(ownerUserId))
            query = query.Where(o => o.OwnerUserId == ownerUserId);

        var opps = await query.ToListAsync();

        return new DashboardSummary
        {
            TotalActive    = opps.Count,
            TimeRiskCount  = opps.Count(o => o.DominantSignal == DominantSignal.TimeRisk),
            DecisionNeeded = opps.Count(o => o.DominantSignal is
                DominantSignal.DecisionRequired or DominantSignal.AwaitingClientDecision),
            BindingCount   = opps.Count(o => o.LifecycleStage == LifecycleStage.Binding),
            BoundThisMonth = opps.Count(o =>
                o.LifecycleStage == LifecycleStage.Bound &&
                o.UpdatedAt >= DateTime.UtcNow.AddDays(-30)),
        };
    }
}

public record DashboardSummary
{
    public int TotalActive    { get; init; }
    public int TimeRiskCount  { get; init; }
    public int DecisionNeeded { get; init; }
    public int BindingCount   { get; init; }
    public int BoundThisMonth { get; init; }
}
