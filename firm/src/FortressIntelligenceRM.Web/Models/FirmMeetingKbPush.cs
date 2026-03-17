namespace FortressIntelligenceRM.Web.Models;

/// <summary>Tracks which meeting documents have been pushed to which KB.</summary>
public class FirmMeetingKbPush
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    /// <summary>"transcript" or "summary"</summary>
    public string DocType { get; set; } = "";
    /// <summary>"personal" or "team"</summary>
    public string KbScope { get; set; } = "";
    public string? KbId { get; set; }
    public DateTime PushedAt { get; set; } = DateTime.UtcNow;
    public FirmMeeting? Meeting { get; set; }
}
