using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

public class FirmMeetingSummary
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string? SummaryText { get; set; }
    public string? ActionItemsJson { get; set; }
    public string? KeyDecisionsJson { get; set; }
    public string? FollowUpsJson { get; set; }
    [MaxLength(100)]
    public string? ModelUsed { get; set; }
    public DateTime CreatedAt { get; set; }
    public FirmMeeting? Meeting { get; set; }
}
