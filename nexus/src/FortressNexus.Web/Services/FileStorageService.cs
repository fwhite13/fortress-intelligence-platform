using Amazon.S3;
using Amazon.S3.Model;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Components.Forms;
using UglyToad.PdfPig;
using System.Text;

namespace FortressNexus.Web.Services;

public class FileStorageService : IFileStorageService
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<FileStorageService> _logger;

    private static readonly string[] AllowedTypes =
        ["text/html", "image/png", "image/jpeg", "image/jpg", "image/webp", "application/pdf", "text/markdown", "text/x-markdown", "application/json", "text/plain"];
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10MB

    private string Bucket => _config["Nexus:S3Bucket"] ?? "fortress-nexus-uploads-dev";

    public FileStorageService(IAmazonS3 s3, IConfiguration config, ILogger<FileStorageService> logger)
    {
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    private static FileType DetectFileType(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "text/html" => FileType.Html,
            var ct when ct.StartsWith("image/") => FileType.Image,
            "application/pdf" => FileType.Pdf,
            "text/plain" or "text/markdown" or "text/x-markdown" or "application/json" => FileType.Text,
            _ => FileType.Other
        };

    private static string ExtractHtmlText(string htmlContent)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(htmlContent);
        // Remove script and style nodes
        foreach (var node in doc.DocumentNode.SelectNodes("//script|//style") ?? Enumerable.Empty<HtmlNode>())
            node.Remove();
        return doc.DocumentNode.InnerText;
    }

    private static string ExtractPdfText(byte[] pdfBytes)
    {
        var sb = new StringBuilder();
        using var pdfDoc = PdfDocument.Open(pdfBytes);
        foreach (var page in pdfDoc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }

    public async Task<UploadedFile> UploadAsync(IBrowserFile file, string uploaderUpn)
    {
        if (file.Size > MaxFileSizeBytes)
            throw new InvalidOperationException($"File exceeds maximum size of 10MB. Actual size: {file.Size / 1024 / 1024}MB.");

        var normalizedContentType = file.ContentType.ToLowerInvariant();
        if (!AllowedTypes.Contains(normalizedContentType))
            throw new InvalidOperationException($"File type '{file.ContentType}' is not allowed. Accepted: HTML, PNG, JPG, JPEG, WEBP, PDF, MD, JSON, TXT.");

        var safeFileName = Path.GetFileName(file.Name);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new InvalidOperationException("Invalid filename.");

        var s3Key = $"nexus/{uploaderUpn}/{Guid.NewGuid()}/{safeFileName}";
        var fileType = DetectFileType(normalizedContentType);
        string? processedText = null;

        using var stream = file.OpenReadStream(MaxFileSizeBytes);
        var fileBytes = new byte[file.Size];
        await stream.ReadExactlyAsync(fileBytes);

        if (fileType == FileType.Html)
        {
            var htmlContent = Encoding.UTF8.GetString(fileBytes);
            processedText = ExtractHtmlText(htmlContent);
        }
        else if (fileType == FileType.Pdf)
        {
            try
            {
                processedText = ExtractPdfText(fileBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "NEXUS: PDF text extraction failed for {FileName}", file.Name);
                processedText = null;
            }
        }
        else if (fileType == FileType.Text || fileType == FileType.Other)
        {
            processedText = Encoding.UTF8.GetString(fileBytes);
        }
        // Image: processedText stays null (vision model handles it)

        using var uploadStream = new MemoryStream(fileBytes);
        var putRequest = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            InputStream = uploadStream,
            ContentType = file.ContentType,
            Metadata = { ["original-filename"] = file.Name, ["uploader-upn"] = uploaderUpn }
        };
        await _s3.PutObjectAsync(putRequest);

        _logger.LogInformation("NEXUS: Uploaded {FileName} ({FileType}) to S3 key {Key}", file.Name, fileType, s3Key);

        return new UploadedFile
        {
            OriginalFileName = safeFileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Size,
            S3Key = s3Key,
            S3Bucket = Bucket,
            UploadedBy = uploaderUpn,
            UploadedAt = DateTime.UtcNow,
            ProcessedText = processedText,
            FileType = fileType
        };
    }

    public async Task<Stream> DownloadAsync(string s3Key, string bucketName)
    {
        var response = await _s3.GetObjectAsync(bucketName, s3Key);
        return response.ResponseStream;
    }

    public async Task<string> GetPresignedUrlAsync(string s3Key, string bucketName, int expiryMinutes = 15)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucketName,
            Key = s3Key,
            Expires = DateTime.UtcNow.AddMinutes(expiryMinutes)
        };
        return await _s3.GetPreSignedURLAsync(request);
    }
}
