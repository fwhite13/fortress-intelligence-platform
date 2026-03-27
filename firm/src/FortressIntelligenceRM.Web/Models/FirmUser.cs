using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

public class FirmUser
{
    public string Id { get; set; } = "";
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
    public ICollection<FirmMeeting> Meetings { get; set; } = new List<FirmMeeting>();
}
