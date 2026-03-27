using System.ComponentModel.DataAnnotations;

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
    public string? CalendarEventId { get; set; }    // Graph calendar event ID for sync
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? DurationSeconds { get; set; }
    [MaxLength(1000)]
    public string? AudioS3Key { get; set; }
    [MaxLength(1000)]
    public string? TranscriptS3Key { get; set; }
    [MaxLength(500)]
    public string? BotTaskArn { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public FirmUser? CreatedByUser { get; set; }
    public ICollection<FirmMeetingParticipant> Participants { get; set; } = new List<FirmMeetingParticipant>();
    public ICollection<FirmMeetingTranscript> Transcripts { get; set; } = new List<FirmMeetingTranscript>();
    public FirmMeetingSummary? Summary { get; set; }
    public bool TranscriptKbPushed { get; set; }
    public bool SummaryKbPushed { get; set; }
}
