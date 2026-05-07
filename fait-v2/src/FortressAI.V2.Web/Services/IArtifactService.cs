using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IArtifactService
{
    Task<ArtifactRecord> RecordArtifactAsync(string userId, CCExecutionResult result, string taskDescription, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string userId, string artifactId, CancellationToken ct = default);
    Task<List<ArtifactRecord>> GetRecentArtifactsAsync(string userId, int limit = 10, CancellationToken ct = default);
}
