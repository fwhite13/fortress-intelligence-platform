using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

public interface IWorkspaceUploadService
{
    Task<List<WorkspaceFolder>> GetFoldersAsync(Guid userId, Guid? parentId = null);
    Task<WorkspaceFolder> CreateFolderAsync(Guid userId, string name, Guid? parentId = null);
    Task DeleteFolderAsync(Guid userId, Guid folderId);
    Task<List<WorkspaceUpload>> GetFilesAsync(Guid userId, Guid? folderId = null);
    Task<WorkspaceUpload> SaveUploadAsync(Guid userId, Guid? folderId, string filename, string mimeType, Stream content);
    Task DeleteFileAsync(Guid userId, Guid fileId);
    Task<string> GetPresignedUrlAsync(string s3Key, int expiryMinutes = 30);
    Task<string> ReadFileContentAsync(string s3Key);
    Task<(Guid? folderId, string? s3Key)?> ResolvePathAsync(Guid userId, string virtualPath);
    Task<List<WorkspaceFileVersion>> GetFileVersionsAsync(Guid userId, Guid fileId);
    Task<WorkspaceUpload?> RollbackFileAsync(Guid userId, Guid fileId, int versionNumber);
    Task<WorkspaceUpload?> RenameFileAsync(Guid userId, Guid fileId, string newFilename);
    Task<WorkspaceUpload?> MoveFileAsync(Guid userId, Guid fileId, Guid? newFolderId);
    Task<BulkDeleteResult> BulkDeleteFilesAsync(Guid userId, List<Guid> fileIds);
}
