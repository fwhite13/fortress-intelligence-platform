using Microsoft.EntityFrameworkCore;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public class ArtifactService : IArtifactService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IWorkspaceService _workspaceService;
    private readonly ILogger<ArtifactService> _logger;

    public ArtifactService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        IWorkspaceService workspaceService,
        ILogger<ArtifactService> logger)
    {
        _dbFactory = dbFactory;
        _workspaceService = workspaceService;
        _logger = logger;
    }

    public async Task<ArtifactRecord> RecordArtifactAsync(
        string userId,
        CCExecutionResult result,
        string taskDescription,
        CancellationToken ct = default)
    {
        var s3Key = result.ArtifactS3Key ?? string.Empty;
        var fileName = string.IsNullOrEmpty(s3Key) ? "artifact" : Path.GetFileName(s3Key);

        var record = new ArtifactRecord
        {
            Id = Guid.NewGuid().ToString(),
            UserId = userId,
            Type = result.ArtifactType ?? "unknown",
            FileName = fileName,
            S3Key = s3Key,
            SizeBytes = 0,
            TaskDescription = taskDescription,
            CreatedAt = DateTime.UtcNow,
        };

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        db.ArtifactRecords.Add(record);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Recorded artifact {ArtifactId} ({Type}) for user {UserId}", record.Id, record.Type, userId);
        return record;
    }

    public async Task<string> GetDownloadUrlAsync(string userId, string artifactId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var record = await db.ArtifactRecords
            .FirstOrDefaultAsync(r => r.Id == artifactId && r.UserId == userId, ct);

        if (record == null)
            throw new InvalidOperationException($"Artifact {artifactId} not found for user {userId}");

        return await _workspaceService.GetDownloadUrlAsync(userId, record.S3Key, ct);
    }

    public async Task<List<ArtifactRecord>> GetRecentArtifactsAsync(string userId, int limit = 10, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ArtifactRecords
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);
    }
}
