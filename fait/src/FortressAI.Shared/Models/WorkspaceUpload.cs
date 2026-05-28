using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.Shared.Models;

[Table("user_workspace_uploads")]
public class WorkspaceUpload
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    [Column("user_id")]
    public Guid UserId { get; set; }

    [Column("folder_id")]
    public Guid? FolderId { get; set; }

    [Column("filename")]
    [MaxLength(500)]
    public string Filename { get; set; } = "";

    [Column("mime_type")]
    [MaxLength(200)]
    public string MimeType { get; set; } = "";

    [Column("s3_key")]
    [MaxLength(1000)]
    public string S3Key { get; set; } = "";

    [Column("size_bytes")]
    public long SizeBytes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    [Column("current_version")]
    public int CurrentVersion { get; set; } = 1;

    [Column("source")]
    [MaxLength(20)]
    public string? Source { get; set; }

    [Column("conversation_id")]
    [MaxLength(36)]
    public string? ConversationId { get; set; }

    [Column("turn_index")]
    public int? TurnIndex { get; set; }

    [Column("preview_s3_key")]
    [MaxLength(500)]
    public string? PreviewS3Key { get; set; }
}
