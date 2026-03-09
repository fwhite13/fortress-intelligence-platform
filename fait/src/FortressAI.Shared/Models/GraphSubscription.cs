using System.ComponentModel.DataAnnotations;

namespace FortressAI.Shared.Models;

public class GraphSubscription
{
    public int Id { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(255)]
    public string SubscriptionId { get; set; } = "";

    [MaxLength(255)]
    public string ClientState { get; set; } = "";

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
}
