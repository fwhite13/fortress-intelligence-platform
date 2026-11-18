using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
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
            JsonElement segmentsEl;
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // vpbot format: bare array
                segmentsEl = doc.RootElement;
            }
            else if (doc.RootElement.TryGetProperty("segments", out var wrapped))
            {
                // legacy wrapped format
                segmentsEl = wrapped;
            }
            else
            {
                return sb.ToString(); // unknown format, return empty
            }

            foreach (var seg in segmentsEl.EnumerateArray())
            {
                // Try both camelCase (vpbot) and snake_case (legacy)
                var speaker = TryGetString(seg, "speakerLabel") ?? TryGetString(seg, "speaker_label") ?? "Unknown";
                var text    = TryGetString(seg, "text") ?? "";
                var startMs = TryGetLong(seg, "startTimeMs") ?? TryGetLong(seg, "start_time_ms") ?? 0L;
                var ts = TimeSpan.FromMilliseconds(startMs);
                sb.AppendLine($"[{ts:hh\\:mm\\:ss}] {speaker}: {text}");
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

    private static string? TryGetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() : null;

    private static long? TryGetLong(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.TryGetInt64(out var n) ? n : null;

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

    public async Task<string> UploadStreamAsync(string s3Key, Stream stream, string contentType = "application/octet-stream")
    {
        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            InputStream = stream,
            ContentType = contentType
        };
        await _s3.PutObjectAsync(request);
        _logger.LogInformation("FIRM: Uploaded stream to S3: {Key}", s3Key);
        return s3Key;
    }

    /// <summary>WI #9131 — uploads a (possibly large) recording with TransferUtility, which switches to
    /// S3 multipart upload above its threshold, instead of a single PutObject. Used by MeetingIngestService.</summary>
    public async Task<string> UploadLargeStreamAsync(string s3Key, Stream stream, string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default)
    {
        using var transfer = new TransferUtility(_s3);
        var request = new TransferUtilityUploadRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            InputStream = stream,
            ContentType = contentType,
            PartSize = 16 * 1024 * 1024,
            AutoCloseStream = false
        };
        await transfer.UploadAsync(request, cancellationToken);
        _logger.LogInformation("FIRM: Uploaded stream to S3 (multipart-capable): {Key}", s3Key);
        return s3Key;
    }

    // ── WI #9139: S3 multipart helpers for the browser chunked upload (/upload). ──────────────────────
    // These deliberately log nothing: the temporary keys contain a sanitised file name, and the caller
    // (RecordingUploadService) logs upload id, byte counts and status only.

    /// <summary>Starts a multipart upload and returns S3's UploadId (server-side only; never sent to the browser).</summary>
    public async Task<string> CreateMultipartUploadAsync(string s3Key, string contentType, CancellationToken cancellationToken = default)
    {
        var response = await _s3.InitiateMultipartUploadAsync(new InitiateMultipartUploadRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            ContentType = contentType
        }, cancellationToken);
        return response.UploadId;
    }

    /// <summary>Uploads one part (1-based). The stream must be seekable with a known length.</summary>
    public async Task<string> UploadPartAsync(string s3Key, string s3UploadId, int partNumber, Stream content,
        CancellationToken cancellationToken = default)
    {
        var response = await _s3.UploadPartAsync(new UploadPartRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            UploadId = s3UploadId,
            PartNumber = partNumber,
            PartSize = content.Length,
            InputStream = content
        }, cancellationToken);
        return response.ETag;
    }

    /// <summary>Completes a multipart upload from the given parts.</summary>
    public async Task CompleteMultipartUploadAsync(string s3Key, string s3UploadId,
        IEnumerable<(int PartNumber, string ETag)> parts, CancellationToken cancellationToken = default)
    {
        var request = new CompleteMultipartUploadRequest { BucketName = Bucket, Key = s3Key, UploadId = s3UploadId };
        request.AddPartETags(parts.OrderBy(p => p.PartNumber).Select(p => new PartETag(p.PartNumber, p.ETag)));
        await _s3.CompleteMultipartUploadAsync(request, cancellationToken);
    }

    /// <summary>Aborts a multipart upload; a NoSuchUpload (already completed/aborted) is ignored.</summary>
    public async Task AbortMultipartUploadAsync(string s3Key, string s3UploadId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _s3.AbortMultipartUploadAsync(new AbortMultipartUploadRequest
            {
                BucketName = Bucket,
                Key = s3Key,
                UploadId = s3UploadId
            }, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode == "NoSuchUpload")
        {
        }
    }

    /// <summary>Server-side copy within the recordings bucket (single CopyObject; fine up to 5 GB).</summary>
    public async Task CopyObjectAsync(string sourceKey, string destinationKey, string? contentType = null,
        CancellationToken cancellationToken = default)
    {
        var request = new CopyObjectRequest
        {
            SourceBucket = Bucket,
            SourceKey = sourceKey,
            DestinationBucket = Bucket,
            DestinationKey = destinationKey
        };
        if (!string.IsNullOrEmpty(contentType))
        {
            request.ContentType = contentType;
            request.MetadataDirective = S3MetadataDirective.REPLACE;
        }
        await _s3.CopyObjectAsync(request, cancellationToken);
    }

    /// <summary>Object size in bytes, or null when the object does not exist.</summary>
    public async Task<long?> GetObjectSizeAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            var meta = await _s3.GetObjectMetadataAsync(Bucket, s3Key, cancellationToken);
            return meta.ContentLength;
        }
        catch (AmazonS3Exception ex) when (IsMissing(ex))
        {
            return null;
        }
    }

    /// <summary>A missing key. Without s3:ListBucket (the FIRM task role is documented with object-level actions
    /// only), S3 answers GET/HEAD on a missing key with 403 AccessDenied instead of 404, so both mean "absent".</summary>
    private static bool IsMissing(AmazonS3Exception ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden;

    /// <summary>Deletes an object (S3 delete is idempotent).</summary>
    public async Task DeleteObjectAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        await _s3.DeleteObjectAsync(Bucket, s3Key, cancellationToken);
    }

    /// <summary>S3 DeleteObjects accepts at most 1000 keys per call.</summary>
    public const int DeleteBatchSize = 1000;

    /// <summary>Shared prefixes that must never be bulk-deleted (voiceprints, enrollment samples, bot auth state,
    /// in-flight chunked uploads). DeletePrefixAsync refuses any prefix under these.</summary>
    private static readonly string[] ProtectedPrefixes = { "voiceprints/", "voice-samples/", "auth/", "firm-uploads/" };

    /// <summary>
    /// WI #9679 — true when <paramref name="prefix"/> is safe for <see cref="DeletePrefixAsync"/>: not empty, ends
    /// with '/', its last segment is a numeric (meeting) id, and it is not under a protected shared prefix. The
    /// trailing '/' is what stops "firm-audio/24/" from matching "firm-audio/240/".
    /// </summary>
    public static bool IsValidMeetingPrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix) || !prefix.EndsWith('/') || prefix.StartsWith('/')) return false;
        if (prefix.Contains("//")) return false;
        foreach (var p in ProtectedPrefixes)
            if (prefix.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return false;
        var segments = prefix.TrimEnd('/').Split('/');
        if (segments.Length < 2) return false; // a bare "{id}/" has no artifact class in front of it
        var idSegment = segments[^1];
        return idSegment.Length > 0 && idSegment.All(char.IsAsciiDigit) && long.TryParse(idSegment, out var id) && id > 0;
    }

    /// <summary>
    /// WI #9679 — deletes every object under <paramref name="prefix"/> (paged ListObjectsV2, then DeleteObjects in
    /// batches of at most <see cref="DeleteBatchSize"/>). Refuses (ArgumentException) any prefix that fails
    /// <see cref="IsValidMeetingPrefix"/>. Returns the number of keys deleted. Throws when a list or delete call
    /// fails or S3 reports per-key delete errors; the caller decides whether that is fatal. Logs the prefix and
    /// counts only, never the keys.
    /// </summary>
    public async Task<int> DeletePrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (!IsValidMeetingPrefix(prefix))
            throw new ArgumentException("Prefix must end with '/' and end in a numeric meeting id segment, and must not be a shared prefix.", nameof(prefix));

        var deleted = 0;
        var failed = 0;
        string? firstErrorCode = null;
        var batch = new List<KeyVersion>(DeleteBatchSize);

        async Task FlushAsync()
        {
            if (batch.Count == 0) return;
            var response = await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = Bucket,
                Objects = batch.ToList(),
                Quiet = true // only errors come back
            }, cancellationToken);
            var errors = response.DeleteErrors?.Count ?? 0;
            if (errors > 0)
            {
                failed += errors;
                firstErrorCode ??= response.DeleteErrors![0].Code;
            }
            deleted += batch.Count - errors;
            batch.Clear();
        }

        string? continuationToken = null;
        do
        {
            var page = await _s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = Bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
                MaxKeys = DeleteBatchSize
            }, cancellationToken);

            foreach (var obj in page.S3Objects ?? new List<S3Object>())
            {
                // Defence in depth: ListObjectsV2 with Prefix only returns keys under it.
                if (!obj.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                batch.Add(new KeyVersion { Key = obj.Key });
                if (batch.Count >= DeleteBatchSize) await FlushAsync();
            }

            continuationToken = page.IsTruncated == true ? page.NextContinuationToken : null;
        } while (!string.IsNullOrEmpty(continuationToken));

        await FlushAsync();

        if (failed > 0)
            throw new InvalidOperationException($"S3 DeleteObjects reported {failed} failed key(s) under the prefix (first error code: {firstErrorCode}); {deleted} deleted.");

        _logger.LogInformation("FIRM: Deleted {Count} S3 object(s) under prefix {Prefix}", deleted, prefix);
        return deleted;
    }

    /// <summary>Reads a small JSON object with its ETag and last-modified time; null when it does not exist.</summary>
    public async Task<(string Json, string ETag, DateTime LastModifiedUtc)?> GetSmallObjectAsync(string s3Key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _s3.GetObjectAsync(Bucket, s3Key, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            var body = await reader.ReadToEndAsync(cancellationToken);
            var lastModified = response.LastModified is DateTime lm ? lm.ToUniversalTime() : DateTime.UtcNow;
            return (body, response.ETag, lastModified);
        }
        catch (AmazonS3Exception ex) when (IsMissing(ex))
        {
            return null;
        }
    }

    /// <summary>Writes a small JSON object. <paramref name="ifNoneMatch"/> = "*" creates it only if absent;
    /// <paramref name="ifMatch"/> overwrites only that ETag. Returns false when the condition failed
    /// (412 PreconditionFailed or 409 ConditionalRequestConflict), true when written.</summary>
    public async Task<bool> PutSmallObjectAsync(string s3Key, string json, string? ifNoneMatch = null, string? ifMatch = null,
        CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = s3Key,
            ContentBody = json,
            ContentType = "application/json"
        };
        if (ifNoneMatch != null) request.IfNoneMatch = ifNoneMatch;
        if (ifMatch != null) request.IfMatch = ifMatch;
        try
        {
            await _s3.PutObjectAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when ((ifNoneMatch != null || ifMatch != null)
                                           && ex.StatusCode is System.Net.HttpStatusCode.PreconditionFailed
                                                            or System.Net.HttpStatusCode.Conflict)
        {
            return false;
        }
    }
}
