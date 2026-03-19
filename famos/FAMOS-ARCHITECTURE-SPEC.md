# FAM OS — Phase 1 Architecture Specification

**Author:** Reed Richards (Software Architect)  
**Date:** 2026-03-18  
**Status:** Ready for Implementation  
**Meeting:** Steve, Lauren, Jay — 2026-03-19 morning  
**Product spec:** `FAM OS Phase 1 2026-03-18.docx`  
**Builder pack:** `TIG - FAM OS Starter Pack.docx`

---

## 1. System Purpose

FAM OS is an internal lifecycle execution engine for Embedded Resources (ERs) managing insurance opportunity pipelines. It is **not** a CRM (HubSpot handles that) and **not** a policy system of record (Epic/Symphony handle that). FAM OS owns one thing: **governed lifecycle progression** from INTAKE through BIND for affinity program insurance opportunities.

Phase 1 success criterion: one ER can manage 100–200 active opportunities simultaneously with no silent stalls, clear next actions, and structured quote comparison.

---

## 2. Tech Stack

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Framework | ASP.NET 9 Blazor Server | Consistent with FIP stack (FAIT/FIRM/FORMS) |
| UI components | MudBlazor 7.x | Consistent with FIP |
| Shared nav | FipShared RCL (`fip/shared/FipShared/`) | FipNavBar, FipModule |
| Database | Aurora MySQL (`fortress-ai-cluster`) | Existing cluster, no new infra |
| DB name | `famos_dev` (dev), `famos_prod` (prod) | New schema on existing cluster |
| ORM | EF Core + Pomelo.EntityFrameworkCore.MySql | Same as all FIP apps |
| Auth | FIP cookie (`.FortressAI.Session`) | Shared session, no new OIDC |
| Hosting | ECS Fargate, `fortress-tools-cluster` | Standard FIP deploy pattern |
| Dev URL | `https://famos.dev.fortressam.ai` | ALB host-based routing |
| Monorepo path | `fip/famos/` | Alongside fait, firm, forms |
| .NET version | net9.0 | Current FIP standard |

---

## 3. Solution Structure

```
fip/famos/
├── src/
│   └── FamOs.Web/                    ← Single-project Blazor Server app
│       ├── FamOs.Web.csproj
│       ├── Program.cs
│       ├── Components/
│       │   ├── App.razor
│       │   ├── Routes.razor
│       │   ├── Layout/
│       │   │   ├── MainLayout.razor
│       │   │   ├── MainLayout.razor.css
│       │   │   └── NavMenu.razor
│       │   └── Pages/
│       │       ├── Dashboard.razor
│       │       ├── Pipeline.razor
│       │       ├── TaskCenter.razor
│       │       └── Opportunity/
│       │           ├── OpportunityWorkspace.razor
│       │           └── (panel components — Sprint 2+)
│       ├── Data/
│       │   ├── FamOsDbContext.cs
│       │   └── Entities/
│       │       ├── Opportunity.cs
│       │       ├── Submission.cs
│       │       ├── Quote.cs
│       │       ├── Proposal.cs
│       │       ├── PolicyShadowRecord.cs
│       │       ├── Activity.cs
│       │       ├── OpportunityFlag.cs
│       │       ├── FamOsTask.cs
│       │       └── OutboxEvent.cs
│       ├── Domain/
│       │   ├── Enums.cs
│       │   ├── LifecycleCommandService.cs
│       │   ├── SignalResolver.cs
│       │   └── TransitionRules.cs
│       ├── Services/
│       │   ├── OutboxProcessorService.cs   ← IHostedService background worker
│       │   ├── SignalRecomputeService.cs   ← IHostedService 15-min scheduler
│       │   └── UserSessionService.cs
│       ├── Theme/
│       │   └── FipTheme.cs
│       ├── wwwroot/
│       │   └── css/
│       │       └── famos.css
│       ├── appsettings.json
│       └── appsettings.Development.json
├── Dockerfile
└── buildspec.yml
```

**Why single project?** FORMS splits Web + Data (two .csproj files). For FAM OS Phase 1, the data layer is simple enough that a single project is appropriate. Data entities and DbContext live in `Data/` subfolder. If Phase 2 brings in complex reporting queries or a separate import service, split then — not now.

---

## 4. Domain Enums

Verbatim from the Starter Pack, translated to C#:

```csharp
// famos/src/FamOs.Web/Domain/Enums.cs

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
    TimeRisk                 = 8
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
```

---

## 5. EF Core Data Model

All enums stored as `int` (EF default). GUIDs stored as `char(36)`. `JSONB` from the spec → `string` (MySQL has JSON column type but EF treats it as string). All tables use `utf8mb4`.

### 5.1 `Opportunity`

```csharp
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
    public string  OwnerUserId          { get; set; } = "";   // FAIT userId (string)

    // Financials
    public decimal? EstimatedPremium    { get; set; }
    public DateOnly? EffectiveDateTarget { get; set; }

    // State
    public bool   IsClosed              { get; set; } = false;
    public int    Version               { get; set; } = 1;    // Optimistic concurrency

    // Timing (used by signal resolver for aging)
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
```

**Optimistic concurrency:** `Version` column. EF Core `IsConcurrencyToken` on `Version`. Before any lifecycle command, `Version` is checked. If mismatch, command is rejected with a user-visible error ("Someone else updated this opportunity — please refresh").

### 5.2 `Submission`

```csharp
public class Submission
{
    public Guid     Id                { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId     { get; set; }
    public string   CarrierName       { get; set; } = "";
    public string   Status            { get; set; } = "pending"; // pending | sent | responded | declined
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt           { get; set; }
    public DateTime? RespondedAt      { get; set; }

    public Opportunity Opportunity    { get; set; } = default!;
    public List<Quote> Quotes         { get; set; } = new();
}
```

### 5.3 `Quote`

```csharp
public class Quote
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     SubmissionId        { get; set; }
    public string   CarrierName         { get; set; } = "";
    public decimal  PremiumAmount       { get; set; }
    public string?  CoverageDetails     { get; set; }   // JSON string
    public bool     IsRecommended       { get; set; } = false;
    public DateTime ReceivedAt          { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
    public Submission  Submission       { get; set; } = default!;
}
```

### 5.4 `Proposal`

```csharp
public class Proposal
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     RecommendedQuoteId  { get; set; }
    public int      Version             { get; set; } = 1;
    public string   Status              { get; set; } = "draft"; // draft | sent | accepted | declined
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt             { get; set; }
    public DateTime? ClientDecisionAt   { get; set; }
    public string?  DeclineReason       { get; set; }

    public Opportunity Opportunity      { get; set; } = default!;
}
```

### 5.5 `PolicyShadowRecord`

```csharp
public class PolicyShadowRecord
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid?    WinningQuoteId      { get; set; }
    public string?  CarrierName         { get; set; }
    public DateOnly? PolicyEffectiveDate { get; set; }
    public decimal?  PremiumAmount      { get; set; }
    public DateOnly? RenewalTimerStart  { get; set; }
    public string?   SnapshotJson       { get; set; }  // coverage summary + carrier + pricing
    public DateTime  CreatedAt          { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
}
```

### 5.6 `Activity`

Immutable event log. Never updated, only inserted.

```csharp
public class Activity
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId   { get; set; }
    public string   EventType       { get; set; } = "";
    public string?  Description     { get; set; }
    public string?  ActorUserId     { get; set; }
    public string?  MetadataJson    { get; set; }
    public DateTime OccurredAt      { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity  { get; set; } = default!;
}
```

### 5.7 `FamOsTask`

```csharp
public class FamOsTask
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId   { get; set; }
    public string   Title           { get; set; } = "";
    public string   Status          { get; set; } = "open"; // open | done | cancelled
    public string?  AssignedToUserId { get; set; }
    public DateTime? DueAt          { get; set; }
    public DateTime  CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt      { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity  { get; set; } = default!;
}
```

### 5.8 `OpportunityFlag`

```csharp
public class OpportunityFlag
{
    public Guid             Id              { get; set; } = Guid.NewGuid();
    public Guid             OpportunityId   { get; set; }
    public OpportunityFlagType FlagType     { get; set; }
    public string?          Reason          { get; set; }
    public DateTime         SetAt           { get; set; } = DateTime.UtcNow;
    public bool             IsActive        { get; set; } = true;

    public Opportunity      Opportunity     { get; set; } = default!;
}
```

### 5.9 `OutboxEvent`

Transactional outbox for domain events. Written in same DB transaction as lifecycle commands. Background processor reads and dispatches.

```csharp
public class OutboxEvent
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public string   EventType       { get; set; } = "";
    public string   PayloadJson     { get; set; } = "";
    public DateTime OccurredAt      { get; set; } = DateTime.UtcNow;
    public bool     Processed       { get; set; } = false;
    public DateTime? ProcessedAt    { get; set; }
    public int      RetryCount      { get; set; } = 0;
    public string?  ErrorMessage    { get; set; }
}
```

### 5.10 `FamOsDbContext`

```csharp
public class FamOsDbContext : DbContext
{
    public FamOsDbContext(DbContextOptions<FamOsDbContext> options) : base(options) { }

    public DbSet<Opportunity>         Opportunities         => Set<Opportunity>();
    public DbSet<Submission>          Submissions           => Set<Submission>();
    public DbSet<Quote>               Quotes                => Set<Quote>();
    public DbSet<Proposal>            Proposals             => Set<Proposal>();
    public DbSet<PolicyShadowRecord>  PolicyShadowRecords   => Set<PolicyShadowRecord>();
    public DbSet<Activity>            Activities            => Set<Activity>();
    public DbSet<FamOsTask>           Tasks                 => Set<FamOsTask>();
    public DbSet<OpportunityFlag>     OpportunityFlags      => Set<OpportunityFlag>();
    public DbSet<OutboxEvent>         OutboxEvents          => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder m)
    {
        // Opportunity
        m.Entity<Opportunity>(e => {
            e.ToTable("opportunities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.LifecycleStage).HasConversion<int>();
            e.Property(x => x.DominantSignal).HasConversion<int>();
            e.Property(x => x.EstimatedPremium).HasPrecision(18, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.LifecycleStage).HasDatabaseName("idx_opp_stage");
            e.HasIndex(x => x.OwnerUserId).HasDatabaseName("idx_opp_owner");
            e.HasIndex(x => x.IsClosed).HasDatabaseName("idx_opp_closed");
        });

        // Submission
        m.Entity<Submission>(e => {
            e.ToTable("submissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.HasOne(x => x.Opportunity).WithMany(o => o.Submissions).HasForeignKey(x => x.OpportunityId);
        });

        // Quote
        m.Entity<Quote>(e => {
            e.ToTable("quotes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.Property(x => x.SubmissionId).HasColumnType("char(36)");
            e.Property(x => x.PremiumAmount).HasPrecision(18, 2);
            e.HasOne(x => x.Opportunity).WithMany(o => o.Quotes).HasForeignKey(x => x.OpportunityId);
            e.HasOne(x => x.Submission).WithMany(s => s.Quotes).HasForeignKey(x => x.SubmissionId);
        });

        // Proposal
        m.Entity<Proposal>(e => {
            e.ToTable("proposals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.Property(x => x.RecommendedQuoteId).HasColumnType("char(36)");
            e.HasOne(x => x.Opportunity).WithMany(o => o.Proposals).HasForeignKey(x => x.OpportunityId);
        });

        // PolicyShadowRecord
        m.Entity<PolicyShadowRecord>(e => {
            e.ToTable("policy_shadow_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.Property(x => x.PremiumAmount).HasPrecision(18, 2);
            e.HasOne(x => x.Opportunity).WithOne(o => o.PolicyShadow).HasForeignKey<PolicyShadowRecord>(x => x.OpportunityId);
        });

        // Activity
        m.Entity<Activity>(e => {
            e.ToTable("activities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.HasIndex(x => x.OpportunityId).HasDatabaseName("idx_act_opp");
            e.HasIndex(x => x.OccurredAt).HasDatabaseName("idx_act_time");
            e.HasOne(x => x.Opportunity).WithMany(o => o.Activities).HasForeignKey(x => x.OpportunityId);
        });

        // FamOsTask
        m.Entity<FamOsTask>(e => {
            e.ToTable("tasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.HasIndex(x => x.OpportunityId).HasDatabaseName("idx_task_opp");
            e.HasOne(x => x.Opportunity).WithMany(o => o.Tasks).HasForeignKey(x => x.OpportunityId);
        });

        // OpportunityFlag
        m.Entity<OpportunityFlag>(e => {
            e.ToTable("opportunity_flags");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.Property(x => x.FlagType).HasConversion<int>();
            e.HasOne(x => x.Opportunity).WithMany(o => o.Flags).HasForeignKey(x => x.OpportunityId);
        });

        // OutboxEvent
        m.Entity<OutboxEvent>(e => {
            e.ToTable("outbox_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.HasIndex(x => new { x.Processed, x.OccurredAt }).HasDatabaseName("idx_outbox_pending");
        });
    }
}
```

---

## 6. Lifecycle Engine Design

### 6.1 Architecture Principle

**Lifecycle cannot be bypassed.** All stage changes go through `LifecycleCommandService`. The Blazor UI calls service methods — it never sets `opportunity.LifecycleStage` directly.

### 6.2 `LifecycleCommandService`

```csharp
// famos/src/FamOs.Web/Domain/LifecycleCommandService.cs

public class LifecycleCommandService
{
    private readonly FamOsDbContext _db;
    private readonly SignalResolver _signals;
    private readonly ILogger<LifecycleCommandService> _logger;

    // All 9 command methods follow the same pattern:
    // 1. Load opportunity with version check
    // 2. Validate preconditions (throw LifecycleValidationException if fail)
    // 3. Mutate state
    // 4. Recompute dominant signal
    // 5. Write OutboxEvent in same transaction
    // 6. Write Activity in same transaction
    // 7. SaveChanges (transactional — all or nothing)

    public async Task PursueOpportunityAsync(Guid opportunityId, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.Intake,
            "PursueOpportunity requires stage INTAKE");
        Validate(!HasFlag(opp, OpportunityFlagType.Lost),
            "Cannot pursue a Lost opportunity");
        Validate(!string.IsNullOrEmpty(opp.OwnerUserId),
            "Opportunity must have an owner before pursuing");

        var from = opp.LifecycleStage;
        opp.LifecycleStage = LifecycleStage.UnderwritingPrep;
        opp.UpdatedAt = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "lifecycle_advanced",
            $"Advanced to {opp.LifecycleStage}", actorUserId);
        await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new {
            opportunity_id = opp.Id,
            from_stage     = from.ToString(),
            to_stage       = opp.LifecycleStage.ToString(),
            actor_user_id  = actorUserId,
            occurred_at    = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RouteToMarketAsync(Guid opportunityId, string[] carrierNames, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.UnderwritingPrep,
            "RouteToMarket requires stage UNDERWRITING_PREP");
        // Additional validation: check underwriting checklist items as Sprint 2+ feature
        // Phase 1: validate at least one carrier name provided
        Validate(carrierNames.Length > 0, "At least one carrier required");

        // Create submission records
        foreach (var carrier in carrierNames)
        {
            _db.Submissions.Add(new Submission {
                OpportunityId = opportunityId,
                CarrierName   = carrier,
                Status        = "sent",
                SentAt        = DateTime.UtcNow
            });
        }

        var from = opp.LifecycleStage;
        opp.LifecycleStage = LifecycleStage.Marketed;
        opp.MarketedAt = DateTime.UtcNow;
        opp.UpdatedAt = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "routed_to_market",
            $"Routed to {carrierNames.Length} carrier(s)", actorUserId);
        await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new {
            opportunity_id = opp.Id, from_stage = from.ToString(),
            to_stage = opp.LifecycleStage.ToString(), actor_user_id = actorUserId,
            occurred_at = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RecordQuoteAsync(Guid opportunityId, Guid submissionId,
        string carrierName, decimal premium, string? coverageDetailsJson, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        var isFirst = !opp.Quotes.Any();

        _db.Quotes.Add(new Quote {
            OpportunityId  = opportunityId,
            SubmissionId   = submissionId,
            CarrierName    = carrierName,
            PremiumAmount  = premium,
            CoverageDetails = coverageDetailsJson,
            ReceivedAt     = DateTime.UtcNow
        });

        if (isFirst && opp.LifecycleStage == LifecycleStage.Marketed)
        {
            var from = opp.LifecycleStage;
            opp.LifecycleStage = LifecycleStage.QuotesReceived;
            opp.UpdatedAt = DateTime.UtcNow;
            opp.Version++;
            await WriteOutboxAsync(DomainEventType.OpportunityLifecycleChanged, new {
                opportunity_id = opp.Id, from_stage = from.ToString(),
                to_stage = opp.LifecycleStage.ToString(), actor_user_id = actorUserId,
                occurred_at = DateTime.UtcNow
            });
        }

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "quote_recorded",
            $"Quote from {carrierName}: ${premium:N0}", actorUserId);
        await WriteOutboxAsync(DomainEventType.QuoteRecorded, new {
            opportunity_id = opportunityId, carrier = carrierName, premium, occurred_at = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task SendProposalAsync(Guid opportunityId, Guid recommendedQuoteId, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.QuotesReceived,
            "SendProposal requires stage QUOTES_RECEIVED");
        Validate(opp.Quotes.Any(q => q.Id == recommendedQuoteId),
            "Recommended quote not found on this opportunity");

        // Mark recommended
        foreach (var q in opp.Quotes) q.IsRecommended = false;
        opp.Quotes.First(q => q.Id == recommendedQuoteId).IsRecommended = true;

        var proposal = new Proposal {
            OpportunityId      = opportunityId,
            RecommendedQuoteId = recommendedQuoteId,
            Status             = "sent",
            SentAt             = DateTime.UtcNow
        };
        _db.Proposals.Add(proposal);

        var from = opp.LifecycleStage;
        opp.LifecycleStage  = LifecycleStage.ClientDecision;
        opp.ProposalSentAt  = DateTime.UtcNow;
        opp.UpdatedAt       = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "proposal_sent", "Proposal sent to client", actorUserId);
        await WriteOutboxAsync(DomainEventType.ProposalSent, new {
            opportunity_id = opportunityId, proposal_id = proposal.Id,
            sent_at = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ReopenMarketAsync(Guid opportunityId, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.ClientDecision,
            "ReopenMarket requires stage CLIENT_DECISION");

        var from = opp.LifecycleStage;
        opp.LifecycleStage = LifecycleStage.QuotesReceived;
        opp.UpdatedAt = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "market_reopened", "Market reopened for negotiation", actorUserId);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RequestBindAsync(Guid opportunityId, Guid winningQuoteId, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.ClientDecision,
            "RequestBind requires stage CLIENT_DECISION");
        Validate(opp.Quotes.Any(q => q.Id == winningQuoteId),
            "Winning quote not found");

        var from = opp.LifecycleStage;
        opp.LifecycleStage    = LifecycleStage.Binding;
        opp.ClientDecisionAt  = DateTime.UtcNow;
        opp.UpdatedAt         = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "bind_requested", "Bind requested from carrier", actorUserId);
        await WriteOutboxAsync(DomainEventType.BindRequested, new {
            opportunity_id = opportunityId, winning_quote_id = winningQuoteId
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task RecordBinderReceivedAsync(Guid opportunityId,
        DateOnly effectiveDate, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityWithDetailsAsync(opportunityId);

        Validate(opp.LifecycleStage == LifecycleStage.Binding,
            "RecordBinderReceived requires stage BINDING");

        var winningQuote = opp.Quotes.FirstOrDefault(q => q.IsRecommended);
        var shadow = new PolicyShadowRecord {
            OpportunityId       = opportunityId,
            WinningQuoteId      = winningQuote?.Id,
            CarrierName         = winningQuote?.CarrierName,
            PolicyEffectiveDate = effectiveDate,
            PremiumAmount       = winningQuote?.PremiumAmount,
            RenewalTimerStart   = effectiveDate,
            SnapshotJson        = winningQuote?.CoverageDetails
        };
        _db.PolicyShadowRecords.Add(shadow);

        var from = opp.LifecycleStage;
        opp.LifecycleStage = LifecycleStage.Bound;
        opp.UpdatedAt = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "binder_received", "Binder received — policy bound", actorUserId);
        await WriteOutboxAsync(DomainEventType.BinderReceived, new {
            opportunity_id = opportunityId, policy_shadow_id = shadow.Id, effective_date = effectiveDate
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task ParkOpportunityAsync(Guid opportunityId, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityAsync(opportunityId);

        // Can park from any active stage except Bound/Closed
        Validate(!opp.IsClosed, "Cannot park a closed opportunity");

        // Remove existing PARKED flag if any, then add fresh one
        var existing = opp.Flags.Where(f => f.FlagType == OpportunityFlagType.Parked && f.IsActive).ToList();
        existing.ForEach(f => f.IsActive = false);
        _db.OpportunityFlags.Add(new OpportunityFlag {
            OpportunityId = opportunityId,
            FlagType      = OpportunityFlagType.Parked,
        });

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "opportunity_parked", "Opportunity parked", actorUserId);
        await WriteOutboxAsync(DomainEventType.OpportunityParked, new {
            opportunity_id = opportunityId, actor_user_id = actorUserId
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task CloseOpportunityAsync(Guid opportunityId, string reason, string actorUserId)
    {
        await using var tx = await _db.Database.BeginTransactionAsync();
        var opp = await LoadOpportunityAsync(opportunityId);

        Validate(!opp.IsClosed, "Opportunity already closed");

        var from = opp.LifecycleStage;
        opp.LifecycleStage = LifecycleStage.ClosedNotBound;
        opp.IsClosed       = true;
        opp.UpdatedAt      = DateTime.UtcNow;
        opp.Version++;

        await RecomputeSignalAsync(opp);
        await WriteActivityAsync(opp.Id, "opportunity_closed", $"Closed: {reason}", actorUserId);
        await WriteOutboxAsync(DomainEventType.OpportunityClosed, new {
            opportunity_id = opportunityId, reason, from_stage = from.ToString()
        });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Opportunity> LoadOpportunityAsync(Guid id)
    {
        var opp = await _db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new NotFoundException($"Opportunity {id} not found");
        return opp;
    }

    private async Task<Opportunity> LoadOpportunityWithDetailsAsync(Guid id)
    {
        var opp = await _db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Quotes)
            .Include(o => o.Submissions)
            .Include(o => o.Proposals)
            .FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new NotFoundException($"Opportunity {id} not found");
        return opp;
    }

    private static void Validate(bool condition, string message)
    {
        if (!condition) throw new LifecycleValidationException(message);
    }

    private static bool HasFlag(Opportunity opp, OpportunityFlagType type)
        => opp.Flags.Any(f => f.FlagType == type && f.IsActive);

    private async Task RecomputeSignalAsync(Opportunity opp)
    {
        var (signal, reason) = _signals.Resolve(opp);
        if (opp.DominantSignal != signal)
        {
            await WriteOutboxAsync(DomainEventType.DominantSignalChanged, new {
                opportunity_id  = opp.Id,
                previous_signal = opp.DominantSignal.ToString(),
                new_signal      = signal.ToString(),
                reason
            });
        }
        opp.DominantSignal       = signal;
        opp.DominantSignalReason = reason;
    }

    private async Task WriteActivityAsync(Guid oppId, string eventType,
        string description, string? actorUserId)
    {
        _db.Activities.Add(new Activity {
            OpportunityId = oppId,
            EventType     = eventType,
            Description   = description,
            ActorUserId   = actorUserId
        });
        await Task.CompletedTask;
    }

    private async Task WriteOutboxAsync(DomainEventType type, object payload)
    {
        _db.OutboxEvents.Add(new OutboxEvent {
            EventType   = type.ToString(),
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload)
        });
        await Task.CompletedTask;
    }
}

public class LifecycleValidationException : Exception
{
    public LifecycleValidationException(string message) : base(message) { }
}

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
```

---

## 7. Signal Resolver

The signal resolver is a **pure function** — given an Opportunity (with its flags and timestamps), it returns `(DominantSignal, reason)`. No DB queries inside the resolver. All data is loaded before calling it.

**Precedence order is authoritative (from the Starter Pack):**

```csharp
// famos/src/FamOs.Web/Domain/SignalResolver.cs

public class SignalResolver
{
    // Aging thresholds — configurable in appsettings
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

        // Rule 1: CLOSED or LOST → no actionable signal
        // (Closed uses ClosedNotBound stage + IsClosed flag)
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

        // Rule 8: MARKETED stage (waiting on carriers)
        if (opp.LifecycleStage == LifecycleStage.Marketed)
            return (DominantSignal.WaitingOnMarket, "Submitted to market — awaiting carrier quotes");

        // Rule 9: UNDERWRITING_PREP
        if (opp.LifecycleStage == LifecycleStage.UnderwritingPrep)
            return (DominantSignal.UnderwritingInProgress, "Underwriting data gathering in progress");

        // Rule 10: Missing underwriting artifacts (INTAKE)
        if (opp.LifecycleStage == LifecycleStage.Intake)
            return (DominantSignal.WaitingOnClient, "Awaiting required intake information from client");

        // Rule 11: BOUND
        if (opp.LifecycleStage == LifecycleStage.Bound)
            return (DominantSignal.PostBindProcessing, "Policy bound — complete post-bind servicing tasks");

        // Fallback
        return (DominantSignal.WaitingOnClient, "Awaiting next action");
    }

    private static bool HasActiveFlag(Opportunity opp, OpportunityFlagType type)
        => opp.Flags.Any(f => f.FlagType == type && f.IsActive);
}
```

---

## 8. Domain Event Outbox Pattern

**Why outbox?** Lifecycle commands and event emission must be atomic. If we emit events after `SaveChanges`, a crash between the two creates inconsistency. The outbox pattern writes the event record in the same transaction as the state change. A background processor delivers it.

### `OutboxProcessorService`

```csharp
// IHostedService background worker
// Runs every 30 seconds; picks up unprocessed events

public class OutboxProcessorService : BackgroundService
{
    // Phase 1: log events. Phase 2: route to HubSpot, AMS, etc.
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await ProcessBatchAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task ProcessBatchAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<OutboxProcessorService>>();

        var events = await db.OutboxEvents
            .Where(e => !e.Processed && e.RetryCount < 5)
            .OrderBy(e => e.OccurredAt)
            .Take(50)
            .ToListAsync();

        foreach (var evt in events)
        {
            try
            {
                // Phase 1: just log. Phase 2: dispatch to integration stubs.
                logger.LogInformation("[Outbox] {EventType}: {Payload}", evt.EventType, evt.PayloadJson);
                // TODO Phase 2: await _hubspotStub.HandleAsync(evt);
                evt.Processed   = true;
                evt.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                evt.RetryCount++;
                evt.ErrorMessage = ex.Message;
                logger.LogWarning(ex, "[Outbox] Failed to process event {Id}, retry {N}", evt.Id, evt.RetryCount);
            }
        }

        await db.SaveChangesAsync();
    }
}
```

### `SignalRecomputeService`

```csharp
// Runs every 15 minutes per Starter Pack scheduler spec.
// Re-evaluates TIME_RISK and STALLED for all active opportunities.

public class SignalRecomputeService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await RecomputeAllAsync();
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }
}
```

---

## 9. Blazor Component Architecture

### 9.1 Navigation Screens

| Screen | Route | Phase |
|--------|-------|-------|
| Dashboard | `/` | Sprint 2 |
| Pipeline Board | `/pipeline` | Sprint 2 |
| Task Center | `/tasks` | Sprint 3 |
| Opportunity Workspace | `/opportunity/{id}` | Sprint 2 |
| Accounts | `/accounts` | Future |
| Reports | `/reports` | Future |

### 9.2 Sprint 1 Stub Pages

Sprint 1 creates stub pages for all nav items (empty but navigable). Real content in Sprint 2.

### 9.3 Pipeline Board (Sprint 2 Design)

The Pipeline Board is a MudBlazor `MudPaper`-based Kanban. **Columns are NOT stored** — they're derived from `LifecycleStage` at query time.

Column mapping:

| Display Column | `LifecycleStage` values |
|----------------|-------------------------|
| Intake | Intake |
| App Review | UnderwritingPrep |
| Submitted | Marketed |
| Quotes In | QuotesReceived |
| Proposal | ClientDecision |
| Binding | Binding |
| Bound | Bound |

Each column loads its opportunities via a `@foreach` in the Kanban component. Cards use `MudCard` with signal chip, name, premium, owner.

### 9.4 Opportunity Workspace (Sprint 2 Design)

The workspace is a single `OpportunityWorkspace.razor` page that renders **stage-specific panels** based on `opportunity.LifecycleStage`. Panel visibility is driven by a `switch` in the component:

```razor
@switch (Opportunity.LifecycleStage)
{
    case LifecycleStage.Intake:
        <IntakePanel Opportunity="Opportunity" OnCommand="HandleCommand" />
        break;
    case LifecycleStage.UnderwritingPrep:
        <UnderwritingPrepPanel Opportunity="Opportunity" OnCommand="HandleCommand" />
        break;
    // etc.
}
```

The lifecycle header shows: current stage badge, dominant signal chip (color-coded), urgency score.

### 9.5 Signal Color Coding

| Signal | Color |
|--------|-------|
| WaitingOnClient | Amber/Warning |
| UnderwritingInProgress | Blue/Info |
| WaitingOnMarket | Purple |
| DecisionRequired | Orange |
| AwaitingClientDecision | Blue |
| BindingInProgress | Green |
| PostBindProcessing | Teal |
| TimeRisk | Red/Error |
| Parked | Gray |

---

## 10. HubSpot Integration (Phase 1 Stub)

HubSpot owns: prospect lifecycle, marketing campaigns, contact records.  
FAM OS owns: opportunity lifecycle from INTAKE through BIND.

**Phase 1 integration is stub-only.** The outbox emits `HubSpotLifecycleUpdated` events which the `OutboxProcessorService` logs but does not dispatch.

**Stub interface** (Sprint 1 registers a no-op implementation):

```csharp
public interface IHubSpotService
{
    Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage);
    Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow);
}

public class HubSpotServiceStub : IHubSpotService
{
    public Task SyncLifecycleAsync(Guid oppId, LifecycleStage stage)
    {
        _logger.LogInformation("[HubSpot stub] Lifecycle sync: {Id} → {Stage}", oppId, stage);
        return Task.CompletedTask;
    }
    public Task SyncBoundAsync(Guid oppId, PolicyShadowRecord shadow) => Task.CompletedTask;
}
```

Bruce is researching the HubSpot MCP — real integration wired in Phase 2.

---

## 11. AMS/Epic Integration (Phase 1 Stub)

AMS (Epic/Symphony) receives policy shadow records when bind is confirmed.

**Phase 1:** stub interface only. `PolicyShadowRecord` is the handoff contract.

```csharp
public interface IAmsService
{
    Task PushPolicyShadowAsync(PolicyShadowRecord record);
}

public class AmsServiceStub : IAmsService
{
    public Task PushPolicyShadowAsync(PolicyShadowRecord record)
    {
        _logger.LogInformation("[AMS stub] Policy shadow: {Id}", record.Id);
        return Task.CompletedTask;
    }
}
```

---

## 12. Auth: FIP Cookie Consumer

FAM OS is a **cookie consumer only** — same pattern as FORMS. No OIDC. No Entra. Reads the shared `.FortressAI.Session` cookie that FAIT issues.

Key auth config (verbatim from FORMS `Program.cs`):

```csharp
builder.Services.AddAuthentication(options => {
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options => {
    options.LoginPath = "/auth/redirect-to-login";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(12);
    options.SlidingExpiration = true;
    options.Cookie.Name = ".FortressAI.Session";
    options.Cookie.Domain = builder.Configuration["Auth__CookieDomain"] ?? "";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.IsEssential = true;
});
builder.Services.AddAuthorization(options => {
    options.FallbackPolicy = options.DefaultPolicy;
});
```

DataProtection reads from `fred_dev.DataProtectionKeys` (same MySQL table as all FIP apps). `DisableAutomaticKeyGeneration()` — FAIT owns key generation.

---

## 13. Dev/Prod Environment Split

```csharp
// appsettings.json defaults (shared)
{
  "ASPNETCORE_ENVIRONMENT": "Production",
  "FamOs": {
    "SubmissionAgingDays": 7,
    "ProposalAgingDays": 5
  }
}

// appsettings.Development.json (local dev overrides — no Kestrel block)
{
  "Auth__CookieDomain": "localhost",
  "FamOs": {
    "SubmissionAgingDays": 1,   // shorter aging for dev testing
    "ProposalAgingDays": 1
  }
}
```

ECS task definition provides the real values via env vars:
- `FORTRESS_DB_HOST`, `FORTRESS_DB_USER`, `FORTRESS_DB_PASS`, `FORTRESS_DB_PORT`
- `FAMOS_DB_NAME` (default: `famos_dev`)
- `Auth__CookieDomain` (`.dev.fortressam.ai`)
- `ASPNETCORE_ENVIRONMENT=Production`

---

## 14. Key Design Decisions Summary

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Single vs. split .csproj | Single project | Phase 1 data model is simple; split if Phase 2 adds import service |
| ORM migrations | `CreateTablesAsync` pattern (same as FORMS) | No EF migrations needed; idempotent startup |
| Enum storage | `int` columns | EF default; simple, fast |
| GUID format | `char(36)` | MySQL-compatible; readable in DB console |
| Signal resolver | Pure function (no DB calls inside) | Testable; fast; deterministic |
| Outbox processor | Background `IHostedService` every 30s | Phase 1: log only; Phase 2: dispatch to HubSpot |
| Concurrency control | Optimistic (`Version` column) | Prevents lost-update on concurrent lifecycle commands |
| HubSpot / AMS | Stub interfaces Phase 1 | Bruce researching HubSpot MCP; Epic integration deferred |
| Pipeline columns | Derived from `LifecycleStage` | Spec says not independently stored |
| Task auto-generation | Manual creation Phase 1 | Sprint 3+ adds auto-generation rules |

---

_Architecture by Reed Richards. FAM OS Phase 1. Meeting: 2026-03-19._
