using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Shared.Models;

[Table("workspace_file_versions")]
public class WorkspaceFileVersion
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("file_id")]
    public Guid FileId { get; set; }

    [Column("version_number")]
    public int VersionNumber { get; set; } = 1;

    [Column("s3_key")]
    [MaxLength(1000)]
    public string S3Key { get; set; } = "";

    [Column("size_bytes")]
    public long SizeBytes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("created_by")]
    [MaxLength(20)]
    public string CreatedBy { get; set; } = "user";
}
