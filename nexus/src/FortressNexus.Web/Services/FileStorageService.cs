using Amazon.S3;
using Amazon.S3.Model;
using FortressNexus.Web.Models.Entities;
using Microsoft.AspNetCore.Components.Forms;

namespace FortressNexus.Web.Services;

public class FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<FileStorageService> _logger;

    private static readonly string[] AllowedTypes = ["text/html", "image/png", "image/jpeg", "image/webp"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    private string Bucket => _config["Nexus:S3Bucket"] ?? "nexus-uploads-dev";

    public FileStorageService(IAmazonS3 s3, IConfiguration config, ILogger<FileStorageService> logger)
    {
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    public async Task<UploadedFile> UploadAsync(IBrowserFile file, string uploaderUpn)
    {
        if (file.Size > MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds maximum size of 10MB. Actual size: {file.Size / 1024 / 1024}MB.");

        if (!AllowedTypes.Contains(file.ContentType))
            throw new InvalidOperationException($"File type '{file.ContentType}' is not allowed. Accepted: text/html, image/png, image/jpeg, image/webp.");

        var safeFileName = Path.GetFileName(file.Name);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new InvalidOperationException("Invalid filename.");
        var s3Key = $"nexus/{uploaderUpn}/{Guid.NewGuid()}/{safeFileName}";
        string? processedText = null;

        using var stream = file.OpenReadStream(MaxFileSizeBytes);

        if (file.ContentType == "text/html")
        {
            // Read text content for HTML mockups
            using var reader = new StreamReader(stream);
            var htmlContent = await reader.ReadToEndAsync();
            processedText = htmlContent;

            // Re-create stream for upload
            var htmlBytes = System.Text.Encoding.UTF8.GetBytes(htmlContent);
            using var uploadStream = new MemoryStream(htmlBytes);
            var putRequest = new PutObjectRequest
            {
                BucketName = Bucket,
                Key = s3Key,
                InputStream = uploadStream,
                ContentType = file.ContentType,
                Metadata = { ["original-filename"] = file.Name, ["uploader-upn"] = uploaderUpn }
            };
            await _s3.PutObjectAsync(putRequest);
        }
        else
        {
            // Images — upload directly, ProcessedText stays null (AI vision handled later)
            var imageBytes = new byte[file.Size];
            await stream.ReadExactlyAsync(imageBytes);
            using var uploadStream = new MemoryStream(imageBytes);
            var putRequest = new PutObjectRequest
            {
                BucketName = Bucket,
                Key = s3Key,
                InputStream = uploadStream,
                ContentType = file.ContentType,
                Metadata = { ["original-filename"] = file.Name, ["uploader-upn"] = uploaderUpn }
            };
            await _s3.PutObjectAsync(putRequest);
        }

        _logger.LogInformation("NEXUS: Uploaded {FileName} to S3 key {Key}", file.Name, s3Key);

        return new UploadedFile
        {
            OriginalFileName = safeFileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Size,
            S3Key = s3Key,
            S3Bucket = Bucket,
            UploadedBy = uploaderUpn,
            UploadedAt = DateTime.UtcNow,
            ProcessedText = processedText
        };
    }

    public async Task<Stream> DownloadAsync(string s3Key)
    {
        var response = await _s3.GetObjectAsync(Bucket, s3Key);
        return response.ResponseStream;
    }

    public async Task<string> GetPresignedUrlAsync(string s3Key, int expiryMinutes = 15)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes)
        };
        return await _s3.GetPreSignedURLAsync(request);
    }
}
