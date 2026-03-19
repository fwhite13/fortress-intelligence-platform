# FAM OS Sprint 1 — Implementation Brief

You are implementing FAM OS Sprint 1 for a .NET 9 Blazor Server app.
Working directory: /home/fredw/projects/fip/famos/src/FamOs.Web
You MUST create ALL files listed below. Write each file to disk exactly as specified.

## CRITICAL RULES
1. Use `blazor.server.js` (NOT blazor.web.js) in Components/App.razor
2. DataProtection in Program.cs MUST have BOTH lines:
   .SetApplicationName("FortressAI")
   .DisableAutomaticKeyGeneration();
3. TargetFramework must be net9.0
4. No EF migrations — use CreateTablesAsync pattern
5. FipModule.FAMOS is already added to FipShared — do NOT touch FipShared

---

## FILE 1: FamOs.Web.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>FamOs.Web</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.DataProtection.EntityFrameworkCore" Version="9.0.*" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="9.0.*" />
    <PackageReference Include="Pomelo.EntityFrameworkCore.MySql" Version="9.0.*" />
    <PackageReference Include="MySqlConnector" Version="2.*" />
    <PackageReference Include="MudBlazor" Version="7.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\shared\FipShared\FipShared.csproj" />
  </ItemGroup>
</Project>
```

---

## FILE 2: Domain/Enums.cs

```csharp
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

## FILE 3: Data/Entities/Opportunity.cs

```csharp
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
```

---

## FILE 4: Data/Entities/Submission.cs

```csharp
namespace FamOs.Web.Data.Entities;

public class Submission
{
    public Guid     Id                { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId     { get; set; }
    public string   CarrierName       { get; set; } = "";
    public string   Status            { get; set; } = "pending";
    public DateTime CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt           { get; set; }
    public DateTime? RespondedAt      { get; set; }

    public Opportunity Opportunity    { get; set; } = default!;
    public List<Quote> Quotes         { get; set; } = new();
}
```

---

## FILE 5: Data/Entities/Quote.cs

```csharp
namespace FamOs.Web.Data.Entities;

public class Quote
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     SubmissionId        { get; set; }
    public string   CarrierName         { get; set; } = "";
    public decimal  PremiumAmount       { get; set; }
    public string?  CoverageDetails     { get; set; }
    public bool     IsRecommended       { get; set; } = false;
    public DateTime ReceivedAt          { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
    public Submission  Submission       { get; set; } = default!;
}
```

---

## FILE 6: Data/Entities/Proposal.cs

```csharp
namespace FamOs.Web.Data.Entities;

public class Proposal
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid     RecommendedQuoteId  { get; set; }
    public int      Version             { get; set; } = 1;
    public string   Status              { get; set; } = "draft";
    public DateTime CreatedAt           { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt             { get; set; }
    public DateTime? ClientDecisionAt   { get; set; }
    public string?  DeclineReason       { get; set; }

    public Opportunity Opportunity      { get; set; } = default!;
}
```

---

## FILE 7: Data/Entities/PolicyShadowRecord.cs

```csharp
namespace FamOs.Web.Data.Entities;

public class PolicyShadowRecord
{
    public Guid     Id                  { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId       { get; set; }
    public Guid?    WinningQuoteId      { get; set; }
    public string?  CarrierName         { get; set; }
    public DateOnly? PolicyEffectiveDate { get; set; }
    public decimal?  PremiumAmount      { get; set; }
    public DateOnly? RenewalTimerStart  { get; set; }
    public string?   SnapshotJson       { get; set; }
    public DateTime  CreatedAt          { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity      { get; set; } = default!;
}
```

---

## FILE 8: Data/Entities/Activity.cs

```csharp
namespace FamOs.Web.Data.Entities;

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

---

## FILE 9: Data/Entities/FamOsTask.cs

```csharp
namespace FamOs.Web.Data.Entities;

public class FamOsTask
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId   { get; set; }
    public string   Title           { get; set; } = "";
    public string   Status          { get; set; } = "open";
    public string?  AssignedToUserId { get; set; }
    public DateTime? DueAt          { get; set; }
    public DateTime  CreatedAt      { get; set; } = DateTime.UtcNow;
    public DateTime  UpdatedAt      { get; set; } = DateTime.UtcNow;

    public Opportunity Opportunity  { get; set; } = default!;
}
```

---

## FILE 10: Data/Entities/OpportunityFlag.cs

```csharp
using FamOs.Web.Domain;

namespace FamOs.Web.Data.Entities;

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

---

## FILE 11: Data/Entities/OutboxEvent.cs

```csharp
namespace FamOs.Web.Data.Entities;

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

---

## FILE 12: Data/FamOsDbContext.cs

```csharp
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Data;

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

/// <summary>
/// Separate DbContext for DataProtection key ring.
/// Points to fred_dev.DataProtectionKeys — shared with FAIT/FIRM/FORMS.
/// DO NOT add any other entities here.
/// </summary>
public class SharedKeyRingDbContext : DbContext, IDataProtectionKeyContext
{
    public SharedKeyRingDbContext(DbContextOptions<SharedKeyRingDbContext> options)
        : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
```

---

## FILE 13: Domain/LifecycleCommandService.cs

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Domain;

public class LifecycleCommandService
{
    private readonly FamOsDbContext _db;
    private readonly SignalResolver _signals;
    private readonly ILogger<LifecycleCommandService> _logger;

    public LifecycleCommandService(FamOsDbContext db, SignalResolver signals,
        ILogger<LifecycleCommandService> logger)
    {
        _db      = db;
        _signals = signals;
        _logger  = logger;
    }

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
        Validate(carrierNames.Length > 0, "At least one carrier required");

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

        Validate(!opp.IsClosed, "Cannot park a closed opportunity");

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

## FILE 14: Domain/SignalResolver.cs

```csharp
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
```

---

## FILE 15: Services/OutboxProcessorService.cs

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;

namespace FamOs.Web.Services;

public class OutboxProcessorService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<OutboxProcessorService> _logger;

    public OutboxProcessorService(IServiceScopeFactory services,
        ILogger<OutboxProcessorService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Outbox] Batch processing error");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task ProcessBatchAsync()
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();

        var events = await db.OutboxEvents
            .Where(e => !e.Processed && e.RetryCount < 5)
            .OrderBy(e => e.OccurredAt)
            .Take(50)
            .ToListAsync();

        if (!events.Any()) return;

        foreach (var evt in events)
        {
            try
            {
                _logger.LogInformation("[Outbox] Processing {EventType}: {Payload}",
                    evt.EventType, evt.PayloadJson);

                evt.Processed   = true;
                evt.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                evt.RetryCount++;
                evt.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "[Outbox] Failed event {Id}, retry {N}",
                    evt.Id, evt.RetryCount);
            }
        }

        await db.SaveChangesAsync();
        _logger.LogInformation("[Outbox] Processed {Count} events", events.Count);
    }
}
```

---

## FILE 16: Services/SignalRecomputeService.cs

```csharp
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public class SignalRecomputeService : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<SignalRecomputeService> _logger;

    public SignalRecomputeService(IServiceScopeFactory services,
        ILogger<SignalRecomputeService> logger)
    {
        _services = services;
        _logger   = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RecomputeAllAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SignalRecompute] Error during recompute run");
            }
            await Task.Delay(TimeSpan.FromMinutes(15), ct);
        }
    }

    private async Task RecomputeAllAsync()
    {
        using var scope = _services.CreateScope();
        var db       = scope.ServiceProvider.GetRequiredService<FamOsDbContext>();
        var resolver = scope.ServiceProvider.GetRequiredService<SignalResolver>();

        var opps = await db.Opportunities
            .Include(o => o.Flags.Where(f => f.IsActive))
            .Include(o => o.Quotes)
            .Where(o => !o.IsClosed)
            .ToListAsync();

        int changed = 0;
        foreach (var opp in opps)
        {
            var (signal, reason) = resolver.Resolve(opp);
            if (opp.DominantSignal != signal || opp.DominantSignalReason != reason)
            {
                opp.DominantSignal       = signal;
                opp.DominantSignalReason = reason;
                opp.UpdatedAt            = DateTime.UtcNow;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync();
            _logger.LogInformation("[SignalRecompute] Updated {Count}/{Total} opportunities",
                changed, opps.Count);
        }
    }
}
```

---

## FILE 17: Services/UserSessionService.cs

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FamOs.Web.Services;

public class UserSessionService
{
    private readonly AuthenticationStateProvider _authProvider;

    public UserSessionService(AuthenticationStateProvider authProvider)
    {
        _authProvider = authProvider;
    }

    private ClaimsPrincipal? _user;

    public async Task<ClaimsPrincipal> GetUserAsync()
    {
        if (_user != null) return _user;
        var state = await _authProvider.GetAuthenticationStateAsync();
        _user = state.User;
        return _user;
    }

    public async Task<string> GetUserIdAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? "unknown";
    }

    public async Task<string> GetUserNameAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.Identity?.Name
            ?? "User";
    }

    public async Task<string> GetUserEmailAsync()
    {
        var user = await GetUserAsync();
        return user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value
            ?? "";
    }
}
```

---

## FILE 18: Services/HubSpotServiceStub.cs

```csharp
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Services;

public interface IHubSpotService
{
    Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage);
    Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow);
}

public class HubSpotServiceStub : IHubSpotService
{
    private readonly ILogger<HubSpotServiceStub> _logger;
    public HubSpotServiceStub(ILogger<HubSpotServiceStub> logger) => _logger = logger;

    public Task SyncLifecycleAsync(Guid opportunityId, LifecycleStage stage)
    {
        _logger.LogInformation("[HubSpot stub] Lifecycle sync: {Id} → {Stage}", opportunityId, stage);
        return Task.CompletedTask;
    }

    public Task SyncBoundAsync(Guid opportunityId, PolicyShadowRecord shadow)
    {
        _logger.LogInformation("[HubSpot stub] Policy shadow: {Id}", shadow.Id);
        return Task.CompletedTask;
    }
}
```

---

## FILE 19: Services/AmsServiceStub.cs

```csharp
using FamOs.Web.Data.Entities;

namespace FamOs.Web.Services;

public interface IAmsService
{
    Task PushPolicyShadowAsync(PolicyShadowRecord record);
}

public class AmsServiceStub : IAmsService
{
    private readonly ILogger<AmsServiceStub> _logger;
    public AmsServiceStub(ILogger<AmsServiceStub> logger) => _logger = logger;

    public Task PushPolicyShadowAsync(PolicyShadowRecord record)
    {
        _logger.LogInformation("[AMS stub] Policy shadow push: {Id}", record.Id);
        return Task.CompletedTask;
    }
}
```

---

## FILE 20: Theme/FipTheme.cs

```csharp
using MudBlazor;

namespace FamOs.Web.Theme;

/// <summary>
/// Fortress Intelligence Platform unified theme — MudBlazor v7 compatible.
/// Light mode only. No PaletteDark.
/// </summary>
public static class FipTheme
{
    public static MudTheme Create() => new MudTheme
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#1a2332",
            PrimaryContrastText = "#ffffff",
            Secondary = "#d4af37",
            SecondaryContrastText = "#1a2332",
            Background = "#f8f9fa",
            Surface = "#ffffff",
            AppbarBackground = "#1a2332",
            AppbarText = "#ffffff",
            DrawerBackground = "#1a2332",
            DrawerText = "#f0f0f0",
            DrawerIcon = "#d4af37",
            TextPrimary = "#1a2332",
            TextSecondary = "#6b7280",
            TextDisabled = "rgba(0,0,0,0.38)",
            ActionDefault = "#6b7280",
            Success = "#059669",
            Warning = "#d97706",
            Error = "#dc2626",
            Info = "#2563eb",
            TableLines = "#e5e7eb",
            TableHover = "#f3f4f6",
        },
        Typography = new Typography
        {
            Default = new Default
            {
                FontFamily = new[] { "Inter", "system-ui", "-apple-system", "sans-serif" },
                FontSize = "0.9375rem",
                LineHeight = 1.6,
            },
            H4 = new H4 { FontWeight = 700 },
            H5 = new H5 { FontWeight = 600 },
            H6 = new H6 { FontWeight = 600 },
            Button = new MudBlazor.Button
            {
                FontFamily = new[] { "Inter", "sans-serif" },
                FontWeight = 500,
                TextTransform = "none",
                FontSize = "0.9rem",
            },
            Caption = new Caption { FontSize = "0.75rem" }
        },
        LayoutProperties = new LayoutProperties
        {
            AppbarHeight = "48px",
            DrawerWidthLeft = "264px",
        }
    };
}
```

---

## FILE 21: Components/App.razor

```razor
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>FAM OS</title>
    <base href="/" />
    <link rel="stylesheet" href="/_content/MudBlazor/MudBlazor.min.css" />
    <link rel="stylesheet" href="/_content/FipShared/css/fip-tokens.css" />
    <link rel="stylesheet" href="/css/famos.css" />
    <HeadOutlet @rendermode="RenderMode.InteractiveServer" />
</head>
<body>
    <Routes @rendermode="RenderMode.InteractiveServer" />
    <script src="/_content/MudBlazor/MudBlazor.min.js"></script>
    <script src="/_framework/blazor.server.js"></script>
</body>
</html>
```

---

## FILE 22: Components/Routes.razor

```razor
@using Microsoft.AspNetCore.Components.Routing

<Router AppAssembly="typeof(Program).Assembly">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="routeData" DefaultLayout="typeof(Layout.MainLayout)">
            <NotAuthorized>
                @if (!context.User.Identity?.IsAuthenticated ?? true)
                {
                    <RedirectToLogin />
                }
                else
                {
                    <p>Access denied.</p>
                }
            </NotAuthorized>
        </AuthorizeRouteView>
    </Found>
    <NotFound>
        <LayoutView Layout="typeof(Layout.MainLayout)">
            <p role="alert">Sorry, nothing here.</p>
        </LayoutView>
    </NotFound>
</Router>

@code {
    private sealed class RedirectToLogin : ComponentBase
    {
        [Inject] NavigationManager Nav { get; set; } = default!;
        protected override void OnInitialized() =>
            Nav.NavigateTo("/auth/redirect-to-login", forceLoad: true);
    }
}
```

---

## FILE 23: Components/Layout/MainLayout.razor

```razor
@inherits LayoutComponentBase
@using FamOs.Web.Theme
@using System.Security.Claims
@using FipShared.Components
@using FipShared.Models
@inject NavigationManager _nav
@inject Microsoft.AspNetCore.Hosting.IWebHostEnvironment HostEnv

<MudThemeProvider Theme="_theme" />
<MudPopoverProvider />
<MudDialogProvider />
<MudSnackbarProvider />

<MudLayout>
    <FipNavBar ActiveModule="FipModule.FAMOS"
               UserInitial="@_userInitial"
               UserName="@_userName"
               UserEmail="@_userEmail"
               OnMenuClick="ToggleDrawer"
               OnSignOut="@(() => _nav.NavigateTo("/auth/logout", forceLoad: true))"
               IsDev="@HostEnv.IsDevelopment()" />

    <MudDrawer @bind-Open="_drawerOpen" Variant="DrawerVariant.Responsive"
               Breakpoint="Breakpoint.Md" ClipMode="DrawerClipMode.Always" Elevation="2">
        <div style="display: flex; flex-direction: column; height: 100%;">
            <MudDrawerHeader Style="padding: 12px 16px; background: var(--color-sidebar-bg);">
                <div style="display: flex; align-items: center; gap: 8px;">
                    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" style="color: var(--color-gold)">
                        <path d="M12 2L3 7v5c0 5.25 3.75 10.15 9 11.5C17.25 22.15 21 17.25 21 12V7L12 2z" fill="currentColor" opacity="0.9"/>
                        <path d="M9 12l2 2 4-4" stroke="white" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
                    </svg>
                    <span style="color: rgba(248,250,252,0.8); font-size: 14px; font-weight: 500;">Fortress</span>
                </div>
                <div style="color: var(--color-gold); font-size: var(--text-lg); font-weight: var(--font-bold); margin-top: 2px;">
                    FAM OS
                </div>
            </MudDrawerHeader>
            <div style="flex: 1; overflow-y: auto;">
                <NavMenu />
            </div>
            <div class="fip-drawer-footer">
                <MudText Typo="Typo.caption">Fortress Affinity Management OS</MudText>
            </div>
        </div>
    </MudDrawer>

    <MudMainContent Class="pa-4" Style="padding-top: 80px !important;">
        @Body
    </MudMainContent>
</MudLayout>

@code {
    private bool _drawerOpen = true;
    private string _userInitial = "F";
    private string _userName = "";
    private string _userEmail = "";

    private MudTheme _theme = FipTheme.Create();

    [Inject] private IServiceProvider ServiceProvider { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        var authProvider = ServiceProvider.GetService<AuthenticationStateProvider>();
        if (authProvider != null)
        {
            try
            {
                var auth = await authProvider.GetAuthenticationStateAsync();
                var name = auth.User?.Identity?.Name
                           ?? auth.User?.FindFirst("name")?.Value
                           ?? auth.User?.FindFirst(ClaimTypes.Name)?.Value
                           ?? "";
                var email = auth.User?.FindFirst("email")?.Value
                            ?? auth.User?.FindFirst(ClaimTypes.Email)?.Value
                            ?? "";
                _userName = string.IsNullOrEmpty(name) ? "User" : name;
                _userEmail = email;
                _userInitial = name.Length > 0 ? name[0].ToString().ToUpper() : "F";
            }
            catch
            {
                _userInitial = "F";
            }
        }
    }

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;
}
```

---

## FILE 24: Components/Layout/MainLayout.razor.css

```css
.fip-drawer-footer {
    padding: 12px 16px;
    border-top: 1px solid var(--color-border);
    color: var(--color-text-muted);
}
```

---

## FILE 25: Components/Layout/NavMenu.razor

```razor
@using Microsoft.AspNetCore.Components.Routing

<MudNavMenu>
    <MudNavLink Href="/" Match="NavLinkMatch.All" Icon="@Icons.Material.Filled.Dashboard">
        Dashboard
    </MudNavLink>
    <MudNavLink Href="/pipeline" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.ViewKanban">
        Pipeline
    </MudNavLink>
    <MudNavLink Href="/tasks" Match="NavLinkMatch.Prefix" Icon="@Icons.Material.Filled.CheckBox">
        Task Center
    </MudNavLink>
    <MudDivider Class="my-2" />
    <MudNavLink Href="/accounts" Disabled="true" Icon="@Icons.Material.Filled.Business">
        Accounts <MudChip Size="Size.Small" Color="Color.Default" Class="ml-1">Soon</MudChip>
    </MudNavLink>
    <MudNavLink Href="/reports" Disabled="true" Icon="@Icons.Material.Filled.BarChart">
        Reports <MudChip Size="Size.Small" Color="Color.Default" Class="ml-1">Soon</MudChip>
    </MudNavLink>
</MudNavMenu>
```

---

## FILE 26: Components/Pages/Dashboard.razor

```razor
@page "/"
@attribute [Authorize]
<PageTitle>Dashboard — FAM OS</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Dashboard</MudText>
<MudText Typo="Typo.body1" Color="Color.Secondary">
    Sprint 2: signal prioritization and pipeline summary will appear here.
</MudText>
```

---

## FILE 27: Components/Pages/Pipeline.razor

```razor
@page "/pipeline"
@attribute [Authorize]
<PageTitle>Pipeline — FAM OS</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Pipeline</MudText>
<MudText Typo="Typo.body1" Color="Color.Secondary">
    Sprint 2: Kanban board with lifecycle stage columns will appear here.
</MudText>
```

---

## FILE 28: Components/Pages/TaskCenter.razor

```razor
@page "/tasks"
@attribute [Authorize]
<PageTitle>Task Center — FAM OS</PageTitle>

<MudText Typo="Typo.h5" Class="mb-4">Task Center</MudText>
<MudText Typo="Typo.body1" Color="Color.Secondary">
    Sprint 3: task batching and reassignment will appear here.
</MudText>
```

---

## FILE 29: wwwroot/css/famos.css

```css
/* FAM OS — application styles */
/* Extends FipShared fip-tokens.css */

/* Pipeline board */
.famos-pipeline-board {
    display: flex;
    gap: 16px;
    overflow-x: auto;
    padding-bottom: 16px;
}

.famos-pipeline-column {
    min-width: 280px;
    max-width: 320px;
    background: var(--color-surface);
    border-radius: 8px;
    border: 1px solid var(--color-border);
}

.famos-pipeline-column-header {
    padding: 12px 16px;
    border-bottom: 1px solid var(--color-border);
    font-weight: var(--font-semibold);
    font-size: var(--text-sm);
    color: var(--color-text-secondary);
    text-transform: uppercase;
    letter-spacing: 0.05em;
}

/* Signal chips */
.famos-signal-chip {
    font-size: 11px;
    font-weight: 600;
    padding: 2px 8px;
    border-radius: 4px;
}

.famos-signal-time-risk     { background: #FEE2E2; color: #991B1B; }
.famos-signal-waiting-on-client { background: #FEF3C7; color: #92400E; }
.famos-signal-decision-required { background: #FED7AA; color: #9A3412; }
.famos-signal-waiting-on-market { background: #EDE9FE; color: #5B21B6; }
.famos-signal-awaiting-client   { background: #DBEAFE; color: #1E40AF; }
.famos-signal-binding           { background: #D1FAE5; color: #065F46; }
.famos-signal-underwriting      { background: #E0F2FE; color: #075985; }
.famos-signal-post-bind         { background: #CCFBF1; color: #134E4A; }
.famos-signal-parked            { background: #F3F4F6; color: #4B5563; }
```

---

## FILE 30: appsettings.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Warning"
    }
  },
  "AllowedHosts": "*",
  "FamOs": {
    "SubmissionAgingDays": 7,
    "ProposalAgingDays": 5
  }
}
```

---

## FILE 31: appsettings.Development.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "ConnectionStrings": {
    "Default": "Server=localhost;Database=famos_dev;User=root;Password=dev;"
  },
  "FamOs": {
    "SubmissionAgingDays": 1,
    "ProposalAgingDays": 1
  }
}
```

---

## FILE 32: Program.cs

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using MySqlConnector;
using FamOs.Web.Data;
using FamOs.Web.Domain;
using FamOs.Web.Services;
using FamOs.Web.Components;
using MudBlazor.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ── Blazor Server ──
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ── MudBlazor ──
builder.Services.AddMudServices();

// ── Authentication ──
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
.AddCookie(options =>
{
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

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = options.DefaultPolicy;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();

// ── EF Core (Aurora MySQL) ──
var dbHost = builder.Configuration["FORTRESS_DB_HOST"];
var dbPort = builder.Configuration["FORTRESS_DB_PORT"] ?? "3306";
var dbUser = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql";
var dbPass = builder.Configuration["FORTRESS_DB_PASS"] ?? "dev";
var dbName = builder.Configuration["FAMOS_DB_NAME"] ?? "famos_dev";

string connectionString;
if (!string.IsNullOrEmpty(dbHost))
{
    var csb = new MySqlConnectionStringBuilder
    {
        Server            = dbHost,
        Port              = uint.Parse(dbPort),
        Database          = dbName,
        UserID            = dbUser,
        Password          = dbPass,
        ConnectionTimeout = 10
    };
    connectionString = csb.ConnectionString;
    Console.WriteLine($"Using Aurora MySQL: {dbHost}/{dbName}");
}
else
{
    connectionString = builder.Configuration.GetConnectionString("Default")
        ?? "Server=localhost;Database=famos_dev;User=root;Password=dev;";
    Console.WriteLine("Using local connection string.");
}

var serverVersion = new MySqlServerVersion(new Version(8, 0, 28));
builder.Services.AddDbContextFactory<FamOsDbContext>(options =>
    options.UseMySql(connectionString, serverVersion,
        mysql => mysql.EnableRetryOnFailure(3)));

// ── Data Protection: shared key ring ──
var keyRingHost = builder.Configuration["FORTRESS_DB_HOST"];
var keyRingDb   = builder.Configuration["FIP_KEYRING_DB_NAME"] ?? "fred_dev";
var keyRingCsb  = new MySqlConnectionStringBuilder
{
    Server            = keyRingHost ?? "localhost",
    Port              = uint.Parse(builder.Configuration["FORTRESS_DB_PORT"] ?? "3306"),
    Database          = keyRingDb,
    UserID            = builder.Configuration["FORTRESS_DB_USER"] ?? "fortress_mysql",
    Password          = builder.Configuration["FORTRESS_DB_PASS"] ?? "",
    ConnectionTimeout = 10
};

builder.Services.AddDbContext<SharedKeyRingDbContext>(options =>
    options.UseMySql(keyRingCsb.ConnectionString,
        new MySqlServerVersion(new Version(8, 0, 28)),
        mysql => mysql.EnableRetryOnFailure(3)));

builder.Services.AddDataProtection()
    .PersistKeysToDbContext<SharedKeyRingDbContext>()
    .SetApplicationName("FortressAI")
    .DisableAutomaticKeyGeneration();

// ── Application Services ──
builder.Services.AddScoped<UserSessionService>();
builder.Services.AddScoped<SignalResolver>();
builder.Services.AddScoped<LifecycleCommandService>();
builder.Services.AddScoped<IHubSpotService, HubSpotServiceStub>();
builder.Services.AddScoped<IAmsService, AmsServiceStub>();

// ── Background Services ──
builder.Services.AddHostedService<OutboxProcessorService>();
builder.Services.AddHostedService<SignalRecomputeService>();

// ── Internal HTTP client ──
var internalBase = "http://localhost:8080/";
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(internalBase) });

var app = builder.Build();
var logger = app.Logger;

// ── Database initialization (background) ──
_ = Task.Run(async () =>
{
    await Task.Delay(5000);
    using var scope = app.Services.CreateScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<FamOsDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    try
    {
        bool tablesExist = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM opportunities LIMIT 1");
            tablesExist = true;
        }
        catch
        {
            // Expected on first run
        }

        if (!tablesExist)
        {
            try
            {
                var creator = db.Database.GetService<IRelationalDatabaseCreator>();
                await creator.CreateTablesAsync();
                Console.WriteLine("[FAM OS] Database tables created.");
            }
            catch (MySqlException ex) when (ex.Number == 1050)
            {
                logger.LogDebug("CreateTablesAsync: tables already exist (1050), continuing");
            }
        }
        else
        {
            Console.WriteLine("[FAM OS] DB tables already exist.");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "[FAM OS] Database initialization failed");
    }
});

// ── Middleware pipeline ──
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// ── Health check ──
app.MapGet("/health", () => Results.Ok(new {
    status    = "healthy",
    service   = "famos",
    timestamp = DateTime.UtcNow
})).AllowAnonymous();

// ── Auth redirect helper ──
app.MapGet("/auth/redirect-to-login", (HttpContext ctx) =>
{
    var faitUrl = builder.Configuration["FIP:LoginUrl"] ?? "https://fait.dev.fortressam.ai/";
    var returnUrl = Uri.EscapeDataString(ctx.Request.Headers.Referer.FirstOrDefault() ?? "/");
    return Results.Redirect($"{faitUrl}?returnUrl={returnUrl}");
}).AllowAnonymous().DisableAntiforgery();

app.MapGet("/auth/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    var faitUrl = builder.Configuration["FIP:LoginUrl"] ?? "https://fait.dev.fortressam.ai/";
    return Results.Redirect(faitUrl);
}).AllowAnonymous().DisableAntiforgery();

// ── Blazor Server ──
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
```

---

## FILE 33: Dockerfile (at ~/projects/fip/famos/Dockerfile)

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY shared/FipShared/ shared/FipShared/
COPY famos/src/FamOs.Web/FamOs.Web.csproj famos/src/FamOs.Web/
RUN dotnet restore "famos/src/FamOs.Web/FamOs.Web.csproj"

COPY famos/src/ famos/src/

WORKDIR "/src/famos/src/FamOs.Web"
RUN dotnet build "FamOs.Web.csproj" -c Release -o /app/build

FROM build AS publish
WORKDIR "/src/famos/src/FamOs.Web"
RUN dotnet publish "FamOs.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "FamOs.Web.dll"]
```

---

## FILE 34: buildspec.yml (at ~/projects/fip/famos/buildspec.yml)

```yaml
version: 0.2

phases:
  pre_build:
    commands:
      - echo Logging in to Amazon ECR...
      - aws ecr get-login-password --region $AWS_DEFAULT_REGION | docker login --username AWS --password-stdin $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com
      - IMAGE_TAG=${CODEBUILD_RESOLVED_SOURCE_VERSION:-latest}
  build:
    commands:
      - echo Build started on `date`
      - docker build -f famos/Dockerfile -t famos-web:$IMAGE_TAG .
      - docker tag famos-web:$IMAGE_TAG $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
  post_build:
    commands:
      - docker push $AWS_ACCOUNT_ID.dkr.ecr.$AWS_DEFAULT_REGION.amazonaws.com/famos-web:dev-latest
      - aws ecs update-service --cluster fortress-tools-cluster --service famos-dev --force-new-deployment --region $AWS_DEFAULT_REGION
      - echo Deploy triggered

env:
  variables:
    AWS_DEFAULT_REGION: us-east-1
    AWS_ACCOUNT_ID: 742932328420
```

---

## SUMMARY
Create ALL 34 files listed above (the 32 files in src/FamOs.Web plus Dockerfile and buildspec.yml in the famos/ directory). Write each file exactly as specified. Do not create any other files.

The Dockerfile and buildspec.yml go in ~/projects/fip/famos/ (NOT in src/FamOs.Web/).
All other files go in ~/projects/fip/famos/src/FamOs.Web/ with the subdirectory structure shown.
