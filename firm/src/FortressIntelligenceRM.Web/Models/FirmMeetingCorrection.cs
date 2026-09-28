namespace FortressIntelligenceRM.Web.Models;

/// <summary>WI #7299 — a user's plain-English speaker-attribution correction that drove a re-summarization.</summary>
public class FirmMeetingCorrection
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string Correction { get; set; } = "";
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
