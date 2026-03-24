using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FamOs.Web.Data.Entities;
using FamOs.Web.Domain;

namespace FamOs.Web.Data;

public class FamOsDbContext : DbContext
{
    // TenantId for global query filters. Currently single-tenant (TenantId=1).
    // Future: inject via ITenantResolver or IHttpContextAccessor for multi-tenant support.
    private readonly int _tenantId = 1;

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

    public DbSet<ProgramVertical>               ProgramVerticals               => Set<ProgramVertical>();
    public DbSet<LineOfBusiness>                LinesOfBusiness                => Set<LineOfBusiness>();
    public DbSet<Requirement>                   Requirements                   => Set<Requirement>();
    public DbSet<Package>                       Packages                       => Set<Package>();
    public DbSet<PackageSelection>              PackageSelections              => Set<PackageSelection>();
    public DbSet<IncumbentPolicy>               IncumbentPolicies              => Set<IncumbentPolicy>();
    public DbSet<CoverageRemovalAcknowledgment> CoverageRemovalAcknowledgments => Set<CoverageRemovalAcknowledgment>();
    public DbSet<CarrierNote>                   CarrierNotes                   => Set<CarrierNote>();
    public DbSet<ComparisonDraft>               ComparisonDrafts               => Set<ComparisonDraft>();
    public DbSet<BenchmarkPremium>              BenchmarkPremiums              => Set<BenchmarkPremium>();
    public DbSet<CarrierBundleRule>             CarrierBundleRules             => Set<CarrierBundleRule>();

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
            e.Property(x => x.FortressRequestId).HasColumnName("fortress_request_id").HasMaxLength(200);
            e.Property(x => x.ScraperError).HasColumnName("scraper_error").HasColumnType("text");
            e.Property(x => x.Notes).HasColumnType("longtext");
            e.Property(x => x.CoverageLine).HasColumnName("CoverageLine").HasMaxLength(50);
            e.Property(x => x.LineStatus).HasColumnName("LineStatus").HasConversion<byte>();
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
            e.HasOne(x => x.Opportunity).WithMany(o => o.Tasks).HasForeignKey(x => x.OpportunityId).IsRequired(false);
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
            e.Property(x => x.IsRenewal).HasColumnName("is_renewal");
            e.Property(x => x.ProgramVerticalId).HasColumnType("char(36)").HasColumnName("program_vertical_id");
            // ADO#1016 — HubSpot field mapping columns
            e.Property(x => x.AccountStatus).HasMaxLength(20).HasColumnName("account_status");
            e.Property(x => x.PrimaryCoverage).HasMaxLength(100).HasColumnName("primary_coverage");
            e.Property(x => x.PrimaryCarrier).HasMaxLength(100).HasColumnName("primary_carrier");
            e.Property(x => x.PolicyExpiresAt).HasColumnName("policy_expires_at");
            e.Property(x => x.PrimaryDealId).HasMaxLength(50).HasColumnName("primary_deal_id");
            e.HasIndex(x => new { x.AffinityId, x.CompanyName });
        });

        // Quote — add new Quote Comparison fields
        m.Entity<Quote>(e => {
            e.Property(x => x.LineOfBusinessId).HasColumnType("char(36)").HasColumnName("line_of_business_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.CoverageLine).HasColumnName("CoverageLine").HasMaxLength(50);
        });

        // ProgramVertical
        m.Entity<ProgramVertical>(e => {
            e.ToTable("program_verticals");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Name).HasMaxLength(100).HasColumnName("name");
            e.Property(x => x.Slug).HasMaxLength(50).HasColumnName("slug");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.FaitPresetChips).HasColumnType("longtext").HasColumnName("fait_preset_chips");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // LineOfBusiness
        m.Entity<LineOfBusiness>(e => {
            e.ToTable("lines_of_business");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.ProgramVerticalId).HasColumnType("char(36)").HasColumnName("program_vertical_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Slug).HasMaxLength(50).HasColumnName("slug");
            e.Property(x => x.Name).HasMaxLength(100).HasColumnName("name");
            e.Property(x => x.Icon).HasMaxLength(20).HasColumnName("icon");
            e.Property(x => x.MetaDescription).HasMaxLength(255).HasColumnName("meta_description");
            e.Property(x => x.DisplayOrder).HasColumnName("display_order");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.FieldDefinitions).HasColumnType("longtext").HasColumnName("field_definitions");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // Requirement
        m.Entity<Requirement>(e => {
            e.ToTable("requirements");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.ProgramVerticalId).HasColumnType("char(36)").HasColumnName("program_vertical_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Slug).HasMaxLength(100).HasColumnName("slug");
            e.Property(x => x.Label).HasMaxLength(255).HasColumnName("label");
            e.Property(x => x.GroupName).HasMaxLength(100).HasColumnName("group_name");
            e.Property(x => x.LineOfBusinessId).HasColumnType("char(36)").HasColumnName("line_of_business_id");
            e.Property(x => x.DisplayOrder).HasColumnName("display_order");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // Package
        m.Entity<Package>(e => {
            e.ToTable("packages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.AccountId).HasColumnType("char(36)").HasColumnName("account_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Label).HasMaxLength(10).HasColumnName("label");
            e.Property(x => x.Status).HasMaxLength(20).HasColumnName("status");
            e.Property(x => x.TotalPremium).HasColumnType("decimal(12,2)").HasColumnName("total_premium");
            e.Property(x => x.CreatedByUserId).HasColumnType("char(36)").HasColumnName("created_by_user_id");
            e.Property(x => x.LastModifiedByUserId).HasColumnType("char(36)").HasColumnName("last_modified_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.AccountId, x.TenantId });
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // PackageSelection
        m.Entity<PackageSelection>(e => {
            e.ToTable("package_selections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.PackageId).HasColumnType("char(36)").HasColumnName("package_id");
            e.Property(x => x.LineOfBusinessId).HasColumnType("char(36)").HasColumnName("line_of_business_id");
            e.Property(x => x.QuoteId).HasColumnType("char(36)").HasColumnName("quote_id");
            e.Property(x => x.IsAutoBundle).HasColumnName("is_auto_bundle");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.PackageId, x.LineOfBusinessId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // IncumbentPolicy
        m.Entity<IncumbentPolicy>(e => {
            e.ToTable("incumbent_policies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.AccountId).HasColumnType("char(36)").HasColumnName("account_id");
            e.Property(x => x.LineOfBusinessId).HasColumnType("char(36)").HasColumnName("line_of_business_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.CarrierName).HasMaxLength(191).HasColumnName("carrier_name");
            e.Property(x => x.PolicyNumber).HasMaxLength(100).HasColumnName("policy_number");
            e.Property(x => x.AnnualPremium).HasColumnType("decimal(12,2)").HasColumnName("annual_premium");
            e.Property(x => x.EffectiveDate).HasColumnName("effective_date");
            e.Property(x => x.ExpirationDate).HasColumnName("expiration_date");
            e.Property(x => x.Vals).HasColumnType("longtext").HasColumnName("vals");
            e.Property(x => x.SourceType).HasMaxLength(20).HasColumnName("source_type");
            e.Property(x => x.ScraperRunId).HasMaxLength(100).HasColumnName("scraper_run_id");
            e.Property(x => x.IsOverridden).HasColumnName("is_overridden");
            e.Property(x => x.OverriddenByUserId).HasColumnType("char(36)").HasColumnName("overridden_by_user_id");
            e.Property(x => x.OverriddenAt).HasColumnName("overridden_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.AccountId, x.LineOfBusinessId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // CoverageRemovalAcknowledgment
        m.Entity<CoverageRemovalAcknowledgment>(e => {
            e.ToTable("coverage_removal_acknowledgments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.AccountId).HasColumnType("char(36)").HasColumnName("account_id");
            e.Property(x => x.PackageId).HasColumnType("char(36)").HasColumnName("package_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.AcknowledgedByUserId).HasColumnType("char(36)").HasColumnName("acknowledged_by_user_id");
            e.Property(x => x.AcknowledgedAt).HasColumnName("acknowledged_at");
            e.Property(x => x.CoverageDescription).HasMaxLength(255).HasColumnName("coverage_description");
            e.Property(x => x.LineOfBusinessId).HasColumnType("char(36)").HasColumnName("line_of_business_id");
            e.Property(x => x.IncumbentFieldKey).HasMaxLength(100).HasColumnName("incumbent_field_key");
            e.Property(x => x.IncumbentValue).HasMaxLength(255).HasColumnName("incumbent_value");
            e.Property(x => x.ProposedValue).HasMaxLength(255).HasColumnName("proposed_value");
            e.Property(x => x.ChangeType).HasMaxLength(20).HasColumnName("change_type");
            e.HasIndex(x => new { x.AccountId, x.AcknowledgedAt });
            e.HasIndex(x => new { x.AcknowledgedByUserId, x.AcknowledgedAt });
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // CarrierNote
        m.Entity<CarrierNote>(e => {
            e.ToTable("carrier_notes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.AccountId).HasColumnType("char(36)").HasColumnName("account_id");
            e.Property(x => x.QuoteId).HasColumnType("char(36)").HasColumnName("quote_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.NoteText).HasColumnType("longtext").HasColumnName("note_text");
            e.Property(x => x.CreatedByUserId).HasColumnType("char(36)").HasColumnName("created_by_user_id");
            e.Property(x => x.UpdatedByUserId).HasColumnType("char(36)").HasColumnName("updated_by_user_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.AccountId, x.QuoteId });
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // ComparisonDraft
        m.Entity<ComparisonDraft>(e => {
            e.ToTable("comparison_drafts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.AccountId).HasColumnType("char(36)").HasColumnName("account_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.UserId).HasColumnType("char(36)").HasColumnName("user_id");
            e.Property(x => x.ActiveRequirementSlugs).HasColumnType("longtext").HasColumnName("active_requirement_slugs");
            e.Property(x => x.PackageASelections).HasColumnType("longtext").HasColumnName("package_a_selections");
            e.Property(x => x.PackageBSelections).HasColumnType("longtext").HasColumnName("package_b_selections");
            e.Property(x => x.ShowIncumbent).HasColumnName("show_incumbent");
            e.Property(x => x.CollapsedBlocks).HasColumnType("longtext").HasColumnName("collapsed_blocks");
            e.Property(x => x.SavedAt).HasColumnName("saved_at");
            e.HasIndex(x => new { x.AccountId, x.UserId }).IsUnique();
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // BenchmarkPremium
        m.Entity<BenchmarkPremium>(e => {
            e.ToTable("benchmark_premiums");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.ProgramVerticalId).HasColumnType("char(36)").HasColumnName("program_vertical_id");
            e.Property(x => x.LineOfBusinessId).HasColumnType("char(36)").HasColumnName("line_of_business_id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.AnnualPremium).HasColumnType("decimal(12,2)").HasColumnName("annual_premium");
            e.Property(x => x.EffectiveDate).HasColumnName("effective_date");
            e.Property(x => x.Source).HasMaxLength(50).HasColumnName("source");
            e.Property(x => x.Notes).HasColumnType("longtext").HasColumnName("notes");
            e.Property(x => x.UpdatedByUserId).HasColumnType("char(36)").HasColumnName("updated_by_user_id");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => new { x.TenantId, x.ProgramVerticalId, x.LineOfBusinessId });
            e.HasQueryFilter(x => x.TenantId == _tenantId);
        });

        // CarrierBundleRule
        m.Entity<CarrierBundleRule>(e => {
            e.ToTable("carrier_bundle_rules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("char(36)");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.CarrierName).HasMaxLength(191).HasColumnName("carrier_name");
            e.Property(x => x.PrimaryLineSlug).HasMaxLength(50).HasColumnName("primary_line_slug");
            e.Property(x => x.RequiredLineSlug).HasMaxLength(50).HasColumnName("required_line_slug");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.Notes).HasColumnType("longtext").HasColumnName("notes");
            e.HasIndex(x => new { x.TenantId, x.CarrierName, x.PrimaryLineSlug });
            e.HasQueryFilter(x => x.TenantId == _tenantId);
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
