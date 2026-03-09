namespace FortressAI.Shared.Models;

public class KbTeamMember
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public Guid UserId { get; set; }
    public KbTeamRole Role { get; set; } = KbTeamRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public KbTeam Team { get; set; } = null!;
}

public enum KbTeamRole { Member = 0, Owner = 1 }
