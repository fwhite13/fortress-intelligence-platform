using Microsoft.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Data;

// NOTE: FIRM does NOT use EF migrations — all schema is managed by DatabaseInitializationService (raw SQL).
// DataProtection keys live in SharedKeyRingDbContext (fred_dev), not here.
public class FirmDbContext : DbContext
{
    public FirmDbContext(DbContextOptions<FirmDbContext> options) : base(options) { }

    public DbSet<FirmUser> Users => Set<FirmUser>();
    public DbSet<FirmMeeting> Meetings => Set<FirmMeeting>();
    public DbSet<FirmMeetingParticipant> Participants => Set<FirmMeetingParticipant>();
    public DbSet<FirmMeetingTranscript> Transcripts => Set<FirmMeetingTranscript>();
    public DbSet<FirmMeetingSummary> Summaries => Set<FirmMeetingSummary>();
    public DbSet<FirmMeetingKbPush> FirmMeetingKbPushes => Set<FirmMeetingKbPush>();
    public DbSet<FirmOrgContext> OrgContexts => Set<FirmOrgContext>();
    public DbSet<FirmUserWiki> UserWikis => Set<FirmUserWiki>();
    public DbSet<FirmMeetingMindmap> Mindmaps => Set<FirmMeetingMindmap>();
    public DbSet<FirmZoomOAuth> ZoomOAuthTokens => Set<FirmZoomOAuth>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<FirmUser>(entity =>
        {
            entity.ToTable("firm_users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.HasIndex(e => e.EntraOid).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.EntraOid).HasColumnName("entra_oid").HasMaxLength(128).IsRequired();
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(256).IsRequired();
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.FaitUserId).HasColumnName("fait_user_id").HasMaxLength(36);
            entity.Property(e => e.IsAdmin).HasColumnName("is_admin").HasDefaultValue(false);
            entity.Property(e => e.ExpoPushToken).HasColumnName("expo_push_token").HasMaxLength(200);
            entity.Property(e => e.AutoAddCalendarMeetings).HasColumnName("auto_add_calendar_meetings").HasDefaultValue(false);
            entity.Property(e => e.AutoEmailSummary).HasColumnName("auto_email_summary").HasDefaultValue(false);
        });

        modelBuilder.Entity<FirmMeeting>(entity =>
        {
            entity.ToTable("firm_meetings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(500);
            entity.Property(e => e.Platform).HasColumnName("platform").HasMaxLength(20).HasDefaultValue("teams");
            entity.Property(e => e.MeetingUrl).HasColumnName("meeting_url").HasMaxLength(2000);
            entity.Property(e => e.Status)
                .HasColumnName("status")
                .HasConversion<string>()
                .HasDefaultValue(MeetingStatus.Joining)
                // ADO#17 diagnostic, 2026-09-09: MeetingStatus.Scheduled == 0, the CLR default for
                // the enum, so EF Core's "omit column if it equals the sentinel" INSERT optimization
                // was treating Scheduled as if it were never explicitly set, letting the DB column
                // default (Joining) win. Declaring Joining as the actual sentinel tells EF Core to
                // always include Status in the INSERT explicitly. Belt-and-suspenders alongside the
                // Joining->Scheduled two-step workaround in CalendarAutoSyncService.InsertScheduledMeetingAsync
                // — keep that workaround in place until this is verified working in production.
                .HasSentinel(MeetingStatus.Joining);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
            entity.Property(e => e.ScheduledAt).HasColumnName("scheduled_at");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.EndedAt).HasColumnName("ended_at");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(e => e.AudioS3Key).HasColumnName("audio_s3_key").HasMaxLength(1000);
            entity.Property(e => e.TranscriptS3Key).HasColumnName("transcript_s3_key").HasMaxLength(1000);
            entity.Property(e => e.BotTaskArn).HasColumnName("bot_task_arn").HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TranscriptKbPushed).HasColumnName("transcript_kb_pushed").HasDefaultValue(false);
            entity.Property(e => e.SummaryKbPushed).HasColumnName("summary_kb_pushed").HasDefaultValue(false);
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).HasDefaultValue("teams");
            entity.Property(e => e.LastFailureReason).HasColumnName("last_failure_reason").HasMaxLength(64);
            entity.HasOne(e => e.Mindmap)
                .WithOne(mm => mm.Meeting)
                .HasForeignKey<FirmMeetingMindmap>(mm => mm.MeetingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fmm_meeting_id");
            entity.Property(e => e.StartDatetime).HasColumnName("start_datetime");
            entity.Property(e => e.CalendarEventId).HasColumnName("calendar_event_id").HasMaxLength(500);
            // graph_meeting_id existed as a dead column (never read/written); repurposed to store
            // Graph's iCalUId, the stable reconciliation anchor (Issue 5b).
            entity.Property(e => e.GraphMeetingId).HasColumnName("graph_meeting_id").HasMaxLength(500);
            entity.Property(e => e.Mode).HasColumnName("mode").HasMaxLength(2);
            entity.Property(e => e.CreatorEntraOid).HasColumnName("creator_entra_oid").HasMaxLength(128);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany(u => u.Meetings)
                .HasForeignKey(e => e.CreatedBy)
                .HasConstraintName("fk_fm_user");
            entity.HasIndex(e => e.CreatedBy).HasDatabaseName("idx_fm_created_by");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_fm_status");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_fm_created_at");
            // Issue 5b: close the race-condition window where two concurrent PollCoreAsync dedup
            // misses can both insert a row for the same Graph event. NULL calendar_event_id
            // (manually-added meetings) is excluded via the filter so those never collide.
            // NOTE: FIRM does not use EF migrations (see class-level comment) — this index is
            // declared here so EF's model matches the DB, but the actual DB-level index is created
            // by the idempotent raw SQL in DatabaseInitializationService, not by `dotnet ef migrations`.
            entity.HasIndex(e => new { e.CreatedBy, e.CalendarEventId })
                .IsUnique()
                .HasDatabaseName("uk_fm_created_by_calendar_event_id")
                .HasFilter("`calendar_event_id` IS NOT NULL");

            // WI #7033 — multi-user meeting dedup (primary/subscriber model).
            entity.Property(e => e.IsPrimaryRecorder).HasColumnName("is_primary_recorder").HasDefaultValue(true);
            entity.Property(e => e.PrimaryMeetingId).HasColumnName("primary_meeting_id");
            entity.Property(e => e.NormalizedMeetingUrl).HasColumnName("normalized_meeting_url").HasMaxLength(2000);
            entity.HasOne(e => e.PrimaryMeeting)
                .WithMany()
                .HasForeignKey(e => e.PrimaryMeetingId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_fm_primary_meeting");
            entity.HasIndex(e => e.PrimaryMeetingId).HasDatabaseName("idx_fm_primary_meeting_id");
            // DB-level race guard (see NormalizedMeetingUrl doc comment on FirmMeeting): only primaries
            // ever populate NormalizedMeetingUrl, so this unique index only ever constrains primaries.
            // NOTE: FIRM does not use EF migrations (see class-level comment) — the actual DB-level
            // index is created by the idempotent raw SQL in DatabaseInitializationService.
            entity.HasIndex(e => new { e.NormalizedMeetingUrl, e.StartDatetime })
                .IsUnique()
                .HasDatabaseName("uk_fm_normalized_url_start")
                .HasFilter("`normalized_meeting_url` IS NOT NULL");
        });

        modelBuilder.Entity<FirmMeetingParticipant>(entity =>
        {
            entity.ToTable("firm_meeting_participants");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(255).IsRequired();
            entity.Property(e => e.SpeakerLabel).HasColumnName("speaker_label").HasMaxLength(20);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255);
            entity.Property(e => e.JoinedAt).HasColumnName("joined_at");
            entity.HasOne(e => e.Meeting)
                .WithMany(m => m.Participants)
                .HasForeignKey(e => e.MeetingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fmp_meeting");
            entity.HasIndex(e => e.MeetingId).HasDatabaseName("idx_fmp_meeting");
        });

        modelBuilder.Entity<FirmMeetingTranscript>(entity =>
        {
            entity.ToTable("firm_meeting_transcripts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.Property(e => e.SpeakerLabel).HasColumnName("speaker_label").HasMaxLength(20);
            entity.Property(e => e.SpeakerName).HasColumnName("speaker_name").HasMaxLength(255);
            entity.Property(e => e.Text).HasColumnName("text").IsRequired();
            entity.Property(e => e.StartTimeMs).HasColumnName("start_time_ms");
            entity.Property(e => e.EndTimeMs).HasColumnName("end_time_ms");
            entity.Property(e => e.IsPartial).HasColumnName("is_partial").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Meeting)
                .WithMany(m => m.Transcripts)
                .HasForeignKey(e => e.MeetingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fmt_meeting");
            entity.HasIndex(e => e.MeetingId).HasDatabaseName("idx_fmt_meeting");
        });

        modelBuilder.Entity<FirmMeetingSummary>(entity =>
        {
            entity.ToTable("firm_meeting_summaries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.HasIndex(e => e.MeetingId).IsUnique();
            entity.Property(e => e.SummaryText).HasColumnName("summary_text");
            entity.Property(e => e.ActionItemsJson).HasColumnName("action_items_json");
            entity.Property(e => e.KeyDecisionsJson).HasColumnName("key_decisions_json");
            entity.Property(e => e.FollowUpsJson).HasColumnName("follow_ups_json");
            entity.Property(e => e.OpenQuestionsJson).HasColumnName("open_questions_json");
            entity.Property(e => e.ModelUsed).HasColumnName("model_used").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne(e => e.Meeting)
                .WithOne(m => m.Summary)
                .HasForeignKey<FirmMeetingSummary>(e => e.MeetingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fms_meeting");
        });

        modelBuilder.Entity<FirmMeetingKbPush>(entity =>
        {
            entity.ToTable("firm_meeting_kb_pushes");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.Property(e => e.DocType).HasColumnName("doc_type").HasMaxLength(20).IsRequired();
            entity.Property(e => e.KbScope).HasColumnName("kb_scope").HasMaxLength(50).IsRequired();
            entity.Property(e => e.KbId).HasColumnName("kb_id").HasMaxLength(100);
            entity.Property(e => e.PushedAt).HasColumnName("pushed_at").HasDefaultValueSql("CURRENT_TIMESTAMP(6)");
            entity.HasOne(e => e.Meeting)
                .WithMany()
                .HasForeignKey(e => e.MeetingId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fmkp_meeting");
            entity.HasIndex(e => new { e.MeetingId, e.DocType, e.KbScope }).HasDatabaseName("idx_fmkp_lookup");
        });

        modelBuilder.Entity<FirmOrgContext>(entity =>
        {
            entity.ToTable("firm_org_context");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.EntraTenantId).HasColumnName("entra_tenant_id").HasMaxLength(36).IsRequired();
            entity.HasIndex(e => e.EntraTenantId).IsUnique().HasDatabaseName("uk_tenant");
            entity.Property(e => e.WikiContent).HasColumnName("wiki_content");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256);
        });

        modelBuilder.Entity<FirmMeetingMindmap>(entity =>
        {
            entity.ToTable("firm_meeting_mindmaps");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.MeetingId).HasColumnName("meeting_id");
            entity.HasIndex(e => e.MeetingId).IsUnique().HasDatabaseName("uk_fmm_meeting");
            entity.Property(e => e.MindmapJson).HasColumnName("mindmap_json").IsRequired();
            entity.Property(e => e.ModelUsed).HasColumnName("model_used").HasMaxLength(100);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<FirmUserWiki>(entity =>
        {
            entity.ToTable("firm_user_wiki");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.EntraOid).HasColumnName("entra_oid").HasMaxLength(128).IsRequired();
            entity.Property(e => e.EntraTenantId).HasColumnName("entra_tenant_id").HasMaxLength(36).IsRequired();
            entity.Property(e => e.WikiContent).HasColumnName("wiki_content");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedBy).HasColumnName("updated_by").HasMaxLength(256);
            entity.HasIndex(e => new { e.EntraOid, e.EntraTenantId }).IsUnique().HasDatabaseName("idx_user_wiki_user");
        });

        modelBuilder.Entity<FirmZoomOAuth>(entity =>
        {
            entity.ToTable("firm_zoom_oauth");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
            entity.HasIndex(e => e.UserId).IsUnique().HasDatabaseName("uk_fzo_user");
            entity.Property(e => e.ZoomUserId).HasColumnName("zoom_user_id").HasMaxLength(128).IsRequired();
            entity.Property(e => e.ZoomEmail).HasColumnName("zoom_email").HasMaxLength(256);
            entity.Property(e => e.AccessToken).HasColumnName("access_token").IsRequired();
            entity.Property(e => e.RefreshToken).HasColumnName("refresh_token").IsRequired();
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasOne<FirmUser>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("fk_fzo_user");
        });

    }
}
