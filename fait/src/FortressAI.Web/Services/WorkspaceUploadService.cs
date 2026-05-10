using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

public class WorkspaceUploadService : IWorkspaceUploadService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<WorkspaceUploadService> _logger;

    public WorkspaceUploadService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<WorkspaceUploadService> logger)
    {
        _dbFactory = dbFactory;
        _s3 = s3;
        _bucket = config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
        _logger = logger;
    }

    public async Task<List<WorkspaceFolder>> GetFoldersAsync(Guid userId, Guid? parentId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.WorkspaceFolders
            .Where(f => f.UserId == userId && f.ParentId == parentId)
            .OrderBy(f => f.Name)
            .ToListAsync();
    }

    public async Task<WorkspaceFolder> CreateFolderAsync(Guid userId, string name, Guid? parentId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var folder = new WorkspaceFolder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name.Trim(),
            ParentId = parentId,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkspaceFolders.Add(folder);
        await db.SaveChangesAsync();
        return folder;
    }

    public async Task DeleteFolderAsync(Guid userId, Guid folderId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var s3Keys = new List<string>();
        await CollectS3KeysRecursiveAsync(db, userId, folderId, s3Keys);

        if (s3Keys.Count > 0)
        {
            for (int i = 0; i < s3Keys.Count; i += 1000)
            {
                var batch = s3Keys.Skip(i).Take(1000).Select(k => new KeyVersion { Key = k }).ToList();
                await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = _bucket,
                    Objects = batch
                });
            }
        }

        var folder = await db.WorkspaceFolders.FirstOrDefaultAsync(f => f.Id == folderId && f.UserId == userId);
        if (folder != null)
        {
            db.WorkspaceFolders.Remove(folder);
            await db.SaveChangesAsync();
        }
    }

    private async Task CollectS3KeysRecursiveAsync(AppDbContext db, Guid userId, Guid folderId, List<string> keys)
    {
        var uploads = await db.WorkspaceUploads
            .Where(u => u.UserId == userId && u.FolderId == folderId)
            .Select(u => u.S3Key)
            .ToListAsync();
        keys.AddRange(uploads);

        var childFolders = await db.WorkspaceFolders
            .Where(f => f.UserId == userId && f.ParentId == folderId)
            .Select(f => f.Id)
            .ToListAsync();
        foreach (var childId in childFolders)
        {
            await CollectS3KeysRecursiveAsync(db, userId, childId, keys);
        }
    }

    public async Task<List<WorkspaceUpload>> GetFilesAsync(Guid userId, Guid? folderId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.WorkspaceUploads
            .Where(u => u.UserId == userId && u.FolderId == folderId)
            .OrderBy(u => u.Filename)
            .ToListAsync();
    }

    public async Task<WorkspaceUpload> SaveUploadAsync(Guid userId, Guid? folderId, string filename, string mimeType, Stream content)
    {
        var id = Guid.NewGuid();
        var safeFilename = Path.GetFileName(filename);
        var s3Key = $"workspaces/{userId}/files/{folderId?.ToString() ?? "root"}/{safeFilename}";

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = s3Key,
            InputStream = content,
            ContentType = mimeType
        });

        var sizeBytes = content.CanSeek ? content.Length : 0;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var upload = new WorkspaceUpload
        {
            Id = id,
            UserId = userId,
            FolderId = folderId,
            Filename = safeFilename,
            MimeType = mimeType,
            S3Key = s3Key,
            SizeBytes = sizeBytes,
            CreatedAt = DateTime.UtcNow
        };
        db.WorkspaceUploads.Add(upload);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            // Rollback: delete the S3 object we just uploaded
            try { await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = s3Key }); }
            catch { /* best-effort cleanup */ }
            throw;
        }
        return upload;
    }

    public async Task DeleteFileAsync(Guid userId, Guid fileId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var upload = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == fileId && u.UserId == userId);
        if (upload == null) return;

        await _s3.DeleteObjectAsync(_bucket, upload.S3Key);
        db.WorkspaceUploads.Remove(upload);
        await db.SaveChangesAsync();
    }

    public Task<string> GetPresignedUrlAsync(string s3Key, int expiryMinutes = 30)
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

    public async Task<string> ReadFileContentAsync(string s3Key)
    {
        try
        {
            var response = await _s3.GetObjectAsync(_bucket, s3Key);
            const int maxBytes = 512000;
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms);
            var bytes = ms.ToArray();

            for (int i = 0; i < Math.Min(bytes.Length, 8192); i++)
            {
                if (bytes[i] == 0)
                    return "Binary file — cannot read as text. Use download instead.";
            }

            string content;
            bool truncated = false;
            if (bytes.Length > maxBytes)
            {
                content = System.Text.Encoding.UTF8.GetString(bytes, 0, maxBytes);
                truncated = true;
            }
            else
            {
                content = System.Text.Encoding.UTF8.GetString(bytes);
            }

            if (truncated) content += "\n[Content truncated at 500KB]";
            return content;
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return "File not found.";
        }
    }

    public async Task<(Guid? folderId, string? s3Key)?> ResolvePathAsync(Guid userId, string virtualPath)
    {
        var parts = virtualPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var filename = parts[^1];
        var folderSegments = parts[..^1];

        Guid? currentParentId = null;
        foreach (var segment in folderSegments)
        {
            var folder = await db.WorkspaceFolders
                .FirstOrDefaultAsync(f => f.UserId == userId && f.Name == segment && f.ParentId == currentParentId);
            if (folder == null) return null;
            currentParentId = folder.Id;
        }

        var upload = await db.WorkspaceUploads
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Filename == filename && u.FolderId == currentParentId);
        if (upload == null) return null;

        return (currentParentId, upload.S3Key);
    }
}
