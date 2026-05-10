using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Shared.Models;
using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class WorkspaceFileService : IWorkspaceFileService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<WorkspaceFileService> _logger;

    public WorkspaceFileService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<WorkspaceFileService> logger)
    {
        _dbFactory = dbFactory;
        _s3 = s3;
        _bucket = config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
        _logger = logger;
    }

    public async Task<UserWorkspaceFile> SaveArtifactAsync(
        Guid userId, Guid conversationId, Guid? taskRunId,
        ArtifactPayload payload, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var file = new UserWorkspaceFile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ConversationId = conversationId,
            TaskRunId = taskRunId,
            Filename = payload.Filename,
            MimeType = payload.MimeType,
            S3Key = payload.S3Key,
            SizeBytes = payload.SizeBytes,
            CreatedAt = DateTime.UtcNow
        };
        db.UserWorkspaceFiles.Add(file);
        await db.SaveChangesAsync(ct);
        return file;
    }

    public async Task<List<UserWorkspaceFile>> GetConversationArtifactsAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.UserWorkspaceFiles
            .Where(f => f.ConversationId == conversationId)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<UserWorkspaceFile>> GetUserArtifactsAsync(
        Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.UserWorkspaceFiles
            .Where(f => f.UserId == userId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(ct);
    }

    public Task<string> GetPresignedDownloadUrlAsync(
        string s3Key, int expiryMinutes = 30, CancellationToken ct = default)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucket,
            Key = s3Key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes)
        };
        var url = _s3.GetPreSignedURL(request);
        return Task.FromResult(url);
    }
}
