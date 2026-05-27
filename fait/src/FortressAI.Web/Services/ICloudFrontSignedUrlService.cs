namespace FortressAI.Web.Services;

/// <summary>
/// Generates short-lived CloudFront signed URLs for S3-backed objects.
/// Returns null when CloudFront is not configured (falls back to S3 presigned URLs).
/// </summary>
public interface ICloudFrontSignedUrlService
{
    /// <summary>
    /// Returns true when the service is configured and ready to sign URLs.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Generates a CloudFront signed URL for the given S3 key.
    /// </summary>
    /// <param name="s3Key">The S3 object key (e.g. "files/abc123/report.pptx")</param>
    /// <param name="expirySeconds">URL validity in seconds. Defaults to configured value (3600).</param>
    /// <returns>A signed CloudFront URL, or null if not configured.</returns>
    Task<string?> GetSignedUrlAsync(string s3Key, int? expirySeconds = null);
}
