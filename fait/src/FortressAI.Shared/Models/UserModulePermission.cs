namespace FortressAI.Shared.Models;

public class UserModulePermission
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string Module { get; set; } = string.Empty;     // e.g. "fait", "forms", "firm"
    public string Permission { get; set; } = string.Empty;  // e.g. "access", "admin", "read_only"
    public bool Granted { get; set; } = true;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public Guid? GrantedByUserId { get; set; }

    // Navigation
    public AppUser? User { get; set; }
}
