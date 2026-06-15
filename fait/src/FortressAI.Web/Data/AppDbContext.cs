using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data.Models;

namespace FortressAI.Web.Data;

public class AppDbContext : DbContext, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // DataProtection key persistence (prevents antiforgery token failures after container restart)
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<UserAssistantConfig> UserAssistantConfigs => Set<UserAssistantConfig>();
    public DbSet<BriefingHistory> BriefingHistories => Set<BriefingHistory>();
    public DbSet<UserBriefingSchedule> UserBriefingSchedules => Set<UserBriefingSchedule>();
    public DbSet<UserMicrosoftToken> UserMicrosoftTokens => Set<UserMicrosoftToken>();
    public DbSet<UserDevOpsConnection> UserDevOpsConnections => Set<UserDevOpsConnection>();
    public DbSet<GraphSubscription> GraphSubscriptions => Set<GraphSubscription>();
    public DbSet<EmailAlert> EmailAlerts => Set<EmailAlert>();
    public DbSet<EmailLog> EmailLogs => Set<EmailLog>();
    public DbSet<TaskItem> TaskCache => Set<TaskItem>();
    public DbSet<CalendarEvent> CalendarCache => Set<CalendarEvent>();
    public DbSet<PostMeetingNote> PostMeetingNotes => Set<PostMeetingNote>();
    public DbSet<KbEntry> KbEntries => Set<KbEntry>();
    public DbSet<KbTeam> KbTeams => Set<KbTeam>();
    public DbSet<KbTeamMember> KbTeamMembers => Set<KbTeamMember>();
    public DbSet<McpServer> McpServers => Set<McpServer>();
    public DbSet<UserMcpToken> UserMcpTokens => Set<UserMcpToken>();
    public DbSet<ConversationMcpServer> ConversationMcpServers => Set<ConversationMcpServer>();
    public DbSet<McpToolCallLog> McpToolCallLogs => Set<McpToolCallLog>();
    public DbSet<ConversationTeamKb> ConversationTeamKbs { get; set; } = null!;
    public DbSet<UserModulePermission> UserModulePermissions { get; set; } = null!;
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<ScheduledTask> ScheduledTasks => Set<ScheduledTask>();
    public DbSet<ScheduledTaskRun> ScheduledTaskRuns => Set<ScheduledTaskRun>();
    public DbSet<MemoryTopic> MemoryTopics => Set<MemoryTopic>();
    // UserWorkspaceFiles DbSet removed — user_workspace_files table dropped in WorkspaceSchemaUnify migration
    public DbSet<WorkspaceFolder> WorkspaceFolders { get; set; }
    public DbSet<WorkspaceUpload> WorkspaceUploads { get; set; }
    public DbSet<WorkspaceFileVersion> WorkspaceFileVersions { get; set; }
    public DbSet<FeedbackSubmission> FeedbackSubmissions => Set<FeedbackSubmission>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(e => e.Id);
            // EF Core generates Guid client-side (Guid.NewGuid()) — no server-side default needed
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).HasMaxLength(255).IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(100);
            entity.Property(e => e.Role).HasMaxLength(20).HasDefaultValue("user");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.IsEntraUser).HasColumnName("is_entra_user").HasDefaultValue(false);
            entity.Property(e => e.EntraOid).HasColumnName("entra_oid").HasMaxLength(255);
            entity.Property(e => e.OnboardingCompletedAt).HasColumnName("onboarding_completed_at");
            entity.Property(e => e.OnboardingStep).HasColumnName("onboarding_step");
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Model).HasMaxLength(100).HasDefaultValue("claude-sonnet-4-6");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany(u => u.Projects).HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<ProjectDocument>(entity =>
        {
            entity.ToTable("project_documents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Filename).HasMaxLength(500).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100);
            entity.Property(e => e.UploadedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.Project).WithMany(p => p.Documents).HasForeignKey(e => e.ProjectId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Title).HasMaxLength(500);
            entity.Property(e => e.Model).HasMaxLength(100).HasDefaultValue("claude-sonnet-4-6");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany(u => u.Conversations).HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.Project).WithMany(p => p.Conversations).HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.WorkingFolderId)
                  .HasColumnName("working_folder_id");
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.ToTable("messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Role).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Model).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.TokensIn).HasColumnName("TokensIn");
            entity.Property(e => e.TokensOut).HasColumnName("TokensOut");
            entity.HasOne(e => e.Conversation).WithMany(c => c.Messages).HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAssistantConfig>(entity =>
        {
            entity.ToTable("user_assistant_config");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.UserId).IsUnique();
            entity.Property(e => e.AssistantName).HasMaxLength(100).IsRequired();
            entity.Property(e => e.AvatarId).HasMaxLength(50).IsRequired();
            entity.Property(e => e.ColorHex).HasMaxLength(10).IsRequired();
            entity.Property(e => e.PersonalityPreset).HasMaxLength(20).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.FirmAutoTranscript).HasColumnName("firm_auto_transcript").HasDefaultValue(false);
            entity.Property(e => e.FirmAutoSummary).HasColumnName("firm_auto_summary").HasDefaultValue(false);
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(100);
            entity.Property(e => e.Responsibilities).HasColumnName("responsibilities");
            entity.Property(e => e.CommunicationStyle).HasColumnName("communication_style").HasMaxLength(20);
            entity.Property(e => e.ResponseFormat).HasColumnName("response_format").HasMaxLength(30);
            entity.Property(e => e.ShowCitations).HasColumnName("show_citations");
            entity.Property(e => e.UseCasesJson).HasColumnName("use_cases_json");
            entity.Property(e => e.AdditionalContext).HasColumnName("additional_context");
            entity.Property(e => e.PreferredName).HasColumnName("preferred_name").HasMaxLength(100);
            entity.HasOne(e => e.User).WithOne(u => u.AssistantConfig).HasForeignKey<UserAssistantConfig>(e => e.UserId);
        });

        modelBuilder.Entity<BriefingHistory>(entity =>
        {
            entity.ToTable("briefing_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.UserId, e.BriefingDate });
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.CalendarEventsJson).HasColumnName("calendar_events");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<UserBriefingSchedule>(entity =>
        {
            entity.ToTable("user_briefing_schedule");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.DeliveryTimeUtc).IsRequired();
            entity.HasOne(e => e.User).WithOne(u => u.BriefingSchedule).HasForeignKey<UserBriefingSchedule>(e => e.UserId);
        });

        modelBuilder.Entity<UserMicrosoftToken>(entity =>
        {
            entity.ToTable("user_microsoft_tokens");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.AccessToken).IsRequired();
            entity.Property(e => e.RefreshToken).IsRequired();
            entity.Property(e => e.MicrosoftEmail).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithOne().HasForeignKey<UserMicrosoftToken>(e => e.UserId);
        });

        modelBuilder.Entity<UserDevOpsConnection>(entity =>
        {
            entity.ToTable("user_devops_connections");
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.UserId).HasColumnName("user_id").ValueGeneratedNever();
            entity.Property(e => e.OrgUrl).HasColumnName("org_url").HasMaxLength(512).IsRequired();
            entity.Property(e => e.PatEncrypted).HasColumnName("pat_encrypted").HasColumnType("LONGTEXT").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithOne().HasForeignKey<UserDevOpsConnection>(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GraphSubscription>(entity =>
        {
            entity.ToTable("graph_subscriptions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.SubscriptionId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ClientState).HasMaxLength(255).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<EmailAlert>(entity =>
        {
            entity.ToTable("email_alerts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Dismissed);
            entity.Property(e => e.MessageId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.SenderEmail).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Importance).HasMaxLength(10).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<EmailLog>(entity =>
        {
            entity.ToTable("email_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.MessageId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.SenderEmail).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Importance).HasMaxLength(10).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("task_cache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.UserId, e.DueDate });
            entity.HasIndex(e => new { e.UserId, e.TaskId }).IsUnique();
            entity.Property(e => e.TaskId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(500).IsRequired();
            entity.Property(e => e.PlanTitle).HasMaxLength(255);
            entity.Property(e => e.BucketName).HasMaxLength(255);
            entity.Property(e => e.LastFetchedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<CalendarEvent>(entity =>
        {
            entity.ToTable("calendar_cache");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.UserId, e.StartTime });
            entity.HasIndex(e => new { e.UserId, e.EventId }).IsUnique();
            entity.Property(e => e.EventId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.Subject).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Location).HasMaxLength(500);
            entity.Property(e => e.Category).HasMaxLength(100);
            entity.Property(e => e.LastFetchedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.OrganizerEmail).HasColumnName("organizer_email").HasMaxLength(255);
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<PostMeetingNote>(entity =>
        {
            entity.ToTable("post_meeting_notes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.HasIndex(e => new { e.UserId, e.EventId });
            entity.HasIndex(e => e.UserId);
            entity.Property(e => e.EventId).HasMaxLength(255).IsRequired();
            entity.Property(e => e.EventSubject).HasMaxLength(500).IsRequired();
            entity.Property(e => e.Notes).IsRequired();
            entity.Property(e => e.Summary).IsRequired(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId);
        });

        // KbEntry
        modelBuilder.Entity<KbEntry>(e => {
            e.ToTable("kb_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Content).HasColumnType("TEXT").IsRequired();
            e.Property(x => x.Tags).HasMaxLength(500);
            e.Property(x => x.SourceUrl).HasMaxLength(1000);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => new { x.UserId, x.Tier });
        });

        // KbTeam
        modelBuilder.Entity<KbTeam>(e => {
            e.ToTable("kb_teams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.HasMany(x => x.Members).WithOne(m => m.Team).HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Entries).WithOne().HasForeignKey(entry => entry.TeamId).OnDelete(DeleteBehavior.SetNull);
        });

        // KbTeamMember
        modelBuilder.Entity<KbTeamMember>(e => {
            e.ToTable("kb_team_members");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<McpServer>(entity =>
        {
            entity.ToTable("mcp_servers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Slug).HasMaxLength(50).IsRequired();
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.Property(e => e.TransportType).HasMaxLength(20).HasDefaultValue("http").HasColumnName("transport_type");
            entity.Property(e => e.EndpointUrl).HasMaxLength(500).HasColumnName("endpoint_url");
            entity.Property(e => e.AuthType).HasMaxLength(20).HasDefaultValue("none").HasColumnName("auth_type");
            entity.Property(e => e.AuthConfigJson).HasColumnName("auth_config").HasColumnType("JSON");
            entity.Property(e => e.ToolManifestJson).HasColumnName("tool_manifest").HasColumnType("JSON");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.RequiresUserAuth).HasColumnName("requires_user_auth");
            entity.Property(e => e.SystemApiKey).HasColumnType("TEXT").HasColumnName("system_api_key");
            entity.Property(e => e.OAuthClientSecret).HasColumnType("TEXT").HasColumnName("oauth_client_secret");
            entity.Property(e => e.RateLimitPerMinute).HasColumnName("rate_limit_per_minute").HasDefaultValue(30);
            entity.Property(e => e.IconUrl).HasMaxLength(500).HasColumnName("icon_url");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)").HasColumnName("updated_at");
            entity.Ignore(e => e.AuthConfig);
            entity.Ignore(e => e.Tools);
        });

        modelBuilder.Entity<UserMcpToken>(entity =>
        {
            entity.ToTable("user_mcp_tokens");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ServerId).HasColumnName("server_id");
            entity.HasIndex(e => new { e.UserId, e.ServerId }).IsUnique();
            entity.Property(e => e.AccessToken).HasColumnType("TEXT").IsRequired().HasColumnName("access_token");
            entity.Property(e => e.RefreshToken).HasColumnType("TEXT").HasColumnName("refresh_token");
            entity.Property(e => e.TokenExpiresAt).HasColumnName("token_expires_at");
            entity.Property(e => e.Scopes).HasMaxLength(1000);
            entity.Property(e => e.ExternalUserId).HasMaxLength(255).HasColumnName("external_user_id");
            entity.Property(e => e.ExternalEmail).HasMaxLength(255).HasColumnName("external_email");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)").HasColumnName("updated_at");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Server).WithMany(s => s.UserTokens).HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.Cascade);
            entity.Ignore(e => e.IsExpired);
        });

        modelBuilder.Entity<ConversationMcpServer>(entity =>
        {
            entity.ToTable("conversation_mcp_servers");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.ServerId).HasColumnName("server_id");
            entity.HasIndex(e => new { e.ConversationId, e.ServerId }).IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)").HasColumnName("created_at");
            entity.HasOne(e => e.Conversation).WithMany().HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Server).WithMany().HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<McpToolCallLog>(entity =>
        {
            entity.ToTable("mcp_tool_call_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.ServerId).HasColumnName("server_id");
            entity.Property(e => e.ToolName).HasMaxLength(100).IsRequired().HasColumnName("tool_name");
            entity.Property(e => e.InputJson).HasColumnType("LONGTEXT").HasColumnName("input_json");
            entity.Property(e => e.OutputJson).HasColumnType("LONGTEXT").HasColumnName("output_json");
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.LatencyMs).HasColumnName("latency_ms");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)").HasColumnName("created_at");
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.ConversationId);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<McpServer>().WithMany().HasForeignKey(e => e.ServerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConversationTeamKb>(entity =>
        {
            entity.ToTable("conversation_team_kbs");
            entity.HasKey(e => new { e.ConversationId, e.TeamId });
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id");
            entity.Property(e => e.TeamId).HasColumnName("team_id");
            entity.Property(e => e.EnabledAt).HasColumnName("enabled_at");
            entity.HasOne(e => e.Conversation).WithMany(c => c.TeamKbs).HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Team).WithMany().HasForeignKey(e => e.TeamId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserModulePermission>(entity =>
        {
            entity.ToTable("user_module_permissions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Module).HasColumnName("module").HasMaxLength(50);
            entity.Property(e => e.Permission).HasColumnName("permission").HasMaxLength(50);
            entity.Property(e => e.Granted).HasColumnName("granted").HasDefaultValue(true);
            entity.Property(e => e.GrantedAt).HasColumnName("granted_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.GrantedByUserId).HasColumnName("granted_by_user_id");
            entity.HasIndex(e => new { e.UserId, e.Module, e.Permission }).IsUnique();
        });

        modelBuilder.Entity<ChatAttachment>(entity =>
        {
            entity.ToTable("chat_attachments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Filename).HasMaxLength(255).IsRequired();
            entity.Property(e => e.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(e => e.S3Key).HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.Conversation).WithMany().HasForeignKey(e => e.ConversationId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.MessageId);
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.LastActiveAt).HasColumnName("last_active_at");
            entity.Property(e => e.EndedAt).HasColumnName("ended_at");
            entity.Property(e => e.TaskArn).HasColumnName("task_arn").HasMaxLength(500);
            entity.Property(e => e.PrivateIp).HasColumnName("private_ip").HasMaxLength(45);
            entity.Property(e => e.FargateStatus).HasColumnName("fargate_status").HasMaxLength(20);
            entity.Property(e => e.FargateSessionId).HasColumnName("fargate_session_id").HasMaxLength(200);
            entity.Property(e => e.TaskDefinitionRevision).HasColumnName("task_definition_revision").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.HarnessVersion).HasColumnName("harness_version").HasMaxLength(100);
            entity.HasIndex(e => e.UserId).HasDatabaseName("ix_user_sessions_user_id");
            entity.HasIndex(e => e.LastActiveAt).HasDatabaseName("ix_user_sessions_last_active_at");
        });

        modelBuilder.Entity<ScheduledTask>(entity =>
        {
            entity.ToTable("scheduled_tasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Name).HasMaxLength(200).IsRequired();
            entity.Property(e => e.Prompt).HasColumnType("TEXT").IsRequired();
            entity.Property(e => e.ScheduleType).HasColumnType("ENUM('recurring','on_demand')").IsRequired();
            entity.Property(e => e.CronExpression).HasMaxLength(100);
            entity.Property(e => e.LastRunStatus).HasColumnType("ENUM('success','failed','cancelled')");
            entity.Property(e => e.FailureCount).HasDefaultValue(0);
            entity.Property(e => e.AlertOnCompletion).HasDefaultValue(false);
            entity.Property(e => e.AlertOnFailure).HasDefaultValue(true);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.TaskMode).HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId).IsRequired(false).OnDelete(DeleteBehavior.SetNull);
            entity.HasMany(e => e.Runs).WithOne(r => r.Task).HasForeignKey(r => r.TaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.NextRunAt);
            entity.HasIndex(e => new { e.UserId, e.IsActive });
        });

        modelBuilder.Entity<ScheduledTaskRun>(entity =>
        {
            entity.ToTable("scheduled_task_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Status).HasColumnType("ENUM('success','failed','cancelled')").IsRequired();
            entity.Property(e => e.ResultSummary).HasMaxLength(500);
            entity.Property(e => e.ArtifactBlobPath).HasMaxLength(500);
            entity.Property(e => e.SandboxId).HasMaxLength(200);
            entity.Property(e => e.Error).HasColumnType("TEXT");
            entity.HasOne(e => e.Task).WithMany(t => t.Runs).HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.TaskId);
            entity.HasIndex(e => e.StartedAt);
        });

        modelBuilder.Entity<MemoryTopic>(entity =>
        {
            entity.ToTable("memory_topics");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Slug).HasMaxLength(100).IsRequired();
            entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnType("DATETIME(6)");
            entity.Property(e => e.UpdatedAt).HasColumnType("DATETIME(6)");
            entity.HasIndex(e => new { e.UserId, e.Slug }).IsUnique();
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkspaceFolder>(entity =>
        {
            entity.ToTable("workspace_folders");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(64).IsRequired();
            entity.Property(e => e.S3Prefix).HasColumnName("s3_prefix").HasMaxLength(500).IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("DATETIME(6)").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasColumnType("DATETIME(6)").IsRequired(false);
            entity.Property(e => e.ParentId).HasColumnName("parent_id").IsRequired(false);
            entity.HasOne<WorkspaceFolder>()
                  .WithMany()
                  .HasForeignKey(e => e.ParentId)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_wf_user_id");
        });

        modelBuilder.Entity<WorkspaceUpload>(entity =>
        {
            entity.ToTable("user_workspace_uploads");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.FolderId).HasColumnName("folder_id");
            entity.Property(e => e.Filename).HasColumnName("filename").HasMaxLength(500).IsRequired();
            entity.Property(e => e.MimeType).HasColumnName("mime_type").HasMaxLength(200).IsRequired();
            entity.Property(e => e.S3Key).HasColumnName("s3_key").HasMaxLength(1000).IsRequired();
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("DATETIME(6)").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.CurrentVersion).HasColumnName("current_version").HasDefaultValue(1);
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).IsRequired(false);
            entity.Property(e => e.ConversationId).HasColumnName("conversation_id").IsRequired(false);
            entity.Property(e => e.TurnIndex).HasColumnName("turn_index").IsRequired(false);
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_uwup_user_id");
            entity.HasIndex(e => e.FolderId).HasDatabaseName("idx_uwup_folder_id");
            entity.HasOne<WorkspaceFolder>()
                  .WithMany()
                  .HasForeignKey(u => u.FolderId)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);
        });

        modelBuilder.Entity<WorkspaceFileVersion>(entity =>
        {
            entity.ToTable("workspace_file_versions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.VersionNumber).HasColumnName("version_number").HasDefaultValue(1);
            entity.Property(e => e.S3Key).HasColumnName("s3_key").HasMaxLength(1000).IsRequired();
            entity.Property(e => e.SizeBytes).HasColumnName("size_bytes");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("DATETIME(6)");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasMaxLength(20).HasDefaultValue("user");
            entity.HasIndex(e => e.FileId).HasDatabaseName("idx_wfv_file_id");
        });

        modelBuilder.Entity<FeedbackSubmission>(entity =>
        {
            entity.ToTable("feedback_submissions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(36).HasColumnType("varchar(36)").HasColumnName("id");
            entity.Property(e => e.UserId).HasMaxLength(255).HasColumnType("varchar(255)").HasColumnName("user_id").IsRequired();
            entity.Property(e => e.Type).HasMaxLength(50).HasColumnType("varchar(50)").HasColumnName("type").IsRequired();
            entity.Property(e => e.Description).HasColumnType("longtext").HasColumnName("description").IsRequired();
            entity.Property(e => e.PageUrl).HasMaxLength(500).HasColumnType("varchar(500)").HasColumnName("page_url");
            entity.Property(e => e.ScreenshotS3Key).HasMaxLength(500).HasColumnType("varchar(500)").HasColumnName("screenshot_s3_key");
            entity.Property(e => e.Status).HasMaxLength(50).HasColumnType("varchar(50)").HasColumnName("status").HasDefaultValue("pending").IsRequired();
            entity.Property(e => e.AdoWiId).HasColumnType("int").HasColumnName("ado_wi_id");
            entity.Property(e => e.TriageResult).HasColumnType("longtext").HasColumnName("triage_result");
            entity.Property(e => e.CreatedAt).HasColumnType("DATETIME(6)").HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.Property(e => e.TriagedAt).HasColumnType("DATETIME(6)").HasColumnName("triaged_at");
            entity.HasIndex(e => e.UserId).HasDatabaseName("idx_feedback_user_id");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_feedback_status");
        });

    }
}
