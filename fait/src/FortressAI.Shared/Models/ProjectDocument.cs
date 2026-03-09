namespace FortressAI.Shared.Models;

public class ProjectDocument
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string? Content { get; set; }
    public long FileSize { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // RAG / S3 ingestion tracking
    public string? S3Key { get; set; }          // null = not yet in S3/RAG
    public string IngestionStatus { get; set; } = "none";  // "none" | "pending" | "ingested" | "failed"
    public DateTime? IngestedAt { get; set; }

    // Navigation
    public Project? Project { get; set; }

    // Non-persisted — set after upload to surface warnings to the UI
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? UploadWarning { get; set; }
}
