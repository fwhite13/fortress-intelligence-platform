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
            e.Property(x => x.IntakeResponsesJson)
                .HasColumnName("intake_responses_json")
                .HasColumnType("mediumtext");
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
