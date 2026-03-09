using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressIntelligenceRM.Web.Models;

public class FirmMeetingSummary
{
    public long Id { get; set; }
    public long MeetingId { get; set; }
    public string? SummaryText { get; set; }
    [Column(TypeName = "json")]
    public string? ActionItemsJson { get; set; }
    [Column(TypeName = "json")]
    public string? KeyDecisionsJson { get; set; }
    [Column(TypeName = "json")]
    public string? FollowUpsJson { get; set; }
    [MaxLength(100)]
    public string? ModelUsed { get; set; }
    public DateTime CreatedAt { get; set; }
    public FirmMeeting? Meeting { get; set; }
}
