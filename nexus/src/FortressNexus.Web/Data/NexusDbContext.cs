using Microsoft.EntityFrameworkCore;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Data;

public class NexusDbContext : DbContext
{
    public NexusDbContext(DbContextOptions<NexusDbContext> options) : base(options) { }

    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SpecDocument> SpecDocuments => Set<SpecDocument>();
    public DbSet<ArtifactSet> ArtifactSets => Set<ArtifactSet>();
    public DbSet<WorkItemRecord> WorkItemRecords => Set<WorkItemRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // UploadedFile
        modelBuilder.Entity<UploadedFile>(entity =>
        {
            entity.ToTable("uploaded_files");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.ContentType).HasColumnName("content_type").HasMaxLength(100).IsRequired();
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes").IsRequired();
            entity.Property(e => e.S3Key).HasColumnName("s3_key").HasMaxLength(500).IsRequired();
            entity.Property(e => e.S3Bucket).HasColumnName("s3_bucket").HasMaxLength(100).IsRequired();
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by").HasMaxLength(100).IsRequired();
            entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at").IsRequired();
            entity.Property(e => e.ProcessedText).HasColumnName("processed_text");
            entity.Ignore(e => e.Submissions);
        });

        // Submission
        modelBuilder.Entity<Submission>(entity =>
        {
            entity.ToTable("submissions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
            entity.Property(e => e.FeatureArea).HasColumnName("feature_area").HasMaxLength(100);
            entity.Property(e => e.NarrativeText).HasColumnName("narrative_text").IsRequired();
            entity.Property(e => e.MockupFileId).HasColumnName("mockup_file_id").IsRequired();
            entity.Property(e => e.SubmittedBy).HasColumnName("submitted_by").HasMaxLength(100).IsRequired();
            entity.Property(e => e.SubmittedAt).HasColumnName("submitted_at").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.ActiveSpecDocumentId).HasColumnName("active_spec_document_id");
            entity.HasOne(e => e.MockupFile)
                .WithMany(f => f.Submissions)
                .HasForeignKey(e => e.MockupFileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // SpecDocument
        modelBuilder.Entity<SpecDocument>(entity =>
        {
            entity.ToTable("spec_documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id").IsRequired();
            entity.Property(e => e.Version).HasColumnName("version").IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.GeneratedAt).HasColumnName("generated_at").IsRequired();
            entity.Property(e => e.GeneratedBy).HasColumnName("generated_by").HasMaxLength(100).IsRequired();
            entity.Property(e => e.EditedContent).HasColumnName("edited_content");
            entity.Property(e => e.EditedAt).HasColumnName("edited_at");
            entity.Property(e => e.EditedBy).HasColumnName("edited_by").HasMaxLength(100);
            entity.Property(e => e.IsApproved).HasColumnName("is_approved").IsRequired();
            entity.Property(e => e.ApprovedAt).HasColumnName("approved_at");
            entity.Property(e => e.ApprovedBy).HasColumnName("approved_by").HasMaxLength(100);
            entity.Property(e => e.PromptTokensUsed).HasColumnName("prompt_tokens_used").IsRequired();
            entity.Property(e => e.CompletionTokensUsed).HasColumnName("completion_tokens_used").IsRequired();
            entity.HasOne(e => e.Submission)
                .WithMany(s => s.SpecDocuments)
                .HasForeignKey(e => e.SubmissionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ArtifactSet
        modelBuilder.Entity<ArtifactSet>(entity =>
        {
            entity.ToTable("artifact_sets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.SpecDocumentId).HasColumnName("spec_document_id").IsRequired();
            entity.Property(e => e.AdoOrganization).HasColumnName("ado_organization").HasMaxLength(200).IsRequired();
            entity.Property(e => e.AdoProjectName).HasColumnName("ado_project_name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.AdoProjectId).HasColumnName("ado_project_id").HasMaxLength(50);
            entity.Property(e => e.ProcessTemplateTypeId).HasColumnName("process_template_type_id").HasMaxLength(50).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Status).HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.ErrorDetail).HasColumnName("error_detail");
            entity.HasOne(e => e.SpecDocument)
                .WithMany(s => s.ArtifactSets)
                .HasForeignKey(e => e.SpecDocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // WorkItemRecord
        modelBuilder.Entity<WorkItemRecord>(entity =>
        {
            entity.ToTable("work_item_records");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.ArtifactSetId).HasColumnName("artifact_set_id").IsRequired();
            entity.Property(e => e.AdoWorkItemId).HasColumnName("ado_work_item_id").IsRequired();
            entity.Property(e => e.AdoWorkItemUrl).HasColumnName("ado_work_item_url").HasMaxLength(500).IsRequired();
            entity.Property(e => e.WorkItemType).HasColumnName("work_item_type").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
            entity.Property(e => e.ErrorDetail).HasColumnName("error_detail").HasMaxLength(1000);
            entity.HasOne(e => e.ArtifactSet)
                .WithMany(a => a.WorkItemRecords)
                .HasForeignKey(e => e.ArtifactSetId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
