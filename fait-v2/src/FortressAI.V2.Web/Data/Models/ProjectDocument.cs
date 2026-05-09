using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FortressAI.V2.Web.Data.Models;

[Table("project_documents")]
public class ProjectDocument
{
    [Key]
    [Column("id")]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [Column("project_id")]
    [MaxLength(36)]
    public string? ProjectId { get; set; }

    [Column("filename")]
    [MaxLength(500)]
    [Required]
    public string Filename { get; set; } = string.Empty;

    [Column("content_type")]
    [MaxLength(200)]
    public string? ContentType { get; set; }

    [Column("content", TypeName = "longtext")]
    public string? Content { get; set; }

    [Column("file_size")]
    public long FileSize { get; set; }

    [Column("uploaded_at")]
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    [Column("s3_key")]
    [MaxLength(1000)]
    public string? S3Key { get; set; }

    [Column("ingestion_status")]
    [MaxLength(50)]
    public string IngestionStatus { get; set; } = "none";

    [Column("ingested_at")]
    public DateTime? IngestedAt { get; set; }

    [NotMapped]
    public string? UploadWarning { get; set; }

    public Project? Project { get; set; }
}
