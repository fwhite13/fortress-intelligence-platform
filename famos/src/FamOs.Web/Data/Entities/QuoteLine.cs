namespace FamOs.Web.Data.Entities;

public class QuoteLine
{
    public Guid     Id        { get; set; } = Guid.NewGuid();
    public Guid     QuoteId   { get; set; }
    public Guid     LobId     { get; set; }
    public string   Slug      { get; set; } = "";
    public decimal? Premium   { get; set; }
    public int      TenantId  { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Quote           Quote           { get; set; } = null!;
    public LineOfBusiness  LineOfBusiness  { get; set; } = null!;
}
