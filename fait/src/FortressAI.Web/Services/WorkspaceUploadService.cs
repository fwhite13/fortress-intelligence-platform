using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

public record BulkDeleteResult(int Succeeded, List<Guid> FailedIds, List<string> Errors);

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

        // C3: Delete all version rows for files in this folder tree
        var uploadIds = await CollectUploadIdsRecursiveAsync(db, userId, folderId);
        if (uploadIds.Count > 0)
        {
            var versionRowsToDelete = await db.WorkspaceFileVersions
                .Where(v => uploadIds.Contains(v.FileId))
                .ToListAsync();
            db.WorkspaceFileVersions.RemoveRange(versionRowsToDelete);
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
            .ToListAsync();

        foreach (var upload in uploads)
        {
            // C3: Collect all version S3 keys for each file in the folder
            var versionKeys = await db.WorkspaceFileVersions
                .Where(v => v.FileId == upload.Id)
                .Select(v => v.S3Key)
                .ToListAsync();

            foreach (var key in versionKeys)
            {
                if (!keys.Contains(key))
                    keys.Add(key);
            }

            // Also include the current S3Key (may not be in version rows if versions are missing)
            if (!keys.Contains(upload.S3Key))
                keys.Add(upload.S3Key);
        }

        var childFolders = await db.WorkspaceFolders
            .Where(f => f.UserId == userId && f.ParentId == folderId)
            .Select(f => f.Id)
            .ToListAsync();
        foreach (var childId in childFolders)
        {
            await CollectS3KeysRecursiveAsync(db, userId, childId, keys);
        }
    }

    // C3: New helper — collect all upload IDs in a folder tree for version row cleanup
    private async Task<List<Guid>> CollectUploadIdsRecursiveAsync(AppDbContext db, Guid userId, Guid folderId)
    {
        var ids = new List<Guid>();
        var uploads = await db.WorkspaceUploads
            .Where(u => u.UserId == userId && u.FolderId == folderId)
            .Select(u => u.Id)
            .ToListAsync();
        ids.AddRange(uploads);

        var childFolders = await db.WorkspaceFolders
            .Where(f => f.UserId == userId && f.ParentId == folderId)
            .Select(f => f.Id)
            .ToListAsync();
        foreach (var childId in childFolders)
        {
            var childIds = await CollectUploadIdsRecursiveAsync(db, userId, childId);
            ids.AddRange(childIds);
        }
        return ids;
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
        var safeFilename = Path.GetFileName(filename);

        // I7: Buffer stream upfront — stream.Length throws on non-seekable streams (e.g., HTTP multipart)
        byte[] fileBytes;
        using (var ms = new MemoryStream())
        {
            await content.CopyToAsync(ms);
            fileBytes = ms.ToArray();
        }
        var sizeBytes = (long)fileBytes.Length;

        await using var db = await _dbFactory.CreateDbContextAsync();

        var existingFile = await db.WorkspaceUploads
            .FirstOrDefaultAsync(u => u.UserId == userId && u.Filename == safeFilename && u.FolderId == folderId);

        var versionNumber = (existingFile?.CurrentVersion ?? 0) + 1;
        var s3Key = $"workspaces/{userId}/files/{folderId?.ToString() ?? "root"}/v{versionNumber}/{safeFilename}";

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = s3Key,
            InputStream = new MemoryStream(fileBytes),
            ContentType = mimeType
        });

        var versionRow = new WorkspaceFileVersion
        {
            Id = Guid.NewGuid(),
            FileId = existingFile?.Id ?? Guid.NewGuid(),
            VersionNumber = versionNumber,
            S3Key = s3Key,
            SizeBytes = sizeBytes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "user"
        };

        WorkspaceUpload upload;
        if (existingFile != null)
        {
            existingFile.S3Key = s3Key;
            existingFile.SizeBytes = sizeBytes;
            existingFile.CurrentVersion = versionNumber;
            existingFile.Source = "user";
            versionRow.FileId = existingFile.Id;
            upload = existingFile;
        }
        else
        {
            upload = new WorkspaceUpload
            {
                Id = versionRow.FileId,
                UserId = userId,
                FolderId = folderId,
                Filename = safeFilename,
                MimeType = mimeType,
                S3Key = s3Key,
                SizeBytes = sizeBytes,
                CreatedAt = DateTime.UtcNow,
                CurrentVersion = 1,
                Source = "user"
            };
            db.WorkspaceUploads.Add(upload);
        }

        db.WorkspaceFileVersions.Add(versionRow);

        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
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

        // C3: Delete all version S3 objects before removing DB rows
        var versions = await db.WorkspaceFileVersions
            .Where(v => v.FileId == fileId)
            .ToListAsync();

        // Delete all version S3 keys (deduplicate)
        var allS3Keys = versions.Select(v => v.S3Key).Distinct().ToList();
        // Also include the current S3Key in case it differs
        if (!allS3Keys.Contains(upload.S3Key))
            allS3Keys.Add(upload.S3Key);

        foreach (var key in allS3Keys)
        {
            try
            {
                await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = _bucket, Key = key });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WorkspaceUploadService] DeleteFileAsync: failed to delete S3 key {Key}", key);
            }
        }

        // Remove version rows and upload row
        db.WorkspaceFileVersions.RemoveRange(versions);
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

    public async Task<List<WorkspaceFileVersion>> GetFileVersionsAsync(Guid userId, Guid fileId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == fileId && u.UserId == userId);
        if (file == null) return new List<WorkspaceFileVersion>();

        return await db.WorkspaceFileVersions
            .Where(v => v.FileId == fileId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync();
    }

    public async Task<WorkspaceUpload?> RollbackFileAsync(Guid userId, Guid fileId, int versionNumber)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == fileId && u.UserId == userId);
        if (file == null) return null;

        var version = await db.WorkspaceFileVersions
            .FirstOrDefaultAsync(v => v.FileId == fileId && v.VersionNumber == versionNumber);
        if (version == null) return null;

        // I4: Rollback = create a NEW version pointing to the old S3 key (preserves audit trail)
        // Do NOT reset CurrentVersion to versionNumber — that causes collisions with existing version rows
        var maxVersion = await db.WorkspaceFileVersions
            .Where(v => v.FileId == fileId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? file.CurrentVersion;

        var newVersionNumber = maxVersion + 1;

        var newVersionRow = new WorkspaceFileVersion
        {
            Id = Guid.NewGuid(),
            FileId = fileId,
            VersionNumber = newVersionNumber,
            S3Key = version.S3Key,
            SizeBytes = version.SizeBytes,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "rollback"
        };
        db.WorkspaceFileVersions.Add(newVersionRow);

        file.S3Key = version.S3Key;
        file.CurrentVersion = newVersionNumber;
        file.SizeBytes = version.SizeBytes;

        await db.SaveChangesAsync();
        return file;
    }

    public async Task<WorkspaceUpload?> RenameFileAsync(Guid userId, Guid fileId, string newFilename)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == fileId && u.UserId == userId);
        if (file == null) return null;

        file.Filename = Path.GetFileName(newFilename);
        await db.SaveChangesAsync();
        return file;
    }

    public async Task<WorkspaceUpload?> MoveFileAsync(Guid userId, Guid fileId, Guid? newFolderId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var file = await db.WorkspaceUploads.FirstOrDefaultAsync(u => u.Id == fileId && u.UserId == userId);
        if (file == null) return null;

        file.FolderId = newFolderId;
        await db.SaveChangesAsync();
        return file;
    }

    public async Task<BulkDeleteResult> BulkDeleteFilesAsync(Guid userId, List<Guid> fileIds)
    {
        var succeeded = 0;
        var failedIds = new List<Guid>();
        var errors = new List<string>();

        foreach (var fileId in fileIds)
        {
            try
            {
                await DeleteFileAsync(userId, fileId);
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WorkspaceUploadService] BulkDeleteFilesAsync: failed to delete fileId={FileId}", fileId);
                failedIds.Add(fileId);
                errors.Add(ex.Message);
            }
        }

        return new BulkDeleteResult(succeeded, failedIds, errors);
    }
}
