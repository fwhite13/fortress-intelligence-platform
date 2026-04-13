using Amazon.S3;
using Amazon.S3.Model;
using System.Text;
using System.Text.Json;

namespace FortressIntelligenceRM.Web.Services;

public class S3Service
{
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<S3Service> _logger;

    public S3Service(IAmazonS3 s3, IConfiguration config, ILogger<S3Service> logger)
    {
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    private string Bucket => _config["Firm:S3Bucket"] ?? "firm-recordings-dev";

    public async Task<string> GeneratePresignedUrlAsync(string s3Key, int expiryHours = 1)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            Expires = DateTime.UtcNow.AddHours(expiryHours)
        };
        return await _s3.GetPreSignedURLAsync(request);
    }

    public async Task<string> GetTranscriptTextAsync(string s3Key)
    {
        try
        {
            var response = await _s3.GetObjectAsync(Bucket, s3Key);
            using var reader = new StreamReader(response.ResponseStream);
            var json = await reader.ReadToEndAsync();
            // Parse transcript JSON and format as plain text
            var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            if (doc.RootElement.TryGetProperty("segments", out var segments))
            {
                foreach (var seg in segments.EnumerateArray())
                {
                    var speaker = seg.TryGetProperty("speaker_label", out var sl) ? sl.GetString() : "Unknown";
                    var text = seg.TryGetProperty("text", out var t) ? t.GetString() : "";
                    var startMs = seg.TryGetProperty("start_time_ms", out var sm) ? sm.GetInt64() : 0;
                    var ts = TimeSpan.FromMilliseconds(startMs);
                    sb.AppendLine($"[{ts:hh\\:mm\\:ss}] {speaker}: {text}");
                }
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to get transcript from S3: {Key}", s3Key);
            return "";
        }
    }

    public async Task<string> GetSummaryTextAsync(string s3Key)
    {
        try
        {
            var response = await _s3.GetObjectAsync(Bucket, s3Key);
            using var reader = new StreamReader(response.ResponseStream);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to get summary from S3: {Key}", s3Key);
            return "";
        }
    }

    public async Task<string> UploadTextAsync(string s3Key, string content, string contentType = "text/plain")
    {
        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            ContentBody = content,
            ContentType = contentType
        };
        await _s3.PutObjectAsync(request);
        _logger.LogInformation("FIRM: Uploaded text to S3: {Key}", s3Key);
        return s3Key;
    }
}
