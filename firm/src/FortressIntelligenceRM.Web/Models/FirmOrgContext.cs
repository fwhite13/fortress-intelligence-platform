using System.ComponentModel.DataAnnotations;

namespace FortressIntelligenceRM.Web.Models;

public class FirmOrgContext
{
    public long Id { get; set; }
    [MaxLength(36)]
    public string EntraTenantId { get; set; } = "";
    public string? WikiContent { get; set; }
    public DateTime UpdatedAt { get; set; }
    [MaxLength(256)]
    public string? UpdatedBy { get; set; }
}
