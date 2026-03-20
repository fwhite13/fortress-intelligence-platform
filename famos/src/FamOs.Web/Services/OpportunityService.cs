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
            .Include(o => o.Contacts)
            .Include(o => o.Documents)
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

        var query = db.Opportunities.AsQueryable();
        if (!string.IsNullOrEmpty(ownerUserId))
            query = query.Where(o => o.OwnerUserId == ownerUserId);

        var all = await query
            .Include(o => o.Flags)
            .Where(o => !o.IsClosed)
            .ToListAsync();

        var urgentSignals = new[]
        {
            DominantSignal.Urgent, DominantSignal.AtRisk, DominantSignal.TimeRisk
        };

        var recent = await db.Activities
            .OrderByDescending(a => a.OccurredAt)
            .Take(5)
            .ToListAsync();

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        return new DashboardSummary
        {
            TotalActive      = all.Count,
            TimeRiskCount    = all.Count(o => urgentSignals.Contains(o.DominantSignal)),
            DecisionNeeded   = all.Count(o =>
                o.LifecycleStage is LifecycleStage.ClientDecision or LifecycleStage.Binding),
            BoundThisMonth   = await db.Opportunities
                .CountAsync(o => o.LifecycleStage == LifecycleStage.Bound
                    && o.UpdatedAt >= monthStart),
            TotalPremiumAtRisk = all
                .Where(o => o.EstimatedPremium.HasValue)
                .Sum(o => o.EstimatedPremium!.Value),
            UrgentOpportunities = all
                .Where(o => urgentSignals.Contains(o.DominantSignal))
                .OrderByDescending(o => o.DominantSignal == DominantSignal.Urgent ? 2
                                      : o.DominantSignal == DominantSignal.AtRisk  ? 1 : 0)
                .Take(10)
                .ToList(),
            ByStage        = all.GroupBy(o => o.LifecycleStage)
                .ToDictionary(g => g.Key, g => g.Count()),
            RecentActivity = recent,
        };
    }
}

public class DashboardSummary
{
    public int TotalActive            { get; set; }
    public int TimeRiskCount          { get; set; }
    public int DecisionNeeded         { get; set; }
    public int BoundThisMonth         { get; set; }
    public decimal TotalPremiumAtRisk { get; set; }

    // Urgent/at-risk strip
    public List<Opportunity> UrgentOpportunities { get; set; } = new();

    // Pipeline distribution
    public Dictionary<LifecycleStage, int> ByStage { get; set; } = new();

    // Recent activity
    public List<Activity> RecentActivity { get; set; } = new();
}
