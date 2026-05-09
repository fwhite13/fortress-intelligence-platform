using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("kb_team_members")]
public class KbTeamMember
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("team_id")]
    [Required]
    public int TeamId { get; set; }

    [Column("user_id")]
    [MaxLength(36)]
    [Required]
    public string UserId { get; set; } = "";

    [Column("role")]
    public KbTeamRole Role { get; set; } = KbTeamRole.Member;

    [Column("joined_at")]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public KbTeam? Team { get; set; }
}

public enum KbTeamRole { Member = 0, Owner = 1 }
