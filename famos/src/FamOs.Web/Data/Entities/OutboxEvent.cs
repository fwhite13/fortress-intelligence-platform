namespace FamOs.Web.Data.Entities;

public class OutboxEvent
{
    public Guid     Id              { get; set; } = Guid.NewGuid();
    public string   EventType       { get; set; } = "";
    public string   PayloadJson     { get; set; } = "";
    public DateTime OccurredAt      { get; set; } = DateTime.UtcNow;
    public bool     Processed       { get; set; } = false;
    public DateTime? ProcessedAt    { get; set; }
    public int      RetryCount      { get; set; } = 0;
    public string?  ErrorMessage    { get; set; }
}
