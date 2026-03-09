namespace FortressAI.Shared.Models;

public class UserBriefingSchedule
{
    public Guid UserId { get; set; }
    public TimeOnly DeliveryTimeUtc { get; set; } = new TimeOnly(13, 0);
    public bool EmailDigestEnabled { get; set; } = false;
    public AppUser? User { get; set; }
}
