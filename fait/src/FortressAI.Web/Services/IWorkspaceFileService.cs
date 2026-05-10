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
    Task<UserWorkspaceFile> SaveArtifactAsync(
        Guid userId, Guid conversationId, Guid? taskRunId,
        ArtifactPayload payload, CancellationToken ct = default);

    Task<List<UserWorkspaceFile>> GetConversationArtifactsAsync(
        Guid conversationId, CancellationToken ct = default);

    Task<List<UserWorkspaceFile>> GetUserArtifactsAsync(
        Guid userId, CancellationToken ct = default);

    Task<string> GetPresignedDownloadUrlAsync(
        string s3Key, int expiryMinutes = 30, CancellationToken ct = default);
}
