using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FortressAI.Web.Services;

public class KbDocumentService
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonBedrockAgent _bedrockAgent;
    private readonly IConfiguration _config;
    private readonly ILogger<KbDocumentService> _logger;
    private readonly KbSyncRetryService _syncRetryService;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    private const string BucketName = "fortress-tools";

    // Config keys read per KB type — KB IDs needed for polling ingestion job status
    private string CorpKbId => _config["KnowledgeBase:CorpKbId"] ?? "";
    private string PersonalKbId => _config["KnowledgeBase:PersonalKbId"] ?? "";
    private string TeamKbId => _config["KnowledgeBase:TeamKbId"] ?? "";
    private string ProjectKbId => _config["KnowledgeBase:ProjectKbId"] ?? "";

    // Data source IDs per KB (Fred will provide these; empty = skip ingestion trigger)
    private string PersonalDataSourceId => _config["KnowledgeBase:PersonalDataSourceId"] ?? "";
    private string TeamDataSourceId => _config["KnowledgeBase:TeamDataSourceId"] ?? "";
    private string ProjectDataSourceId => _config["KnowledgeBase:ProjectDataSourceId"] ?? "";
    private string CorpDataSourceId => _config["KnowledgeBase:CorpDataSourceId"] ?? "";

    public KbDocumentService(IAmazonS3 s3, IAmazonBedrockAgent bedrockAgent, IConfiguration config, ILogger<KbDocumentService> logger, KbSyncRetryService syncRetryService, IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _s3 = s3;
        _bedrockAgent = bedrockAgent;
        _config = config;
        _logger = logger;
        _syncRetryService = syncRetryService;
        _dbContextFactory = dbContextFactory;
    }

    /// <summary>Upload document to S3 + write companion .metadata.json. Returns the S3 key.</summary>
    public async Task<string> UploadDocumentAsync(Stream fileStream, string filename, string contentType, KbTier tier, Guid userId, int? teamId = null)
    {
        var safeFilename = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeFilename))
            throw new ArgumentException("Invalid filename.", nameof(filename));

        var key = tier switch
        {
            KbTier.Team      => $"kb-docs/teams/{teamId}/{safeFilename}",
            KbTier.Corporate => $"kb-docs/fortress/{safeFilename}",
            _                => $"kb-docs/personal/{userId}/{safeFilename}"
        };

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

        // Write metadata companion file (not needed for Corp KB — structural isolation)
        if (tier != KbTier.Corporate)
        {
            var metadataDict = tier == KbTier.Team
                ? new Dictionary<string, object> { ["teamId"] = teamId!.Value.ToString() }
                : new Dictionary<string, object> { ["ownerId"] = userId.ToString() };
            var metadata = new { metadataAttributes = metadataDict };
            var metadataJson = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = $"{key}.metadata.json",
                ContentBody = metadataJson,
                ContentType = "application/json"
            });
        }

        // Track upload in DB for ingestion status monitoring
        // ProjectId = null for personal/team/corp uploads (project uploads tracked by DocumentService)
        try
        {
            await using var trackDb = await _dbContextFactory.CreateDbContextAsync();
            trackDb.ProjectDocuments.Add(new FortressAI.Shared.Models.ProjectDocument
            {
                Id = Guid.NewGuid(),
                ProjectId = null,   // not a project document
                Filename = safeFilename,
                S3Key = key,
                FileSize = 0,       // not tracked for KB uploads — size not available at this point
                IngestionStatus = "pending",
                UploadedAt = DateTime.UtcNow
            });
            await trackDb.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create KB document tracking row for {Key} — non-fatal", key);
        }

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

        // Write metadata — structural isolation: Project KB contains ONLY project docs.
        // Only projectId needed for within-KB filtering. No kbType or ownerId needed.
        var metadata = new { metadataAttributes = new Dictionary<string, object> { ["projectId"] = projectId.ToString() } };
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

    /// <summary>Trigger Bedrock KB ingestion sync for the specified KB tier. Returns the ingestion job ID on success, null otherwise.</summary>
    /// <param name="tier">Which KB to trigger ingestion for. Defaults to Personal for backward compatibility.</param>
    /// <param name="throwOnConflict">
    /// When true, re-throws ConflictException instead of swallowing it.
    /// Used by KbSyncRetryService so the retry loop can detect a still-busy ingestion
    /// and keep _retryNeeded = true for the next cycle.
    /// </param>
    /// <remarks>
    /// Note: Project KB uploads are triggered via UploadProjectDocumentAsync → StartProjectIngestionAsync.
    /// KbTier enum only covers Personal/Team/Corporate — project docs have their own upload path.
    /// </remarks>
    public async Task<string?> StartIngestionAsync(KbTier tier = KbTier.Personal, bool throwOnConflict = false)
    {
        var (kbId, dsId) = tier switch
        {
            KbTier.Personal   => (PersonalKbId, PersonalDataSourceId),
            KbTier.Team       => (TeamKbId, TeamDataSourceId),
            KbTier.Corporate  => (CorpKbId, CorpDataSourceId),
            _                 => (PersonalKbId, PersonalDataSourceId)
        };

        if (string.IsNullOrEmpty(kbId) || string.IsNullOrEmpty(dsId))
        {
            _logger.LogDebug("{Tier} KbId or DataSourceId not configured — skipping ingestion trigger", tier);
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
            _logger.LogInformation("Started {Tier} KB ingestion job {JobId} for KB {KbId}", tier, jobId, kbId);
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
    public async Task<string> PollIngestionJobAsync(string jobId, string kbId, string dsId)
    {
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

    /// <summary>Convenience overload — defaults to Personal KB for backward compatibility with KbSyncRetryService.
    /// TODO: Update KbSyncRetryService to store KB type alongside job ID for multi-KB ingestion polling support.</summary>
    public async Task<string> PollIngestionJobAsync(string jobId)
        => await PollIngestionJobAsync(jobId, PersonalKbId, PersonalDataSourceId);

    /// <summary>Trigger ingestion for Project KB specifically. Called from UploadProjectDocumentAsync path.</summary>
    public async Task<string?> StartProjectIngestionAsync(bool throwOnConflict = false)
    {
        var kbId = ProjectKbId;
        var dsId = ProjectDataSourceId;
        if (string.IsNullOrEmpty(kbId) || string.IsNullOrEmpty(dsId))
        {
            _logger.LogDebug("ProjectKbId or ProjectDataSourceId not configured — skipping ingestion trigger");
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
            _logger.LogInformation("Started project KB ingestion job {JobId} for KB {KbId}", jobId, kbId);
            if (jobId != null)
                _syncRetryService.EnqueueJobForPolling(jobId, DateTime.UtcNow);
            return jobId;
        }
        catch (Amazon.BedrockAgent.Model.ConflictException)
        {
            if (throwOnConflict)
            {
                _logger.LogInformation("Project ingestion already in progress (throwOnConflict=true) — re-throwing for retry loop");
                throw;
            }
            _logger.LogInformation("Project ingestion already in progress — queued for automatic retry");
            _syncRetryService.RequestRetry();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start project KB ingestion job — documents will sync on next scheduled ingestion");
            return null;
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

        // Look up actual ingestion status from DB — the project_documents table is updated by
        // KbSyncRetryService when Bedrock ingestion completes. S3 listing alone never returns
        // the updated status; this join is the primary fix for the 'always Processing' chip bug.
        if (docs.Any())
        {
            var s3Keys = docs.Select(d => d.S3Key).ToList();
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var statusMap = await db.ProjectDocuments
                .Where(pd => pd.S3Key != null && s3Keys.Contains(pd.S3Key))
                .ToDictionaryAsync(pd => pd.S3Key!, pd => pd.IngestionStatus);
            foreach (var doc in docs)
            {
                if (statusMap.TryGetValue(doc.S3Key, out var status))
                    doc.IngestionStatus = string.IsNullOrEmpty(status) ? "pending" : status;
            }
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
