namespace FortressAI.V2.Web.Services;

public interface IWorkspaceService
{
    Task<List<WorkspaceFolder>> GetFolderStructureAsync(string userId, CancellationToken ct = default);
    Task<List<WorkspaceFile>> ListFilesAsync(string userId, string folder, CancellationToken ct = default);
    Task<string> GetDownloadUrlAsync(string userId, string s3Key, CancellationToken ct = default);
    Task DeleteFileAsync(string userId, string s3Key, CancellationToken ct = default);
}

public class WorkspaceFolder
{
    public string Name { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public int FileCount { get; set; }
}

public class WorkspaceFile
{
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public string Folder { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
}
