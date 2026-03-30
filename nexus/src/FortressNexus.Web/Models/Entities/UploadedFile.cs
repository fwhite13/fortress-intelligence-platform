namespace FortressNexus.Web.Models.Entities;

public class UploadedFile
{
    public int Id { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string S3Key { get; set; } = "";
    public string S3Bucket { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public DateTime UploadedAt { get; set; }
    public string? ProcessedText { get; set; }

    // Navigation
    public List<Submission> Submissions { get; set; } = new();
}
