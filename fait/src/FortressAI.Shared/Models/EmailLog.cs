using System.ComponentModel.DataAnnotations;

namespace FortressAI.Shared.Models;

public class EmailLog
{
    public int Id { get; set; }
    public Guid UserId { get; set; }

    [MaxLength(255)]
    public string MessageId { get; set; } = "";

    [MaxLength(255)]
    public string SenderEmail { get; set; } = "";

    public string Subject { get; set; } = "";

    [MaxLength(10)]
    public string Importance { get; set; } = "LOW";

    public DateTime ReceivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AppUser? User { get; set; }
}
