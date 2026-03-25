using FamOs.Web.Data.Entities;

namespace FamOs.Web.Data.Dtos;

public class QuoteWithCoverageDto
{
    public Guid Id { get; set; }
    public Guid OpportunityId { get; set; }
    public Guid? LineOfBusinessId { get; set; }
    public string CarrierName { get; set; } = "";
    public decimal PremiumAmount { get; set; }
    public CoverageDetailsDto? CoverageDetails { get; set; }
    public DateTime ReceivedAt { get; set; }
    public List<QuoteLine> QuoteLines { get; set; } = new();
}
