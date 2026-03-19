namespace FamOs.Web.Domain;

public enum LifecycleStage
{
    Intake             = 0,
    UnderwritingPrep   = 1,
    Marketed           = 2,
    QuotesReceived     = 3,
    ClientDecision     = 4,
    Binding            = 5,
    Bound              = 6,
    ClosedNotBound     = 7
}

public enum DominantSignal
{
    Parked                   = 0,
    WaitingOnClient          = 1,
    UnderwritingInProgress   = 2,
    WaitingOnMarket          = 3,
    DecisionRequired         = 4,
    AwaitingClientDecision   = 5,
    BindingInProgress        = 6,
    PostBindProcessing       = 7,
    TimeRisk                 = 8,

    // Sprint 5 additions — aging-derived signals
    FollowUpNeeded         = 9,
    WaitingOnUW            = 10,
    WaitingOnCarrier       = 11,
    AtRisk                 = 12,
    Urgent                 = 13,
}

public enum OpportunityFlagType
{
    Parked    = 0,
    Lost      = 1,
    TimeRisk  = 2,
    Stalled   = 3
}

public enum DomainEventType
{
    OpportunityLifecycleChanged = 0,
    QuoteRecorded               = 1,
    ProposalSent                = 2,
    BindRequested               = 3,
    BinderReceived              = 4,
    DominantSignalChanged       = 5,
    OpportunityClosed           = 6,
    OpportunityParked           = 7
}

public enum CloseReason
{
    NotQuoted              = 0,
    PriceTooHigh           = 1,
    LostToCompetitor       = 2,
    ClientDeclinedCoverage = 3,
    PolicyLapsed           = 4,
    Other                  = 5
}
