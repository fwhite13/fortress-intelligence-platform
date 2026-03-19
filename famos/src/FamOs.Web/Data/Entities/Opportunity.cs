using FamOs.Web.Domain;

namespace FamOs.Web.Data.Entities;

public class Opportunity
{
    public Guid   Id                    { get; set; } = Guid.NewGuid();
    public string Name                  { get; set; } = "";
    public Guid?  ProgramId             { get; set; }

    // Lifecycle
    public LifecycleStage LifecycleStage  { get; set; } = LifecycleStage.Intake;
    public DominantSignal DominantSignal  { get; set; } = DominantSignal.WaitingOnClient;
    public string?        DominantSignalReason { get; set; }
    public int            UrgencyScore   { get; set; } = 0;

    // Ownership
    public string  OwnerUserId          { get; set; } = "";

    // Financials
    public decimal? EstimatedPremium    { get; set; }
    public DateOnly? EffectiveDateTarget { get; set; }

    // State
    public bool   IsClosed              { get; set; } = false;
    public int    Version               { get; set; } = 1;

    // Timing
    public DateTime?  MarketedAt         { get; set; }
    public DateTime?  ProposalSentAt     { get; set; }
    public DateTime?  ClientDecisionAt   { get; set; }

    // Audit
    public DateTime CreatedAt            { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt            { get; set; } = DateTime.UtcNow;

    // Navigation
    public List<Submission>         Submissions    { get; set; } = new();
    public List<Quote>              Quotes         { get; set; } = new();
    public List<Proposal>           Proposals      { get; set; } = new();
    public List<Activity>           Activities     { get; set; } = new();
    public List<FamOsTask>          Tasks          { get; set; } = new();
    public List<OpportunityFlag>    Flags          { get; set; } = new();
    public PolicyShadowRecord?      PolicyShadow   { get; set; }
}
