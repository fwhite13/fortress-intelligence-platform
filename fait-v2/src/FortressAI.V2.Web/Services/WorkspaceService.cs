using Amazon.S3;
using Amazon.S3.Model;

namespace FortressAI.V2.Web.Services;

public class WorkspaceService : IWorkspaceService
{
    private static readonly string[] Folders = ["artifacts", "uploads", "memory", "assistants"];
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private string Bucket => _config["AWS:WorkspaceBucket"] ?? "fortress-user-workspaces";

    public WorkspaceService(IAmazonS3 s3, IConfiguration config)
    {
        _s3 = s3;
        _config = config;
    }

    public async Task<List<WorkspaceFolder>> GetFolderStructureAsync(string userId, CancellationToken ct = default)
    {
        var result = new List<WorkspaceFolder>();
        foreach (var folder in Folders)
        {
            var prefix = $"workspaces/{userId}/{folder}/";
            var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = prefix,
                MaxKeys = 1000,
            }, ct);
            result.Add(new WorkspaceFolder
            {
                Name = folder,
                Prefix = prefix,
                FileCount = response.S3Objects.Count(o => o.Key != prefix),
            });
        }
        return result;
    }

    public async Task<List<WorkspaceFile>> ListFilesAsync(string userId, string folder, CancellationToken ct = default)
    {
        var prefix = $"workspaces/{userId}/{folder}/";
        var response = await _s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = Bucket,
            Prefix = prefix,
            MaxKeys = 500,
        }, ct);

        return response.S3Objects
            .Where(o => o.Key != prefix)
            .Select(o => new WorkspaceFile
            {
                Key = o.Key,
                FileName = Path.GetFileName(o.Key),
                SizeBytes = o.Size,
                LastModified = o.LastModified,
                Folder = folder,
                Extension = Path.GetExtension(o.Key).ToLowerInvariant(),
            })
            .OrderByDescending(f => f.LastModified)
            .ToList();
    }

    public Task<string> GetDownloadUrlAsync(string userId, string s3Key, CancellationToken ct = default)
    {
        if (!s3Key.StartsWith($"workspaces/{userId}/"))
            throw new UnauthorizedAccessException("Access denied");

        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            Expires = DateTime.UtcNow.AddMinutes(15),
            Verb = HttpVerb.GET,
        };
        return Task.FromResult(_s3.GetPreSignedURL(request));
    }

    public async Task DeleteFileAsync(string userId, string s3Key, CancellationToken ct = default)
    {
        if (!s3Key.StartsWith($"workspaces/{userId}/"))
            throw new UnauthorizedAccessException("Access denied");

        await _s3.DeleteObjectAsync(Bucket, s3Key, ct);
    }
}
