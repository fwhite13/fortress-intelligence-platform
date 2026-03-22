namespace FamOs.Web.Data.Entities;

public class CarrierNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AccountId { get; set; }
    public Guid QuoteId { get; set; }
    public int TenantId { get; set; }
    public string NoteText { get; set; } = "";
    public Guid CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
