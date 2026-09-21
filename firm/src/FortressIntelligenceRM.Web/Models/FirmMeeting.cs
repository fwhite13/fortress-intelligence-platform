using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressIntelligenceRM.Web.Models;

public class FirmMeeting
{
    public long Id { get; set; }
    [MaxLength(500)]
    public string? Title { get; set; }
    [MaxLength(20)]
    public string Platform { get; set; } = "teams";
    [MaxLength(2000)]
    public string? MeetingUrl { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Joining;
    public string? ErrorMessage { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? StartDatetime { get; set; }    // When the meeting is scheduled to start
    [MaxLength(500)]
    public string? CalendarEventId { get; set; }    // Graph calendar event ID for sync — can drift across polls
    [MaxLength(500)]
    public string? GraphMeetingId { get; set; }      // Graph iCalUId — stable reconciliation anchor (Issue 5b)
    [MaxLength(2)]
    public string? Mode { get; set; }  // "A" or "B" — set at creation time
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }
    [MaxLength(1000)]
    public string? AudioS3Key { get; set; }
    [MaxLength(1000)]
    public string? TranscriptS3Key { get; set; }
    [MaxLength(500)]
    public string? BotTaskArn { get; set; }
    public Guid CreatedBy { get; set; }
    [MaxLength(128)]
    public string? CreatorEntraOid { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public FirmUser? CreatedByUser { get; set; }
    public ICollection<FirmMeetingParticipant> Participants { get; set; } = new List<FirmMeetingParticipant>();
    public ICollection<FirmMeetingTranscript> Transcripts { get; set; } = new List<FirmMeetingTranscript>();
    public FirmMeetingSummary? Summary { get; set; }
    public bool TranscriptKbPushed { get; set; }
    public bool SummaryKbPushed { get; set; }
    /// <summary>Recording source: "teams", "mobile", "vpbot". Defaults to "teams" for legacy rows.</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "teams";
    public FirmMeetingMindmap? Mindmap { get; set; }
    /// <summary>Machine-readable reason the last bot attempt failed, e.g. "lobby_timeout". Cleared once the bot successfully joins (status = recording).</summary>
    [MaxLength(64)]
    public string? LastFailureReason { get; set; }

    // WI #7033 — multi-user meeting dedup (primary/subscriber model).
    /// <summary>True if this row owns the bot slot for the underlying real-world meeting (or is a
    /// standalone single-user meeting). False for subscriber rows, which mirror a primary's output.</summary>
    public bool IsPrimaryRecorder { get; set; } = true;
    /// <summary>FK to the primary FirmMeeting row when this is a subscriber (IsPrimaryRecorder=false). Null for primaries.</summary>
    public long? PrimaryMeetingId { get; set; }
    public FirmMeeting? PrimaryMeeting { get; set; }
    /// <summary>Populated only on primary rows via CalendarService.NormalizeMeetingUrl(MeetingUrl) — the
    /// dedup key, alongside StartDatetime. Left NULL on subscriber rows so the DB-level unique index
    /// (NormalizedMeetingUrl, StartDatetime) only ever constrains primaries — MySQL unique indexes treat
    /// NULLs as distinct, the same technique uk_fm_created_by_calendar_event_id already relies on.</summary>
    [MaxLength(2000)]
    public string? NormalizedMeetingUrl { get; set; }
    /// <summary>WI #7297 — timestamped roster join/leave timeline from Teams bot, passed to firm-transcriber
    /// as soft LLM context for speaker labeling (handles conference rooms, multi-person single-account joins).
    /// Stored as JSON: [{"name":"...", "joinedAtMs":..., "leftAtMs":..., "possiblyMultiVoice":true}, ...]</summary>
    [Column("roster_timeline")]
    public string? RosterTimeline { get; set; }
}
