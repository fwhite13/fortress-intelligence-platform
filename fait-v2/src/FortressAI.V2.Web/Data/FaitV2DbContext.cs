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
    public DbSet<ArtifactRecord> ArtifactRecords => Set<ArtifactRecord>();
    public DbSet<AgentPlugin> AgentPlugins => Set<AgentPlugin>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();
    public DbSet<ScheduledTaskApproval> ScheduledTaskApprovals => Set<ScheduledTaskApproval>();
    public DbSet<ConversationTask> ConversationTasks => Set<ConversationTask>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<UserDevOpsConnection> UserDevOpsConnections => Set<UserDevOpsConnection>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<KbEntry> KbEntries => Set<KbEntry>();
    public DbSet<KbTeam> KbTeams => Set<KbTeam>();
    public DbSet<KbTeamMember> KbTeamMembers => Set<KbTeamMember>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── users (fait_dev: PascalCase Id/Email/DisplayName/CreatedAt, entra_oid snake_case) ──
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").HasMaxLength(36);
            entity.Property(e => e.EntraOid).HasColumnName("entra_oid").HasMaxLength(255).IsRequired(false);
            entity.Property(e => e.Email).HasColumnName("Email").HasMaxLength(255).IsRequired();
            entity.Property(e => e.DisplayName).HasColumnName("DisplayName").HasMaxLength(100).IsRequired(false);
            entity.Property(e => e.OnboardingCompletedAt).HasColumnName("onboarding_completed_at").HasColumnType("datetime(6)");
            entity.Property(e => e.OnboardingStep).HasColumnName("onboarding_step").HasColumnType("int").IsRequired(false);
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");
            entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(1000);

            entity.HasIndex(e => e.EntraOid).IsUnique().HasDatabaseName("ix_users_entra_oid");
            entity.HasIndex(e => e.Email).IsUnique().HasDatabaseName("IX_users_Email");
        });

        // ── main_assistants (new v2 table, FK → users.Id) ──────────────────────
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

        // ── projects (fait_dev: PascalCase Id/Name/etc., snake_case KB flags) ───
        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("UserId").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasColumnName("Description").HasColumnType("TEXT");
            entity.Property(e => e.V1ProjectId).HasColumnName("v1_project_id");
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("datetime(6)");
            entity.Property(e => e.CustomInstructions).HasColumnName("CustomInstructions").HasColumnType("TEXT");
            entity.Property(e => e.Model).HasColumnName("Model").HasMaxLength(100).HasDefaultValue("claude-sonnet-4-6");
            entity.Property(e => e.EnableFortressKb).HasColumnName("enable_fortress_kb").HasColumnType("tinyint(1)").HasDefaultValue(false);
            entity.Property(e => e.EnablePersonalKb).HasColumnName("enable_personal_kb").HasColumnType("tinyint(1)").HasDefaultValue(false);

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_projects_user_id");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.Projects)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Documents)
                  .WithOne(d => d.Project)
                  .HasForeignKey(d => d.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.ConversationTasks)
                  .WithOne(ct => ct.Project)
                  .HasForeignKey(ct => ct.ProjectId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ── memory_topics (new v2 table) ─────────────────────────────────────────
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

        // ── user_sessions (new v2 table) ─────────────────────────────────────────
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
            entity.Property(e => e.TaskDefinitionRevision).HasColumnName("task_definition_revision").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_sessions_user_id");
            entity.HasIndex(e => e.StartedAt).HasDatabaseName("ix_user_sessions_started_at");

            entity.HasOne(e => e.User)
                  .WithMany(u => u.UserSessions)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── mcp_servers (fait_dev: snake_case, nullable endpoint_url, slug NOT NULL) ──
        modelBuilder.Entity<McpServer>(entity =>
        {
            entity.ToTable("mcp_servers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Slug).HasColumnName("slug").HasMaxLength(50).IsRequired();
            entity.Property(e => e.EndpointUrl).HasColumnName("endpoint_url").HasMaxLength(500).IsRequired(false);
            entity.Property(e => e.AuthType).HasColumnName("auth_type").HasMaxLength(20).IsRequired().HasDefaultValue("oauth_entra");
            entity.Property(e => e.DefaultRead).HasColumnName("default_read").HasDefaultValue(true);
            entity.Property(e => e.DefaultWrite).HasColumnName("default_write").HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("ix_mcp_servers_name");
        });

        // ── user_mcp_tokens (v1 table name; server_name is new column) ───────────
        modelBuilder.Entity<McpUserToken>(entity =>
        {
            entity.ToTable("user_mcp_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.ServerName).HasColumnName("server_name").HasMaxLength(100).IsRequired(false);
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

        // ── design_agent_sessions (new v2 table) ─────────────────────────────────
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

        // ── design_agent_artifacts (new v2 table) ────────────────────────────────
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

        // ── pushed_messages (new v2 table) ───────────────────────────────────────
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

        // ── artifact_records (new v2 table) ──────────────────────────────────────
        modelBuilder.Entity<ArtifactRecord>(entity =>
        {
            entity.ToTable("artifact_records");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(20).IsRequired();
            entity.Property(e => e.FileName).HasColumnName("file_name").HasMaxLength(500).IsRequired();
            entity.Property(e => e.S3Key).HasColumnName("s3_key").HasMaxLength(500).IsRequired();
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.TaskDescription).HasColumnName("task_description").HasColumnType("TEXT");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_artifact_records_user_id");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_artifact_records_created_at");
        });

        // ── feedback_submissions (new v2 table) ───────────────────────────────────
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

        // ── scheduled_tasks (new v2 table) ───────────────────────────────────────
        modelBuilder.Entity<ScheduledTask>(entity =>
        {
            entity.ToTable("scheduled_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.ProjectId).HasColumnName("project_id").HasMaxLength(36);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Prompt).HasColumnName("prompt").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.ScheduleType).HasColumnName("schedule_type").HasMaxLength(20).IsRequired();
            entity.Property(e => e.CronExpression).HasColumnName("cron_expression").HasMaxLength(100);
            entity.Property(e => e.NextRunAt).HasColumnName("next_run_at").HasColumnType("datetime(6)");
            entity.Property(e => e.LastRunAt).HasColumnName("last_run_at").HasColumnType("datetime(6)");
            entity.Property(e => e.LastRunStatus).HasColumnName("last_run_status").HasMaxLength(20);
            entity.Property(e => e.FailureCount).HasColumnName("failure_count");
            entity.Property(e => e.AlertOnCompletion).HasColumnName("alert_on_completion");
            entity.Property(e => e.AlertOnFailure).HasColumnName("alert_on_failure");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.TaskMode).HasColumnName("task_mode").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_scheduled_tasks_user_id");
            entity.HasIndex(e => e.NextRunAt).HasDatabaseName("ix_scheduled_tasks_next_run_at");
        });

        // ── scheduled_task_runs (new v2 table) ───────────────────────────────────
        modelBuilder.Entity<ScheduledTaskRun>(entity =>
        {
            entity.ToTable("scheduled_task_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.TaskId).HasColumnName("task_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.StartedAt).HasColumnName("started_at").HasColumnType("datetime(6)");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at").HasColumnType("datetime(6)");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("TEXT");
            entity.Property(e => e.ArtifactS3Key).HasColumnName("artifact_s3_key").HasMaxLength(500);
            entity.Property(e => e.SandboxId).HasColumnName("sandbox_id").HasMaxLength(200);
            entity.Property(e => e.OutputText).HasColumnName("output_text").HasColumnType("LONGTEXT");

            entity.HasIndex(e => e.TaskId).HasDatabaseName("ix_scheduled_task_runs_task_id");

            entity.HasOne(e => e.Task)
                  .WithMany()
                  .HasForeignKey(e => e.TaskId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── conversation_tasks (new v2 table) ────────────────────────────────────
        modelBuilder.Entity<ConversationTask>(entity =>
        {
            entity.ToTable("conversation_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasColumnType("char(36)").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("user_id").HasColumnType("char(36)").IsRequired();
            entity.Property(e => e.Title).HasColumnName("title").HasColumnType("varchar(500)");
            entity.Property(e => e.ProjectId).HasColumnName("project_id").HasColumnType("char(36)");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_conversation_tasks_user_id");
            entity.HasIndex(e => e.UpdatedAt).HasDatabaseName("ix_conversation_tasks_updated_at");
            entity.HasIndex(e => e.ProjectId).HasDatabaseName("ix_conversation_tasks_project_id");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .HasConstraintName("fk_conversation_tasks_user")
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── user_devops_connections (fait_dev: snake_case, already compatible) ───
        modelBuilder.Entity<UserDevOpsConnection>(entity =>
        {
            entity.ToTable("user_devops_connections");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36);
            entity.Property(e => e.OrgUrl).HasColumnName("org_url").HasMaxLength(500).IsRequired();
            entity.Property(e => e.PatEncrypted).HasColumnName("pat_encrypted").HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_devops_connections_user_id");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .HasConstraintName("fk_user_devops_connections_user")
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── agent_plugins (new v2 table) ──────────────────────────────────────────
        modelBuilder.Entity<AgentPlugin>(entity =>
        {
            entity.ToTable("agent_plugins");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasColumnType("TEXT");
            entity.Property(e => e.SkillsDirectory).HasColumnName("skills_directory").HasMaxLength(500);
            entity.Property(e => e.AllowedMcpServers).HasColumnName("allowed_mcp_servers").HasColumnType("longtext");
            entity.Property(e => e.AllowedRoles).HasColumnName("allowed_roles").HasColumnType("longtext");
            entity.Property(e => e.AllowKbWrite)
                .HasColumnName("allow_kb_write")
                .HasDefaultValue(false);
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(36);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.IsActive).HasDatabaseName("ix_agent_plugins_is_active");
            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("ix_agent_plugins_name");
        });

        // ── conversations (fait_dev: PascalCase Id/UserId/Title/CreatedAt) ───────
        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").HasMaxLength(36);
            entity.Property(e => e.UserId).HasColumnName("UserId").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime(6)");
            entity.Property(e => e.LastActiveAt).HasColumnName("last_active_at").HasColumnType("datetime(6)");
            entity.Property(e => e.EstimatedTokenCount).HasColumnName("estimated_token_count").HasDefaultValue(0);

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_conversations_user_id");
            entity.HasIndex(e => e.LastActiveAt).HasDatabaseName("ix_conversations_last_active_at");

            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .HasConstraintName("fk_conversations_user")
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── messages (fait_dev: PascalCase Id/ConversationId/Role/Content/CreatedAt) ──
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").HasMaxLength(36);
            entity.Property(e => e.ConversationId).HasColumnName("ConversationId").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Role).HasColumnName("Role").HasMaxLength(20).IsRequired();
            entity.Property(e => e.Content).HasColumnName("Content").HasColumnType("longtext").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime(6)");
            entity.Property(e => e.CompactedAt).HasColumnName("compacted_at").HasColumnType("datetime(6)");
            entity.Property(e => e.IsCompactionSummary).HasColumnName("is_compaction_summary").HasColumnType("tinyint(1)").HasDefaultValue(false);
            entity.Property(e => e.SessionType).HasColumnName("session_type").HasMaxLength(10).HasDefaultValue("main");
            entity.Property(e => e.PluginAgentId).HasColumnName("plugin_agent_id").HasMaxLength(50);
            entity.Property(e => e.TokenCount).HasColumnName("token_count").HasDefaultValue(0);

            entity.HasIndex(e => e.ConversationId).HasDatabaseName("ix_messages_conversation_id");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("ix_messages_created_at");

            entity.HasOne(e => e.Conversation)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.ConversationId)
                  .HasConstraintName("fk_messages_conversation")
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── project_documents (fait_dev: PascalCase columns) ─────────────────────
        modelBuilder.Entity<ProjectDocument>(entity =>
        {
            entity.ToTable("project_documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").HasMaxLength(36);
            entity.Property(e => e.ProjectId).HasColumnName("ProjectId").HasMaxLength(36);
            entity.Property(e => e.Filename).HasColumnName("Filename").HasMaxLength(500).IsRequired();
            entity.Property(e => e.ContentType).HasColumnName("ContentType").HasMaxLength(200);
            entity.Property(e => e.Content).HasColumnName("Content").HasColumnType("longtext");
            entity.Property(e => e.FileSize).HasColumnName("FileSize");
            entity.Property(e => e.UploadedAt).HasColumnName("UploadedAt").HasColumnType("datetime(6)");
            entity.Property(e => e.S3Key).HasColumnName("S3Key").HasMaxLength(1000);
            entity.Property(e => e.IngestionStatus).HasColumnName("IngestionStatus").HasMaxLength(50).HasDefaultValue("none");
            entity.Property(e => e.IngestedAt).HasColumnName("IngestedAt").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.ProjectId).HasDatabaseName("ix_project_documents_project_id");

            entity.HasOne(e => e.Project)
                  .WithMany(p => p.Documents)
                  .HasForeignKey(e => e.ProjectId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── kb_entries (fait_dev: PascalCase, int PK auto-increment) ─────────────
        modelBuilder.Entity<KbEntry>(entity =>
        {
            entity.ToTable("kb_entries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("UserId").HasMaxLength(36).IsRequired();
            entity.Property(e => e.TeamId).HasColumnName("TeamId");
            entity.Property(e => e.Tier).HasColumnName("Tier");
            entity.Property(e => e.Title).HasColumnName("Title").HasMaxLength(500);
            entity.Property(e => e.Content).HasColumnName("Content").HasColumnType("longtext");
            entity.Property(e => e.Tags).HasColumnName("Tags").HasMaxLength(1000);
            entity.Property(e => e.SourceUrl).HasColumnName("SourceUrl").HasMaxLength(2000);
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("UpdatedAt").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_kb_entries_user_id");
            entity.HasIndex(e => e.TeamId).HasDatabaseName("ix_kb_entries_team_id");

            entity.HasOne(e => e.Team)
                  .WithMany(t => t.Entries)
                  .HasForeignKey(e => e.TeamId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ── kb_teams (fait_dev: PascalCase, int PK auto-increment) ───────────────
        modelBuilder.Entity<KbTeam>(entity =>
        {
            entity.ToTable("kb_teams");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.CreatorId).HasColumnName("CreatorId").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Name).HasColumnName("Name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Description).HasColumnName("Description").HasMaxLength(1000);
            entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.CreatorId).HasDatabaseName("ix_kb_teams_creator_id");
        });

        // ── kb_team_members (fait_dev: PascalCase, int PK + int TeamId) ──────────
        modelBuilder.Entity<KbTeamMember>(entity =>
        {
            entity.ToTable("kb_team_members");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("Id").ValueGeneratedOnAdd();
            entity.Property(e => e.TeamId).HasColumnName("TeamId").IsRequired();
            entity.Property(e => e.UserId).HasColumnName("UserId").HasMaxLength(36).IsRequired();
            entity.Property(e => e.Role).HasColumnName("Role");
            entity.Property(e => e.JoinedAt).HasColumnName("JoinedAt").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.TeamId).HasDatabaseName("ix_kb_team_members_team_id");
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_kb_team_members_user_id");
            entity.HasIndex(new[] { "TeamId", "UserId" }).IsUnique().HasDatabaseName("ix_kb_team_members_team_user");

            entity.HasOne(e => e.Team)
                  .WithMany(t => t.Members)
                  .HasForeignKey(e => e.TeamId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── scheduled_task_approvals (new v2 table) ───────────────────────────────
        modelBuilder.Entity<ScheduledTaskApproval>(entity =>
        {
            entity.ToTable("scheduled_task_approvals");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").HasMaxLength(36);
            entity.Property(e => e.ScheduledTaskId).HasColumnName("scheduled_task_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.InterventionId).HasColumnName("intervention_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.ActionType).HasColumnName("action_type").HasMaxLength(100);
            entity.Property(e => e.ActionSummary).HasColumnName("action_summary").HasMaxLength(2000);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("datetime(6)");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("datetime(6)");
            entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at").HasColumnType("datetime(6)");

            entity.HasIndex(e => e.ScheduledTaskId).HasDatabaseName("ix_scheduled_task_approvals_task_id");
            entity.HasIndex(e => e.Status).HasDatabaseName("ix_scheduled_task_approvals_status");
        });
    }
}
