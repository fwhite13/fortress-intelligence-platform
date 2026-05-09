namespace FortressAI.V2.Web.Models;

public record ArtifactInfo(
    string ArtifactId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string S3Key,
    DateTimeOffset CreatedAt,
    string? TaskId,
    string? ProjectId
);
