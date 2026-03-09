namespace FortressFormTools.Web.Services;

/// <summary>
/// HTTP client wrapper for the existing Fortress Projects API.
/// Handles: get upload link → upload to S3 → submit request → poll for results.
/// </summary>
public interface IFortressProjectsClient
{
    /// <summary>Get presigned S3 upload URLs for files.</summary>
    Task<List<UploadLinkResult>> GetUploadLinksAsync(string clientReferenceId, List<string> fileNames);

    /// <summary>Upload a file to the presigned S3 URL.</summary>
    Task UploadFileAsync(string uploadUrl, byte[] fileData, string contentType);

    /// <summary>Submit a processing request with uploaded file keys.</summary>
    Task<string> SubmitRequestAsync(string clientReferenceId, List<string> fileKeys);

    /// <summary>Poll for request status and results.</summary>
    Task<ProjectRequestResult> GetRequestStatusAsync(string projectRequestId);
}

public class UploadLinkResult
{
    public string FileName { get; set; } = string.Empty;
    public string FileKey { get; set; } = string.Empty;
    public string UploadUrl { get; set; } = string.Empty;
}

public class ProjectRequestResult
{
    public string Status { get; set; } = string.Empty; // Pending, Processing, Assembling, Completed, Failed
    public string? ProjectRequestId { get; set; }
    public object? Results { get; set; }
    public string? RawJson { get; set; }
}
