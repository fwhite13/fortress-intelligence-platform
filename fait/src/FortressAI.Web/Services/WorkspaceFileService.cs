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

    public async Task<WorkspaceUpload> SaveArtifactAsync(
        Guid userId, Guid conversationId, Guid? taskRunId,
        ArtifactPayload payload, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // DEDUP: if a row with this (userId, s3Key) already exists, return it immediately
        var existing = await db.WorkspaceUploads
            .FirstOrDefaultAsync(u => u.UserId == userId && u.S3Key == payload.S3Key, ct);
        if (existing != null)
        {
            _logger.LogDebug("[WorkspaceFileService] Artifact already registered (s3_key={S3Key}), returning existing row {Id}", payload.S3Key, existing.Id);
            return existing;
        }

        // Find or create the default 'general' folder for this user
        var folder = await db.WorkspaceFolders
            .FirstOrDefaultAsync(f => f.UserId == userId && f.Name == "general", ct);

        if (folder == null)
        {
            var folderId = Guid.NewGuid();
            folder = new WorkspaceFolder
            {
                Id = folderId,
                UserId = userId,
                Name = "general",
                S3Prefix = $"files/{folderId}/",
                CreatedAt = DateTime.UtcNow
            };
            db.WorkspaceFolders.Add(folder);
            await db.SaveChangesAsync(ct);
        }

        var upload = new WorkspaceUpload
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folder.Id,
            Filename = payload.Filename,
            MimeType = payload.MimeType,
            S3Key = payload.S3Key,
            SizeBytes = payload.SizeBytes,
            CreatedAt = DateTime.UtcNow,
            CurrentVersion = 1,
            Source = "assistant",
            ConversationId = conversationId.ToString()
        };
        db.WorkspaceUploads.Add(upload);
        await db.SaveChangesAsync(ct);
        return upload;
    }

    public async Task<List<WorkspaceUpload>> GetConversationArtifactsAsync(
        Guid conversationId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceUploads
            .Where(f => f.ConversationId == conversationId.ToString() && (f.Source == "assistant" || f.Source == "cc"))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<List<WorkspaceUpload>> GetUserArtifactsAsync(
        Guid userId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.WorkspaceUploads
            .Where(f => f.UserId == userId && (f.Source == "assistant" || f.Source == "cc"))
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
