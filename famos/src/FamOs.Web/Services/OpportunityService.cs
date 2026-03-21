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
    private readonly UserAffinityService _affinity;

    public OpportunityService(IDbContextFactory<FamOsDbContext> dbFactory,
        LifecycleCommandService lifecycle, ILogger<OpportunityService> logger,
        UserAffinityService affinity)
    {
        _dbFactory = dbFactory;
        _lifecycle = lifecycle;
        _logger    = logger;
        _affinity  = affinity;
    }

    public async Task<List<Opportunity>> GetPipelineAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
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
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    /// <summary>
    /// Lean load — used by Dashboard urgent list, Task Center opportunity refs.
    /// Does NOT include all navigation properties. Never use for workspace display.
    /// </summary>
    public async Task<Opportunity?> GetByIdLeanAsync(Guid id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public const int PipelinePageSize = 25;

    /// <summary>
    /// Returns one page of opportunities for a single lifecycle stage.
    /// Used by Pipeline board per-column pagination.
    /// </summary>
    public async Task<OpportunityPage> GetStagePageAsync(
        LifecycleStage stage, int pageIndex, string? affinityId = null, string? search = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Where(o => !o.IsClosed && o.LifecycleStage == stage);

        if (!string.IsNullOrEmpty(affinityId))
            query = query.Where(o => o.AffinityId == affinityId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.Name.Contains(search));

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(o => o.UrgencyScore)
            .ThenByDescending(o => o.UpdatedAt)
            .Skip(pageIndex * PipelinePageSize)
            .Take(PipelinePageSize)
            .ToListAsync();

        return new OpportunityPage
        {
            Items      = items,
            TotalCount = total,
            PageIndex  = pageIndex,
            PageSize   = PipelinePageSize,
            HasMore    = (pageIndex + 1) * PipelinePageSize < total,
        };
    }

    /// <summary>
    /// Returns paginated stage counts for all pipeline columns (cheap query — counts only).
    /// </summary>
    public async Task<Dictionary<LifecycleStage, int>> GetStageSummaryAsync(string? affinityId = null, string? search = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.Opportunities.Where(o => !o.IsClosed);
        if (!string.IsNullOrEmpty(affinityId))
            query = query.Where(o => o.AffinityId == affinityId);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.Name.Contains(search));

        return await query
            .GroupBy(o => o.LifecycleStage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Stage, x => x.Count);
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
            AffinityId           = await _affinity.GetCurrentAffinityIdAsync(),
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

        var urgentSignals = new[]
        {
            DominantSignal.Urgent, DominantSignal.AtRisk, DominantSignal.TimeRisk
        };

        var baseQuery = db.Opportunities.Where(o => !o.IsClosed);
        if (!string.IsNullOrEmpty(ownerUserId))
            baseQuery = baseQuery.Where(o => o.OwnerUserId == ownerUserId);

        // All aggregations as separate DB queries — each is cheap (indexed)
        var totalActive     = await baseQuery.CountAsync();
        var timeRiskCount   = await baseQuery.CountAsync(o => urgentSignals.Contains(o.DominantSignal));
        var decisionNeeded  = await baseQuery.CountAsync(o =>
            o.LifecycleStage == LifecycleStage.ClientDecision
            || o.LifecycleStage == LifecycleStage.Binding);
        var monthStart      = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var boundThisMonth  = await db.Opportunities.CountAsync(o =>
            o.LifecycleStage == LifecycleStage.Bound && o.UpdatedAt >= monthStart);
        var totalPremium    = await baseQuery
            .Where(o => o.EstimatedPremium.HasValue)
            .SumAsync(o => o.EstimatedPremium!.Value);
        var byStage         = await baseQuery
            .GroupBy(o => o.LifecycleStage)
            .Select(g => new { Stage = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Stage, x => x.Count);

        // Urgent list: load only top 10, with flags
        var urgentOpps = await baseQuery
            .Where(o => urgentSignals.Contains(o.DominantSignal))
            .Include(o => o.Flags.Where(f => f.IsActive))
            .OrderByDescending(o =>
                o.DominantSignal == DominantSignal.Urgent ? 2
                : o.DominantSignal == DominantSignal.AtRisk ? 1 : 0)
            .Take(10)
            .ToListAsync();

        // Recent activity: last 5, global (not filtered by owner)
        var recentActivity = await db.Activities
            .OrderByDescending(a => a.OccurredAt)
            .Take(5)
            .ToListAsync();

        // Premium by stage
        var premiumByStage = await baseQuery
            .Where(o => o.EstimatedPremium.HasValue)
            .GroupBy(o => o.LifecycleStage)
            .Select(g => new { Stage = g.Key, Total = g.Sum(o => o.EstimatedPremium!.Value) })
            .ToDictionaryAsync(x => x.Stage, x => x.Total);

        // Stale deals — based on UpdatedAt
        var staleThreshold  = DateTime.UtcNow.AddDays(-14);
        var urgentThreshold = DateTime.UtcNow.AddDays(-21);
        var now = DateTime.UtcNow;
        var staleRaw = await baseQuery
            .Where(o => o.UpdatedAt < staleThreshold)
            .OrderBy(o => o.UpdatedAt)
            .Take(8)
            .Select(o => new { o.Id, o.Name, o.LifecycleStage, o.UpdatedAt })
            .ToListAsync();
        var staleOpps = staleRaw.Select(o => new StaleOpportunity
        {
            Id        = o.Id,
            Name      = o.Name,
            Stage     = o.LifecycleStage,
            DaysStale = (int)(now - o.UpdatedAt).TotalDays,
            IsUrgent  = o.UpdatedAt < urgentThreshold,
        }).ToList();

        return new DashboardSummary
        {
            TotalActive          = totalActive,
            TimeRiskCount        = timeRiskCount,
            DecisionNeeded       = decisionNeeded,
            BoundThisMonth       = boundThisMonth,
            TotalPremiumAtRisk   = totalPremium,
            UrgentOpportunities  = urgentOpps,
            ByStage              = byStage,
            RecentActivity       = recentActivity,
            PremiumByStage       = premiumByStage,
            StaleDeals           = staleOpps,
        };
    }
}

public class OpportunityPage
{
    public List<Opportunity> Items      { get; init; } = new();
    public int  TotalCount              { get; init; }
    public int  PageIndex               { get; init; }
    public int  PageSize                { get; init; }
    public bool HasMore                 { get; init; }
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

    // Premium by stage
    public Dictionary<LifecycleStage, decimal> PremiumByStage { get; set; } = new();

    // Stale deals
    public List<StaleOpportunity> StaleDeals { get; set; } = new();
}

public class StaleOpportunity
{
    public Guid   Id        { get; set; }
    public string Name      { get; set; } = "";
    public LifecycleStage Stage { get; set; }
    public int    DaysStale { get; set; }
    public bool   IsUrgent  { get; set; }  // true if 21+ days stale
}
