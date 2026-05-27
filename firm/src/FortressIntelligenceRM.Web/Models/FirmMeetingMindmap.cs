using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

public class FirmMeetingMindmap
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string MindmapJson { get; set; } = "";
    [MaxLength(100)]
    public string? ModelUsed { get; set; }
    public DateTime CreatedAt { get; set; }
    public FirmMeeting? Meeting { get; set; }
}
