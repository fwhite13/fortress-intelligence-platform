using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class DashboardService
{
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;
    private readonly UserAffinityService _affinity;
    private readonly ILogger<DashboardService> _logger;

    public DashboardService(
        IDbContextFactory<FamOsDbContext> dbFactory,
        UserAffinityService affinity,
        ILogger<DashboardService> logger)
    {
        _dbFactory = dbFactory;
        _affinity = affinity;
        _logger = logger;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var affinityId = await _affinity.GetCurrentAffinityIdAsync();
        var now = DateTime.UtcNow;
        var yearStart = new DateTime(now.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var staleThreshold = now.AddDays(-7);

        // Active pipeline (not Bound, not ClosedNotBound)
        var activeStages = new[] {
            LifecycleStage.Intake,
            LifecycleStage.UnderwritingPrep,
            LifecycleStage.Marketed,
            LifecycleStage.QuotesReceived,
            LifecycleStage.ClientDecision,
            LifecycleStage.Binding
        };

        var activeOpps = await db.Opportunities
            .Where(o => !o.IsClosed && activeStages.Contains(o.LifecycleStage) && o.AffinityId == affinityId)
            .Select(o => new {
                o.Id, o.Name, o.LifecycleStage, o.LastStageTransitionAt, o.EstimatedPremium,
                o.DominantSignal, o.UpdatedAt, o.CreatedAt
            })
            .ToListAsync();

        var activePipelineCount = activeOpps.Count;

        // Stale count: LastStageTransitionAt (or UpdatedAt) > 7 days ago
        var staleCount = activeOpps.Count(o =>
            (o.LastStageTransitionAt ?? o.UpdatedAt) < staleThreshold);

        // Submission queue: opportunities in UnderwritingPrep or Marketed stage
        var submissionQueueCount = await db.Opportunities
            .Where(o => !o.IsClosed && o.AffinityId == affinityId &&
                (o.LifecycleStage == LifecycleStage.UnderwritingPrep ||
                 o.LifecycleStage == LifecycleStage.Marketed))
            .CountAsync();

        // Bound Premium YTD
        var boundPremiumYtd = await db.Opportunities
            .Where(o => o.LifecycleStage == LifecycleStage.Bound &&
                        o.AffinityId == affinityId &&
                        o.UpdatedAt >= yearStart &&
                        o.EstimatedPremium.HasValue)
            .SumAsync(o => o.EstimatedPremium ?? 0);

        // Bound Premium last year (for comparison)
        var lastYearStart = new DateTime(now.Year - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastYearEnd = new DateTime(now.Year - 1, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);
        var boundPremiumLastYear = await db.Opportunities
            .Where(o => o.LifecycleStage == LifecycleStage.Bound &&
                        o.AffinityId == affinityId &&
                        o.UpdatedAt >= lastYearStart && o.UpdatedAt <= lastYearEnd &&
                        o.EstimatedPremium.HasValue)
            .SumAsync(o => o.EstimatedPremium ?? 0);

        // All accounts
        var totalAccountCount = await db.Accounts
            .Where(a => a.AffinityId == affinityId)
            .CountAsync();

        var newAccountsYtd = await db.Accounts
            .Where(a => a.AffinityId == affinityId && a.CreatedAt >= yearStart)
            .CountAsync();

        // Pipeline by stage
        var pipelineByStage = activeOpps
            .GroupBy(o => o.LifecycleStage)
            .Select(g => new PipelineStageSummaryDto
            {
                Stage = g.Key,
                Count = g.Count(),
                TotalPremium = g.Sum(o => o.EstimatedPremium ?? 0)
            })
            .OrderBy(s => (int)s.Stage)
            .ToList();

        // Accounts to route (max 6)
        var accountsToRoute = await BuildAccountsToRouteAsync(db, affinityId);

        // Next tasks
        var nextTasks = BuildNextTasks(activeOpps.Select(o => new OpportunitySlim
        {
            Id = o.Id,
            Name = o.Name,
            LifecycleStage = o.LifecycleStage,
            DominantSignal = o.DominantSignal,
            LastStageTransitionAt = o.LastStageTransitionAt,
            UpdatedAt = o.UpdatedAt
        }).ToList(), now);

        // Recent activity from Activities table (filtered to current affinity)
        var recentActivity = await db.Activities
            .Where(a => a.Opportunity.AffinityId == affinityId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(15)
            .Select(a => new ActivityEventDto
            {
                OpportunityId = a.OpportunityId,
                Title = a.Description ?? "Activity",
                Description = a.EventType,
                OccurredAt = a.OccurredAt,
                EventType = a.EventType
            })
            .ToListAsync();

        // Set dot colors based on event type
        foreach (var evt in recentActivity)
        {
            evt.DotColor = GetEventDotColor(evt.EventType);
        }

        return new DashboardSummaryDto
        {
            ActivePipelineCount = activePipelineCount,
            StaleCount = staleCount,
            SubmissionQueueCount = submissionQueueCount,
            BoundPremiumYtd = boundPremiumYtd,
            BoundPremiumLastYear = boundPremiumLastYear,
            TotalAccountCount = totalAccountCount,
            NewAccountsYtd = newAccountsYtd,
            PipelineByStage = pipelineByStage,
            AccountsToRoute = accountsToRoute,
            NextTasks = nextTasks,
            RecentActivity = recentActivity
        };
    }

    private async Task<List<RoutingAccountDto>> BuildAccountsToRouteAsync(FamOsDbContext db, string affinityId)
    {
        var results = new List<RoutingAccountDto>();

        // 1. QuotesReceived stage → ready to compare
        var quotedOpps = await db.Opportunities
            .Where(o => !o.IsClosed && o.AffinityId == affinityId &&
                        o.LifecycleStage == LifecycleStage.QuotesReceived)
            .Take(3)
            .ToListAsync();

        foreach (var opp in quotedOpps)
        {
            results.Add(new RoutingAccountDto
            {
                OpportunityId = opp.Id,
                CompanyName = opp.Name,
                Description = "Quotes in · Ready to compare",
                Destination = "→ Quote Comparison",
                CtaText = "Compare",
                CtaUrl = $"/quote-comparison/{opp.Id}",
                Priority = 1
            });
        }

        // 2. UnderwritingPrep without submissions → ready to submit
        var uwPrepOpps = await db.Opportunities
            .Include(o => o.Submissions)
            .Where(o => !o.IsClosed && o.AffinityId == affinityId &&
                        o.LifecycleStage == LifecycleStage.UnderwritingPrep)
            .Take(6)
            .ToListAsync();

        foreach (var opp in uwPrepOpps.Where(o => !o.Submissions.Any()).Take(3))
        {
            results.Add(new RoutingAccountDto
            {
                OpportunityId = opp.Id,
                CompanyName = opp.Name,
                Description = "App review complete · Ready to submit",
                Destination = "→ Submission Queue",
                CtaText = "Submit",
                CtaUrl = "/submission-queue",
                Priority = 2
            });
        }

        // 3. Intake stage (prospect) → awaiting review
        var intakeOpps = await db.Opportunities
            .Where(o => !o.IsClosed && o.AffinityId == affinityId &&
                        o.LifecycleStage == LifecycleStage.Intake)
            .Take(3)
            .ToListAsync();

        foreach (var opp in intakeOpps)
        {
            results.Add(new RoutingAccountDto
            {
                OpportunityId = opp.Id,
                CompanyName = opp.Name,
                Description = "Intake complete · Awaiting review",
                Destination = "→ App Review",
                CtaText = "View",
                CtaUrl = $"/pipeline?opp={opp.Id}",
                Priority = 4
            });
        }

        return results.OrderBy(r => r.Priority).Take(6).ToList();
    }

    private List<NextTaskDto> BuildNextTasks(List<OpportunitySlim> opps, DateTime now)
    {
        var tasks = new List<NextTaskDto>();

        foreach (var opp in opps)
        {
            var stageAge = opp.LastStageTransitionAt.HasValue
                ? (now - opp.LastStageTransitionAt.Value).TotalDays
                : (now - opp.UpdatedAt).TotalDays;

            // Decision Required → URGENT
            if (opp.DominantSignal == DominantSignal.DecisionRequired)
            {
                tasks.Add(new NextTaskDto
                {
                    OpportunityId = opp.Id,
                    Priority = "URGENT",
                    TaskName = $"Follow up — {TruncateName(opp.Name)}",
                    Description = "Decision required",
                    CtaText = "Call",
                    PriorityOrder = 1
                });
            }
            // QuotesReceived stage → URGENT (compare quotes)
            else if (opp.LifecycleStage == LifecycleStage.QuotesReceived)
            {
                tasks.Add(new NextTaskDto
                {
                    OpportunityId = opp.Id,
                    Priority = "URGENT",
                    TaskName = $"Compare quotes — {TruncateName(opp.Name)}",
                    Description = "Quotes ready for review",
                    CtaText = "Compare",
                    PriorityOrder = 1
                });
            }
            // Very stale (14+ days) → URGENT
            else if (stageAge >= 14)
            {
                tasks.Add(new NextTaskDto
                {
                    OpportunityId = opp.Id,
                    Priority = "URGENT",
                    TaskName = $"Check velocity — {TruncateName(opp.Name)}",
                    Description = $"Stale {(int)stageAge} days",
                    CtaText = "Check",
                    PriorityOrder = 2
                });
            }
            // ClientDecision stage stale 7+ days → HIGH
            else if (opp.LifecycleStage == LifecycleStage.ClientDecision && stageAge >= 7)
            {
                tasks.Add(new NextTaskDto
                {
                    OpportunityId = opp.Id,
                    Priority = "HIGH",
                    TaskName = $"Proposal follow-up — {TruncateName(opp.Name)}",
                    Description = "Awaiting client decision",
                    CtaText = "Follow Up",
                    PriorityOrder = 3
                });
            }
        }

        return tasks.OrderBy(t => t.PriorityOrder).Take(6).ToList();
    }

    private static string TruncateName(string name) =>
        name.Length > 20 ? name[..17] + "..." : name;

    private static string GetEventDotColor(string? eventType) => eventType switch
    {
        "bound" or "policy_bound" => "#2E7D32",
        "quotes_received" => "#10B981",
        "stage_advanced" or "stage_changed" => "#F59E0B",
        "note_added" => "#6B7280",
        "alert" => "#CC2200",
        "proposal_sent" => "#8B5CF6",
        "submitted" or "submission_created" => "#F59E0B",
        _ => "#6B7280"
    };

    private class OpportunitySlim
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public LifecycleStage LifecycleStage { get; set; }
        public DominantSignal DominantSignal { get; set; }
        public DateTime? LastStageTransitionAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}

// DTOs
public class DashboardSummaryDto
{
    public int ActivePipelineCount { get; set; }
    public int StaleCount { get; set; }
    public int SubmissionQueueCount { get; set; }
    public decimal BoundPremiumYtd { get; set; }
    public decimal BoundPremiumLastYear { get; set; }
    public int TotalAccountCount { get; set; }
    public int NewAccountsYtd { get; set; }
    public List<PipelineStageSummaryDto> PipelineByStage { get; set; } = new();
    public List<RoutingAccountDto> AccountsToRoute { get; set; } = new();
    public List<NextTaskDto> NextTasks { get; set; } = new();
    public List<ActivityEventDto> RecentActivity { get; set; } = new();
}

public class PipelineStageSummaryDto
{
    public LifecycleStage Stage { get; set; }
    public int Count { get; set; }
    public decimal TotalPremium { get; set; }
}

public class RoutingAccountDto
{
    public Guid OpportunityId { get; set; }
    public string CompanyName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Destination { get; set; } = "";
    public string CtaText { get; set; } = "";
    public string CtaUrl { get; set; } = "";
    public int Priority { get; set; }
}

public class NextTaskDto
{
    public Guid OpportunityId { get; set; }
    public string Priority { get; set; } = "";
    public string TaskName { get; set; } = "";
    public string Description { get; set; } = "";
    public string CtaText { get; set; } = "";
    public int PriorityOrder { get; set; }
}

public class ActivityEventDto
{
    public Guid OpportunityId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime OccurredAt { get; set; }
    public string EventType { get; set; } = "";
    public string DotColor { get; set; } = "#6B7280";
}
