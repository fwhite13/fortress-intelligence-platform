using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Shared.Models;

[Table("user_workspace_folders")]
public class WorkspaceFolder
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("name")]
    [MaxLength(255)]
    public string Name { get; set; } = "";

    [Column("parent_id")]
    public Guid? ParentId { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
}
