namespace FortressAI.V2.Web.Data.Models;

public class ArtifactRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;      // "docx", "xlsx", "pptx", "html", "json", "code"
    public string FileName { get; set; } = string.Empty;
    public string S3Key { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string? TaskDescription { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
