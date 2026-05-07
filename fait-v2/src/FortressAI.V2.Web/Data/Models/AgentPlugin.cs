namespace FortressAI.V2.Web.Data.Models;

public class AgentPlugin
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? SkillsDirectory { get; set; }
    public string AllowedMcpServers { get; set; } = "[]";
    public string AllowedRoles { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
