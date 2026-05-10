namespace FortressAI.Shared.Models;

public class UserWorkspaceFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? TaskRunId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public string S3Key { get; set; } = string.Empty;
    public long SizeBytes { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
