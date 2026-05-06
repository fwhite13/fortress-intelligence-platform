using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Data;

public class FaitV2DbContext : DbContext
{
    public FaitV2DbContext(DbContextOptions<FaitV2DbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<MainAssistant> MainAssistants => Set<MainAssistant>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<MemoryTopic> MemoryTopics => Set<MemoryTopic>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // users
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.EntraOid).HasColumnName("entra_oid").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(200).IsRequired();
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.OnboardingCompletedAt).HasColumnName("onboarding_completed_at").HasColumnType("datetime(6)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.EntraOid).IsUnique().HasDatabaseName("ix_users_entra_oid");
            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("ix_users_email");
        });

        // main_assistants
        modelBuilder.Entity<MainAssistant>(entity =>
        {
            entity.ToTable("main_assistants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.SoulBlobPath).HasColumnName("soul_blob_path").HasMaxLength(500).IsRequired();
            entity.Property(e => e.MemoryBlobPath).HasColumnName("memory_blob_path").HasMaxLength(500).IsRequired();
            entity.Property(e => e.WorkspaceS3Prefix).HasColumnName("workspace_s3_prefix").HasMaxLength(500).IsRequired();
            entity.Property(e => e.FargateSessionId).HasColumnName("fargate_session_id").HasMaxLength(200);
            entity.Property(e => e.FargateTaskArn).HasColumnName("fargate_task_arn").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("ix_main_assistants_user_id");

            entity.HasOne(e => e.User)
                  .WithOne(u => u.MainAssistant)
                  .HasForeignKey<MainAssistant>(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // projects
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("TEXT");
            entity.Property(e => e.V1ProjectId).HasColumnName("v1_project_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_projects_user_id");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.Projects)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // memory_topics
        modelBuilder.Entity<MemoryTopic>(entity =>
        {
            entity.ToTable("memory_topics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.TopicName).HasColumnName("topic_name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.TopicSlug).HasColumnName("topic_slug").HasMaxLength(200).IsRequired();
            entity.Property(e => e.BlobPath).HasColumnName("blob_path").HasMaxLength(500).IsRequired();
            entity.Property(e => e.LastUpdatedAt).HasColumnName("last_updated_at").HasColumnType("datetime(6)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_memory_topics_user_id");
            entity.HasIndex(new[] { "UserId", "TopicSlug" }).IsUnique().HasDatabaseName("ix_memory_topics_user_slug");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.MemoryTopics)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // user_sessions
        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("datetime(6)");
            entity.Property(e => e.LastActiveAt).HasColumnName("last_active_at").HasColumnType("datetime(6)");
            entity.Property(e => e.EndedAt).HasColumnName("ended_at").HasColumnType("datetime(6)");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(50);
            entity.Property(e => e.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_sessions_user_id");
            entity.HasIndex(e => e.StartedAt).HasDatabaseName("ix_user_sessions_started_at");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.UserSessions)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
