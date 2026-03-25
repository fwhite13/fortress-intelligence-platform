namespace FamOs.Web.Data.Entities;

public class IntakeSession
{
    public long Id { get; set; }
    public string OpportunityId { get; set; } = "";
    public string Email { get; set; } = "";
    public string? OtpCode { get; set; }
    public DateTime? OtpExpiresAt { get; set; }
    public bool IsVerified { get; set; }
    public string? LastPage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
