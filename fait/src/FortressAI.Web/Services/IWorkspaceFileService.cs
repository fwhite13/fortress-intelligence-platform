using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

public record ArtifactPayload(
    string Filename,
    string S3Key,
    string MimeType,
    long SizeBytes
);

public interface IWorkspaceFileService
{
    Task<WorkspaceUpload> SaveArtifactAsync(
        Guid userId, Guid conversationId, Guid? taskRunId,
        ArtifactPayload payload, CancellationToken ct = default);

    Task<List<WorkspaceUpload>> GetConversationArtifactsAsync(
        Guid conversationId, CancellationToken ct = default);

    Task<List<WorkspaceUpload>> GetUserArtifactsAsync(
        Guid userId, CancellationToken ct = default);

    Task<string> GetPresignedDownloadUrlAsync(
        string s3Key, int expiryMinutes = 30, CancellationToken ct = default);

    /// <summary>
    /// Returns a CloudFront signed URL if CloudFront is configured, otherwise falls back to S3 presigned URL.
    /// Use this for Office Online embed (requires publicly accessible URL).
    /// </summary>
    Task<string> GetFilePreviewUrlAsync(string s3Key, int? expirySeconds = null, CancellationToken ct = default);
}
