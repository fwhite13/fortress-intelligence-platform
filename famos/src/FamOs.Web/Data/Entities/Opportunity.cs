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

    /// <summary>Affinity program this opportunity belongs to (e.g. "tig", "iaapa", "nbais").</summary>
    public string AffinityId { get; set; } = "tig";

    // Financials
    public decimal? EstimatedPremium    { get; set; }
    public DateOnly? EffectiveDateTarget { get; set; }

    /// <summary>
    /// JSON object mapping intake field IDs to user-entered values.
    /// Structure: { "fleet_size": "42", "dot_number": "123456", ... }
    /// Schema is affinity-group-specific; Phase 1 hardcoded for trucking program.
    /// </summary>
    public string? IntakeResponsesJson { get; set; }

    // State
    public bool   IsClosed              { get; set; } = false;
    public CloseReason? CloseReason     { get; set; }
    public string?      CloseNotes      { get; set; }
    public int    Version               { get; set; } = 1;

    // Timing
    public DateTime?  MarketedAt              { get; set; }
    public DateTime?  ProposalSentAt          { get; set; }
    public DateTime?  ClientDecisionAt        { get; set; }
    public string?   BindConfirmationNumber   { get; set; }
    public DateTime? BindRequestSubmittedAt   { get; set; }
    /// <summary>UTC timestamp of the most recent lifecycle stage change. Used for aging calculations.</summary>
    public DateTime?  LastStageTransitionAt   { get; set; }

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
    public List<Contact>           Contacts          { get; set; } = new();
    public Guid?                   PrimaryContactId  { get; set; }
    public List<OpportunityDocument> Documents { get; set; } = new();
}
