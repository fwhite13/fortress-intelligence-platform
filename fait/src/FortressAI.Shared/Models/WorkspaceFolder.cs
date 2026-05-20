using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Shared.Models;

[Table("workspace_folders")]
public class WorkspaceFolder
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("name")]
    [MaxLength(64)]
    public string Name { get; set; } = "";

    [Column("s3_prefix")]
    [MaxLength(500)]
    public string S3Prefix { get; set; } = "";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
