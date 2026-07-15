using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

public class FirmUser
{
    public Guid Id { get; set; }
    [MaxLength(128)]
    public string EntraOid { get; set; } = "";
    [MaxLength(256)]
    public string Email { get; set; } = "";
    [MaxLength(255)]
    public string DisplayName { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? FaitUserId { get; set; }
    public bool IsAdmin { get; set; }
    [MaxLength(200)]
    public string? ExpoPushToken { get; set; }
    public bool AutoAddCalendarMeetings { get; set; }
    public bool AutoEmailSummary { get; set; }
    public ICollection<FirmMeeting> Meetings { get; set; } = new List<FirmMeeting>();
}
