namespace FortressAI.Shared.Models;

public class KbTeam
{
    public int Id { get; set; }
    public Guid CreatorId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<KbTeamMember> Members { get; set; } = new List<KbTeamMember>();
    public ICollection<KbEntry> Entries { get; set; } = new List<KbEntry>();
}
