using FamOs.Web.Data.Entities;

namespace FamOs.Web.Domain;

public class SignalResolver
{
    private readonly TimeSpan _submissionAgingThreshold;
    private readonly TimeSpan _proposalAgingThreshold;

    public SignalResolver(IConfiguration config)
    {
        _submissionAgingThreshold = TimeSpan.FromDays(
            config.GetValue<int>("FamOs:SubmissionAgingDays", 7));
        _proposalAgingThreshold = TimeSpan.FromDays(
            config.GetValue<int>("FamOs:ProposalAgingDays", 5));
    }

    public (DominantSignal Signal, string Reason) Resolve(Opportunity opp)
    {
        var now = DateTime.UtcNow;

        // Rule 1: CLOSED or LOST
        if (opp.IsClosed || opp.LifecycleStage == LifecycleStage.ClosedNotBound)
            return (DominantSignal.Parked, "Opportunity closed");

        if (HasActiveFlag(opp, OpportunityFlagType.Lost))
            return (DominantSignal.Parked, "Opportunity marked lost");

        // Rule 2: PARKED flag
        if (HasActiveFlag(opp, OpportunityFlagType.Parked))
            return (DominantSignal.Parked, "Opportunity parked");

        // Rule 3: BINDING stage
        if (opp.LifecycleStage == LifecycleStage.Binding)
            return (DominantSignal.BindingInProgress, "Awaiting carrier bind confirmation");

        // Rule 4: Proposal aging threshold exceeded
        if (opp.ProposalSentAt.HasValue &&
            now - opp.ProposalSentAt.Value > _proposalAgingThreshold)
            return (DominantSignal.TimeRisk, $"Proposal sent {(now - opp.ProposalSentAt.Value).Days}d ago — client decision overdue");

        // Rule 5: Submission aging threshold exceeded
        if (opp.MarketedAt.HasValue &&
            now - opp.MarketedAt.Value > _submissionAgingThreshold &&
            opp.LifecycleStage == LifecycleStage.Marketed)
            return (DominantSignal.TimeRisk, $"Submitted {(now - opp.MarketedAt.Value).Days}d ago — no quote response");

        // Rule 6: Proposal sent (but not yet aged)
        if (opp.LifecycleStage == LifecycleStage.ClientDecision && opp.ProposalSentAt.HasValue)
            return (DominantSignal.AwaitingClientDecision, "Proposal sent — awaiting client decision");

        // Rule 7: Quotes present but proposal not sent
        if (opp.LifecycleStage == LifecycleStage.QuotesReceived && opp.Quotes.Any())
            return (DominantSignal.DecisionRequired, "Quotes received — select recommended and send proposal");

        // Rule 8: MARKETED stage
        if (opp.LifecycleStage == LifecycleStage.Marketed)
            return (DominantSignal.WaitingOnMarket, "Submitted to market — awaiting carrier quotes");

        // Rule 9: UNDERWRITING_PREP
        if (opp.LifecycleStage == LifecycleStage.UnderwritingPrep)
            return (DominantSignal.UnderwritingInProgress, "Underwriting data gathering in progress");

        // Rule 10: INTAKE
        if (opp.LifecycleStage == LifecycleStage.Intake)
            return (DominantSignal.WaitingOnClient, "Awaiting required intake information from client");

        // Rule 11: BOUND
        if (opp.LifecycleStage == LifecycleStage.Bound)
            return (DominantSignal.PostBindProcessing, "Policy bound — complete post-bind servicing tasks");

        return (DominantSignal.WaitingOnClient, "Awaiting next action");
    }

    private static bool HasActiveFlag(Opportunity opp, OpportunityFlagType type)
        => opp.Flags.Any(f => f.FlagType == type && f.IsActive);
}
