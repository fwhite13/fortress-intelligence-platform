namespace FortressNexus.Web.Models.Entities;

public class NexusUserRole
{
    public int Id { get; set; }
    public string UserUpn { get; set; } = "";
    public string Role { get; set; } = "";
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public string AssignedBy { get; set; } = "system";
}
