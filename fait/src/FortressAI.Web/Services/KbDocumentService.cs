using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Shared.Models;
using System.Text.Json;

namespace FortressAI.Web.Services;

public class KbDocumentService
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonBedrockAgent _bedrockAgent;
    private readonly IConfiguration _config;
    private readonly ILogger<KbDocumentService> _logger;
    private readonly KbSyncRetryService _syncRetryService;

    private const string BucketName = "fortress-tools";

    public KbDocumentService(IAmazonS3 s3, IAmazonBedrockAgent bedrockAgent, IConfiguration config, ILogger<KbDocumentService> logger, KbSyncRetryService syncRetryService)
    {
        _s3 = s3;
        _bedrockAgent = bedrockAgent;
        _config = config;
        _logger = logger;
        _syncRetryService = syncRetryService;
    }

    /// <summary>Upload document to S3 + write companion .metadata.json. Returns the S3 key.</summary>
    public async Task<string> UploadDocumentAsync(Stream fileStream, string filename, string contentType, KbTier tier, Guid userId, int? teamId = null)
    {
        var safeFilename = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeFilename))
            throw new ArgumentException("Invalid filename.", nameof(filename));

        var key = tier == KbTier.Team
            ? $"kb-docs/teams/{teamId}/{safeFilename}"
            : $"kb-docs/personal/{userId}/{safeFilename}";

        // Upload document
        var putReq = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType
        };
        await _s3.PutObjectAsync(putReq);
        _logger.LogInformation("Uploaded KB document to s3://{Bucket}/{Key}", BucketName, key);

        // Write metadata file — omit teamId entirely for personal tier (empty string breaks Bedrock KB filtering)
        var metadataDict = new Dictionary<string, object>
        {
            ["tier"] = tier == KbTier.Team ? "team" : "personal",
            ["ownerId"] = userId.ToString()
        };
        if (teamId.HasValue)
            metadataDict["teamId"] = teamId.Value.ToString();
        var metadata = new { metadataAttributes = metadataDict };
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var metaPutReq = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = $"{key}.metadata.json",
            ContentBody = metadataJson,
            ContentType = "application/json"
        };
        await _s3.PutObjectAsync(metaPutReq);

        return key;
    }

    /// <summary>Upload project document to S3 + write metadata. Returns the S3 key.</summary>
    public async Task<string> UploadProjectDocumentAsync(Stream fileStream, string filename, string contentType, Guid projectId, Guid userId)
    {
        var safeFilename = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeFilename))
            throw new ArgumentException("Invalid filename.", nameof(filename));

        var key = $"kb-docs/project/{projectId}/{safeFilename}";

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            InputStream = fileStream,
            ContentType = contentType
        });

        _logger.LogInformation("Uploaded project KB document to s3://{Bucket}/{Key}", BucketName, key);

        // Write metadata — kbType=project + projectId for Bedrock filtering
        var metadataDict = new Dictionary<string, object>
        {
            ["kbType"] = "project",
            ["projectId"] = projectId.ToString(),
            ["ownerId"] = userId.ToString()
        };
        var metadata = new { metadataAttributes = metadataDict };
        var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = $"{key}.metadata.json",
            ContentBody = metadataJson,
            ContentType = "application/json"
        });

        return key;
    }

    /// <summary>Delete a document from S3 (also deletes .metadata.json companion).</summary>
    public async Task DeleteDocumentAsync(string s3Key)
    {
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = s3Key });
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = $"{s3Key}.metadata.json" });
        _logger.LogInformation("Deleted KB document s3://{Bucket}/{Key}", BucketName, s3Key);
    }

    /// <summary>Trigger Bedrock KB ingestion sync. Returns the ingestion job ID on success, null otherwise.</summary>
    /// <param name="throwOnConflict">
    /// When true, re-throws ConflictException instead of swallowing it.
    /// Used by KbSyncRetryService so the retry loop can detect a still-busy ingestion
    /// and keep _retryNeeded = true for the next cycle.
    /// </param>
    public async Task<string?> StartIngestionAsync(bool throwOnConflict = false)
    {
        var kbId = _config["KnowledgeBase:PersonalTeamKbId"];
        var dsId = _config["KnowledgeBase:PersonalDataSourceId"];

        if (string.IsNullOrEmpty(kbId) || string.IsNullOrEmpty(dsId))
        {
            _logger.LogDebug("PersonalTeamKbId or PersonalDataSourceId not configured — skipping ingestion trigger");
            return null;
        }

        try
        {
            var response = await _bedrockAgent.StartIngestionJobAsync(new StartIngestionJobRequest
            {
                KnowledgeBaseId = kbId,
                DataSourceId = dsId
            });
            var jobId = response.IngestionJob?.IngestionJobId;
            _logger.LogInformation("Started KB ingestion job {JobId} for KB {KbId}", jobId, kbId);
            if (jobId != null)
                _syncRetryService.EnqueueJobForPolling(jobId, DateTime.UtcNow);
            return jobId;
        }
        catch (Amazon.BedrockAgent.Model.ConflictException)
        {
            if (throwOnConflict)
            {
                _logger.LogInformation("Ingestion already in progress (throwOnConflict=true) — re-throwing for retry loop");
                throw;
            }
            _logger.LogInformation("Ingestion already in progress — queued for automatic retry");
            _syncRetryService.RequestRetry();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start KB ingestion job — documents will sync on next scheduled ingestion");
            return null;
        }
    }

    /// <summary>Poll the status of a Bedrock ingestion job. Returns Bedrock status string (COMPLETE, FAILED, IN_PROGRESS, etc.) or "UNKNOWN" on error.</summary>
    public async Task<string> PollIngestionJobAsync(string jobId)
    {
        var kbId = _config["KnowledgeBase:PersonalTeamKbId"];
        var dsId = _config["KnowledgeBase:PersonalDataSourceId"];
        try
        {
            var response = await _bedrockAgent.GetIngestionJobAsync(new GetIngestionJobRequest
            {
                KnowledgeBaseId = kbId,
                DataSourceId = dsId,
                IngestionJobId = jobId
            });
            return response.IngestionJob?.Status?.Value ?? "UNKNOWN";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to poll ingestion job {JobId}", jobId);
            return "UNKNOWN";
        }
    }

    /// <summary>
    /// Scan personal KB metadata files and remove empty teamId attributes.
    /// One-time repair for documents uploaded before the metadata fix.
    /// </summary>
    public async Task RepairPersonalKbMetadataAsync()
    {
        try
        {
            var prefix = "kb-docs/personal/";
            var listReq = new ListObjectsV2Request { BucketName = BucketName, Prefix = prefix };
            ListObjectsV2Response listResp;
            do
            {
                listResp = await _s3.ListObjectsV2Async(listReq);
                foreach (var obj in listResp.S3Objects.Where(o => o.Key.EndsWith(".metadata.json")))
                {
                    var getResp = await _s3.GetObjectAsync(new GetObjectRequest { BucketName = BucketName, Key = obj.Key });
                    using var reader = new StreamReader(getResp.ResponseStream);
                    var json = await reader.ReadToEndAsync();
                    using var doc = JsonDocument.Parse(json);
                    var attrs = doc.RootElement.GetProperty("metadataAttributes");
                    if (attrs.TryGetProperty("teamId", out var teamIdProp) && teamIdProp.GetString() == "")
                    {
                        // Rebuild without empty teamId
                        var newAttrs = new Dictionary<string, object>();
                        foreach (var prop in attrs.EnumerateObject())
                        {
                            if (prop.Name == "teamId") continue;
                            newAttrs[prop.Name] = prop.Value.GetString() ?? "";
                        }
                        var newMetadata = new { metadataAttributes = newAttrs };
                        var newJson = JsonSerializer.Serialize(newMetadata, new JsonSerializerOptions { WriteIndented = true });
                        await _s3.PutObjectAsync(new PutObjectRequest
                        {
                            BucketName = BucketName,
                            Key = obj.Key,
                            ContentBody = newJson,
                            ContentType = "application/json"
                        });
                        _logger.LogInformation("Repaired metadata for {Key}", obj.Key);
                    }
                }
                listReq.ContinuationToken = listResp.NextContinuationToken;
            } while (listResp.IsTruncated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Metadata repair scan failed — non-fatal");
        }
    }

    /// <summary>List documents in S3 for a user or team (excludes .metadata.json files).</summary>
    public async Task<List<KbDocumentInfo>> ListDocumentsAsync(KbTier tier, Guid userId, int? teamId = null)
    {
        var prefix = tier == KbTier.Team
            ? $"kb-docs/teams/{teamId}/"
            : $"kb-docs/personal/{userId}/";

        var listReq = new ListObjectsV2Request
        {
            BucketName = BucketName,
            Prefix = prefix
        };

        var docs = new List<KbDocumentInfo>();
        try
        {
            ListObjectsV2Response response;
            do
            {
                response = await _s3.ListObjectsV2Async(listReq);
                foreach (var obj in response.S3Objects)
                {
                    // Skip companion metadata files
                    if (obj.Key.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase))
                        continue;

                    docs.Add(new KbDocumentInfo
                    {
                        S3Key = obj.Key,
                        Filename = obj.Key.Split('/').Last(),
                        Size = obj.Size,
                        LastModified = obj.LastModified
                    });
                }
                listReq.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list KB documents from S3 prefix {Prefix}", prefix);
        }

        return docs;
    }
}

public class KbDocumentInfo
{
    public string S3Key { get; set; } = "";
    public string Filename { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string IngestionStatus { get; set; } = "pending";  // "pending" | "ingested" | "failed"
}
