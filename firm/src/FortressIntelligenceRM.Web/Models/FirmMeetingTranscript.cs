using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

public class FirmMeetingTranscript
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    [MaxLength(20)]
    public string? SpeakerLabel { get; set; }
    [MaxLength(255)]
    public string? SpeakerName { get; set; }
    public string Text { get; set; } = "";
    public long? StartTimeMs { get; set; }
    public long? EndTimeMs { get; set; }
    public bool IsPartial { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public FirmMeeting? Meeting { get; set; }
}
