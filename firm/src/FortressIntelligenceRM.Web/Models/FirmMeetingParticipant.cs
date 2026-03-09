using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

public class FirmMeetingParticipant
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    [MaxLength(255)]
    public string DisplayName { get; set; } = "";
    [MaxLength(20)]
    public string? SpeakerLabel { get; set; }
    [MaxLength(255)]
    public string? Email { get; set; }
    public DateTime? JoinedAt { get; set; }
    public FirmMeeting? Meeting { get; set; }
}
