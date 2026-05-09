using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("kb_teams")]
public class KbTeam
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("creator_id")]
    [MaxLength(36)]
    [Required]
    public string CreatorId { get; set; } = "";

    [Column("name")]
    [MaxLength(200)]
    [Required]
    public string Name { get; set; } = "";

    [Column("description")]
    [MaxLength(1000)]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<KbTeamMember> Members { get; set; } = new List<KbTeamMember>();
    public ICollection<KbEntry> Entries { get; set; } = new List<KbEntry>();
}
