using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using FortressIntelligenceRM.Web.Models;

namespace FortressIntelligenceRM.Web.Data;

public class FirmDbContext : DbContext, IDataProtectionKeyContext
{
    public FirmDbContext(DbContextOptions<FirmDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<FirmUser> Users => Set<FirmUser>();
    public DbSet<FirmMeeting> Meetings => Set<FirmMeeting>();
    public DbSet<FirmMeetingParticipant> Participants => Set<FirmMeetingParticipant>();
    public DbSet<FirmMeetingTranscript> Transcripts => Set<FirmMeetingTranscript>();
    public DbSet<FirmMeetingSummary> Summaries => Set<FirmMeetingSummary>();
    public DbSet<FirmMeetingKbPush> FirmMeetingKbPushes => Set<FirmMeetingKbPush>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Map to FAIT DataProtectionKeys table — shared key ring for cross-app cookie reading
        modelBuilder.Entity<DataProtectionKey>().ToTable("DataProtectionKeys");

        modelBuilder.Entity<FirmUser>(entity =>
        {
            entity.ToTable("firm_users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnType("char(36)").HasConversion<string>();
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
                .HasDefaultValue(MeetingStatus.Joining);
            entity.Property(e => e.ErrorMessage).HasColumnName("error_message").HasColumnType("TEXT");
            entity.Property(e => e.ScheduledAt).HasColumnName("scheduled_at");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.EndedAt).HasColumnName("ended_at");
            entity.Property(e => e.DurationSeconds).HasColumnName("duration_seconds");
            entity.Property(e => e.AudioS3Key).HasColumnName("audio_s3_key").HasMaxLength(1000);
            entity.Property(e => e.TranscriptS3Key).HasColumnName("transcript_s3_key").HasMaxLength(1000);
            entity.Property(e => e.BotTaskArn).HasColumnName("bot_task_arn").HasMaxLength(500);
            entity.Property(e => e.CreatedBy).HasColumnName("created_by").HasColumnType("char(36)").HasConversion<string>();
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.TranscriptKbPushed).HasColumnName("transcript_kb_pushed").HasDefaultValue(false);
            entity.Property(e => e.SummaryKbPushed).HasColumnName("summary_kb_pushed").HasDefaultValue(false);
            entity.Property(e => e.StartDatetime).HasColumnName("start_datetime");
            entity.Property(e => e.CalendarEventId).HasColumnName("calendar_event_id").HasMaxLength(500);
            entity.HasOne(e => e.CreatedByUser)
                .WithMany(u => u.Meetings)
                .HasForeignKey(e => e.CreatedBy)
                .HasConstraintName("fk_fm_user");
            entity.HasIndex(e => e.CreatedBy).HasDatabaseName("idx_fm_created_by");
            entity.HasIndex(e => e.Status).HasDatabaseName("idx_fm_status");
            entity.HasIndex(e => e.CreatedAt).HasDatabaseName("idx_fm_created_at");
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
            entity.Property(e => e.Text).HasColumnName("text").HasColumnType("TEXT").IsRequired();
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
            entity.Property(e => e.SummaryText).HasColumnName("summary_text").HasColumnType("TEXT");
            entity.Property(e => e.ActionItemsJson).HasColumnName("action_items_json").HasColumnType("JSON");
            entity.Property(e => e.KeyDecisionsJson).HasColumnName("key_decisions_json").HasColumnType("JSON");
            entity.Property(e => e.FollowUpsJson).HasColumnName("follow_ups_json").HasColumnType("JSON");
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
    }
}
