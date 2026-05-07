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
    public DbSet<McpServer> McpServers => Set<McpServer>();
    public DbSet<McpUserToken> McpUserTokens => Set<McpUserToken>();
    public DbSet<DesignAgentSession> DesignAgentSessions => Set<DesignAgentSession>();
    public DbSet<DesignAgentArtifact> DesignAgentArtifacts => Set<DesignAgentArtifact>();
    public DbSet<PushedMessage> PushedMessages => Set<PushedMessage>();
    public DbSet<FeedbackSubmission> FeedbackSubmissions => Set<FeedbackSubmission>();

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
            entity.Property(e => e.TaskArn).HasColumnName("task_arn").HasMaxLength(500);
            entity.Property(e => e.PrivateIp).HasColumnName("private_ip").HasMaxLength(45);
            entity.Property(e => e.FargateStatus).HasColumnName("fargate_status").HasMaxLength(20);
            entity.Property(e => e.FargateSessionId).HasColumnName("fargate_session_id").HasMaxLength(200);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_sessions_user_id");
            entity.HasIndex(e => e.StartedAt).HasDatabaseName("ix_user_sessions_started_at");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.UserSessions)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // mcp_servers
        modelBuilder.Entity<McpServer>(entity =>
        {
            entity.ToTable("mcp_servers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.EndpointUrl).HasColumnName("endpoint_url").HasMaxLength(500).IsRequired();
            entity.Property(e => e.AuthType).HasColumnName("auth_type").HasMaxLength(20).IsRequired().HasDefaultValue("oauth_entra");
            entity.Property(e => e.DefaultRead).HasColumnName("default_read").HasDefaultValue(true);
            entity.Property(e => e.DefaultWrite).HasColumnName("default_write").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("ix_mcp_servers_name");
        });

        // mcp_user_tokens
        modelBuilder.Entity<McpUserToken>(entity =>
        {
            entity.ToTable("mcp_user_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.ServerName).HasColumnName("server_name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.AccessToken).HasColumnName("access_token").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").HasColumnType("TEXT");
            entity.Property(e => e.TokenExpiresAt).HasColumnName("token_expires_at").HasColumnType("datetime(6)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_mcp_user_tokens_user_id");
            entity.HasIndex(new[] { "UserId", "ServerName" }).IsUnique().HasDatabaseName("ix_mcp_user_tokens_user_server");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .HasConstraintName("fk_mcp_user_tokens_user")
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // design_agent_sessions
        modelBuilder.Entity<DesignAgentSession>(entity =>
        {
            entity.ToTable("design_agent_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").HasMaxLength(36);
            entity.Property(e => e.StitchProjectId).HasColumnName("stitch_project_id").HasMaxLength(200);
            entity.Property(e => e.DesignDna).HasColumnName("design_dna").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId);

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // design_agent_artifacts
        modelBuilder.Entity<DesignAgentArtifact>(entity =>
        {
            entity.ToTable("design_agent_artifacts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.ArtifactName).HasColumnName("artifact_name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.S3Key).HasColumnName("s3_key").HasMaxLength(500).IsRequired();
            entity.Property(e => e.StitchScreenId).HasColumnName("stitch_screen_id").HasMaxLength(200);
            entity.Property(e => e.IsFallback).HasColumnName("is_fallback").HasColumnType("tinyint(1)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.SessionId);

            entity.HasOne(e => e.Session)
                  .WithMany(s => s.Artifacts)
                  .HasForeignKey(e => e.SessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // pushed_messages
        modelBuilder.Entity<PushedMessage>(entity =>
        {
            entity.ToTable("pushed_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
            entity.Property(e => e.Content).HasColumnName("content").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.ExternalId).HasColumnName("external_id").HasMaxLength(100);
            entity.Property(e => e.IsRead).HasColumnName("is_read").HasDefaultValue(false);
            entity.Property(e => e.MeetingDate).HasColumnName("meeting_date").HasColumnType("datetime(6)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_pushed_messages_user_id");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_pushed_messages_created_at");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // feedback_submissions
        modelBuilder.Entity<FeedbackSubmission>(entity =>
        {
            entity.ToTable("feedback_submissions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.PageUrl).HasColumnName("page_url").HasMaxLength(500);
            entity.Property(e => e.ScreenshotS3Key).HasColumnName("screenshot_s3_key").HasMaxLength(500);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired().HasDefaultValue("pending");
            entity.Property(e => e.AdoWiId).HasColumnName("ado_wi_id").HasMaxLength(20);
            entity.Property(e => e.TriageResult).HasColumnName("triage_result").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.TriagedAt).HasColumnName("triaged_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_feedback_submissions_user_id");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_feedback_submissions_status");
        });
    }
}
