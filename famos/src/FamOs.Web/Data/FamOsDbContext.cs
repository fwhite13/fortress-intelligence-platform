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
    public DbSet<Contact>              Contacts              => Set<Contact>();
    public DbSet<OpportunityDocument>  Documents             => Set<OpportunityDocument>();
    public DbSet<Account>    Accounts   => Set<Account>();
    public DbSet<TeamNote>   TeamNotes  => Set<TeamNote>();

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
            e.Property(x => x.CloseReason).HasConversion<int?>();
            e.Property(x => x.CloseNotes).HasColumnType("longtext");
            e.Property(x => x.LastStageTransitionAt).HasColumnType("datetime");
            e.Property(x => x.PrimaryContactId).HasColumnType("char(36)");
            // Sprint 7 column mappings
            e.Property(x => x.BindConfirmationNumber).HasMaxLength(100).HasColumnName("bind_confirmation_number");
            e.Property(x => x.BindRequestSubmittedAt).HasColumnType("datetime").HasColumnName("bind_request_submitted_at");
            // Sprint 8 column mappings
            e.Property(x => x.AffinityId).HasMaxLength(50).HasColumnName("affinity_id")
                .HasDefaultValue("tig");
        });

        // Submission
        m.Entity<Submission>(e => {
            e.ToTable("submissions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.Property(x => x.Status).HasConversion<int>();
            e.Property(x => x.QuoteResultJson).HasColumnType("mediumtext");
            e.Property(x => x.Notes).HasColumnType("longtext");
            e.HasOne(x => x.Opportunity)
                .WithMany(o => o.Submissions)
                .HasForeignKey(x => x.OpportunityId);
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
            // Sprint 7 column mappings
            e.Property(x => x.Notes).HasColumnType("longtext");
            e.Property(x => x.CarrierName).HasMaxLength(200).HasColumnName("carrier_name");
            e.Property(x => x.CoverageTypes).HasMaxLength(200).HasColumnName("coverage_types");
            e.Property(x => x.ProposalDate).HasColumnName("proposal_date");
            e.HasOne(x => x.Opportunity).WithMany(o => o.Proposals).HasForeignKey(x => x.OpportunityId);
        });

        // PolicyShadowRecord
        m.Entity<PolicyShadowRecord>(e => {
            e.ToTable("policy_shadow_records");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)");
            e.Property(x => x.PremiumAmount).HasPrecision(18, 2);
            // Sprint 7 column mappings
            e.Property(x => x.PolicyNumber).HasMaxLength(100).HasColumnName("policy_number");
            e.Property(x => x.ExpirationDate).HasColumnType("date").HasColumnName("expiration_date");
            e.Property(x => x.CoverageType).HasMaxLength(100).HasColumnName("coverage_type");
            e.Property(x => x.BoundAt).HasColumnType("datetime").HasColumnName("bound_at");
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

        // Contact — table uses snake_case columns; map all non-trivial properties
        m.Entity<Contact>(e => {
            e.ToTable("contacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)").HasColumnName("opportunity_id");
            e.Property(x => x.FirstName).HasColumnName("first_name");
            e.Property(x => x.LastName).HasColumnName("last_name");
            e.Property(x => x.ContactType).HasConversion<int>().HasColumnName("contact_type");
            e.Property(x => x.Notes).HasColumnType("longtext");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.Opportunity)
                .WithMany(o => o.Contacts)
                .HasForeignKey(x => x.OpportunityId);
        });

        // OpportunityDocument — table uses snake_case columns; map all non-trivial properties
        m.Entity<OpportunityDocument>(e => {
            e.ToTable("opportunity_documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.OpportunityId).HasColumnType("char(36)").HasColumnName("opportunity_id");
            e.Property(x => x.FileName).HasColumnName("file_name");
            e.Property(x => x.FileType).HasColumnName("file_type");
            e.Property(x => x.S3Key).HasColumnName("s3_key");
            e.Property(x => x.DocumentCategory).HasConversion<int>().HasColumnName("document_category");
            e.Property(x => x.UploadedAt).HasColumnName("uploaded_at");
            e.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
            e.HasOne(x => x.Opportunity)
                .WithMany(o => o.Documents)
                .HasForeignKey(x => x.OpportunityId);
        });

        // TeamNote
        m.Entity<TeamNote>(e =>
        {
            e.ToTable("team_notes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.AuthorId).HasColumnName("author_id").HasMaxLength(255).IsRequired();
            e.Property(x => x.NoteText).HasColumnName("note_text").HasColumnType("text").IsRequired();
            e.Property(x => x.OpportunityId).HasColumnName("opportunity_id");
            e.Property(x => x.TeamTag).HasColumnName("team_tag").HasMaxLength(20).HasDefaultValue("TIG");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        // Account — local cache of HubSpot companies; full snake_case column mapping
        m.Entity<Account>(e => {
            e.ToTable("accounts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.AffinityId).HasMaxLength(50).HasColumnName("affinity_id");
            e.Property(x => x.CompanyName).HasMaxLength(255).HasColumnName("company_name");
            e.Property(x => x.HubSpotId).HasMaxLength(50).HasColumnName("hubspot_id");
            e.Property(x => x.City).HasMaxLength(100).HasColumnName("city");
            e.Property(x => x.State).HasMaxLength(10).HasColumnName("state");
            e.Property(x => x.ActiveOppCount).HasColumnName("active_opp_count");
            e.Property(x => x.LastSyncedAt).HasColumnName("last_synced_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.AffinityId, x.CompanyName });
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
