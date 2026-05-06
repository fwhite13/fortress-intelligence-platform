using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;
using FortressNexus.Web.Services;

namespace FortressNexus.Web.Data;

public class NexusDbContext : DbContext
{
    public NexusDbContext(DbContextOptions<NexusDbContext> options) : base(options) { }

    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<SubmissionFile> SubmissionFiles => Set<SubmissionFile>();
    public DbSet<SpecDocument> SpecDocuments => Set<SpecDocument>();
    public DbSet<ArtifactSet> ArtifactSets => Set<ArtifactSet>();
    public DbSet<WorkItemRecord> WorkItemRecords => Set<WorkItemRecord>();
    public DbSet<DiscoverySession> DiscoverySessions => Set<DiscoverySession>();
    public DbSet<DiscoveryQuestion> DiscoveryQuestions => Set<DiscoveryQuestion>();
    public DbSet<DiscoveryAnswer> DiscoveryAnswers => Set<DiscoveryAnswer>();

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
            entity.Property(e => e.FileType).HasColumnName("file_type").HasConversion<int>().IsRequired();
            entity.Property(e => e.UserDescription).HasColumnName("user_description").HasMaxLength(500);
            entity.HasMany(e => e.SubmissionFiles)
                .WithOne(sf => sf.UploadedFile)
                .HasForeignKey(sf => sf.UploadedFileId)
                .OnDelete(DeleteBehavior.Restrict);
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
            entity.Property(e => e.MockupFileId).HasColumnName("mockup_file_id").IsRequired(false);
            entity.Property(e => e.SubmittedBy).HasColumnName("submitted_by").HasMaxLength(100).IsRequired();
            entity.Property(e => e.SubmittedAt).HasColumnName("submitted_at").IsRequired();
            entity.Property(e => e.Status).HasColumnName("status")
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.ActiveSpecDocumentId).HasColumnName("active_spec_document_id");
            entity.Property(e => e.DiscoveryStatus).HasColumnName("discovery_status").HasMaxLength(50);
            entity.HasOne(e => e.MockupFile)
                .WithMany()
                .HasForeignKey(e => e.MockupFileId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
            entity.HasMany(e => e.SubmissionFiles)
                .WithOne(sf => sf.Submission)
                .HasForeignKey(sf => sf.SubmissionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.DiscoverySessions)
                  .WithOne(ds => ds.Submission)
                  .HasForeignKey(ds => ds.SubmissionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // SubmissionFile
        modelBuilder.Entity<SubmissionFile>(entity =>
        {
            entity.ToTable("submission_files");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id").IsRequired();
            entity.Property(e => e.UploadedFileId).HasColumnName("uploaded_file_id").IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").IsRequired();
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
            entity.Property(e => e.ExternalDependencyCount).HasColumnName("external_dependency_count").IsRequired();
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
            entity.Property(e => e.PredecessorTitles).HasColumnName("predecessor_titles")
                .HasColumnType("json")
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null))
                .Metadata.SetValueComparer(new ValueComparer<List<string>?>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => v == null ? null : JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)));
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("text");
            entity.Property(e => e.AcceptanceCriteria).HasColumnName("acceptance_criteria").HasColumnType("text");
            entity.Property(e => e.ParentTitle).HasColumnName("parent_title").HasMaxLength(500);
            entity.Property(e => e.IsExternalDependency).HasColumnName("is_external_dependency").IsRequired();
            entity.Property(e => e.ExternalOwner).HasColumnName("external_owner").HasMaxLength(100);
            entity.Property(e => e.WiTemplate).HasColumnName("wi_template")
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(e => e.TestedByTitles).HasColumnName("tested_by_titles")
                .HasColumnType("json")
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => v == null ? null : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null))
                .Metadata.SetValueComparer(new ValueComparer<List<string>?>(
                    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                    v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
                    v => v == null ? null : JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)));
            entity.HasOne(e => e.ArtifactSet)
                .WithMany(a => a.WorkItemRecords)
                .HasForeignKey(e => e.ArtifactSetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // DiscoverySession
        modelBuilder.Entity<DiscoverySession>(entity =>
        {
            entity.ToTable("discovery_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.SubmissionId).HasColumnName("submission_id");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(50);
            entity.Property(e => e.KbQueryUsed).HasColumnName("kb_query_used").HasMaxLength(1000);
            entity.Property(e => e.KbPassagesRetrieved).HasColumnName("kb_passages_retrieved");
            entity.Property(e => e.QuestionCount).HasColumnName("question_count");
            entity.Property(e => e.SkippedByUser).HasColumnName("skipped_by_user");
            entity.Property(e => e.GeneratedAt).HasColumnName("generated_at");
            entity.Property(e => e.AnsweredAt).HasColumnName("answered_at");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasMany(e => e.Questions)
                  .WithOne(q => q.Session)
                  .HasForeignKey(q => q.DiscoverySessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // DiscoveryQuestion
        modelBuilder.Entity<DiscoveryQuestion>(entity =>
        {
            entity.ToTable("discovery_questions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DiscoverySessionId).HasColumnName("discovery_session_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.QuestionText).HasColumnName("question_text").HasMaxLength(1000);
            entity.Property(e => e.Category).HasColumnName("category").HasMaxLength(50);
            entity.Property(e => e.IsBlocking).HasColumnName("is_blocking");
            entity.Property(e => e.Rationale).HasColumnName("rationale").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasOne(e => e.Answer)
                  .WithOne(a => a.Question)
                  .HasForeignKey<DiscoveryAnswer>(a => a.DiscoveryQuestionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // DiscoveryAnswer
        modelBuilder.Entity<DiscoveryAnswer>(entity =>
        {
            entity.ToTable("discovery_answers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.DiscoveryQuestionId).HasColumnName("discovery_question_id");
            entity.Property(e => e.AnswerText).HasColumnName("answer_text").HasMaxLength(2000);
            entity.Property(e => e.AnsweredBy).HasColumnName("answered_by").HasMaxLength(255);
            entity.Property(e => e.AnsweredAt).HasColumnName("answered_at");
        });
    }
}
