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
    private string DevKbId => _config["KnowledgeBase:DevKbId"] ?? "";

    // Data source IDs per KB (Fred will provide these; empty = skip ingestion trigger)
    private string PersonalDataSourceId => _config["KnowledgeBase:PersonalDataSourceId"] ?? "";
    private string TeamDataSourceId => _config["KnowledgeBase:TeamDataSourceId"] ?? "";
    private string ProjectDataSourceId => _config["KnowledgeBase:ProjectDataSourceId"] ?? "";
    private string CorpDataSourceId => _config["KnowledgeBase:CorpDataSourceId"] ?? "";
    private string DevDataSourceId => _config["KnowledgeBase:DevDataSourceId"] ?? "";

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

        // Auto-convert PPTX — Bedrock KB does not support .pptx natively; convert to PDF via LibreOffice headless
        Stream uploadStream = fileStream;
        if (safeFilename.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("PPTX detected — converting to PDF via LibreOffice: {Filename}", safeFilename);
            var pdfBytes = await ConvertPptxToPdfAsync(fileStream, safeFilename, _logger);

            if (pdfBytes != null)
            {
                var convertedFilename = Path.ChangeExtension(safeFilename, ".pdf");
                uploadStream = new MemoryStream(pdfBytes);
                safeFilename = convertedFilename;
                contentType = "application/pdf";
                _logger.LogInformation("PPTX converted to PDF: {Filename} ({Bytes} bytes)", convertedFilename, pdfBytes.Length);
            }
            else
            {
                _logger.LogWarning("PPTX→PDF conversion failed — uploading original PPTX (Bedrock may not ingest it)");
                // Fall through — upload original PPTX as-is; Bedrock will skip it but file is preserved in S3
            }
        }

        var key = tier switch
        {
            KbTier.Team      => $"kb-docs/teams/{teamId}/{safeFilename}",
            KbTier.Corporate => $"kb-docs/fortress/{safeFilename}",
            KbTier.Developer => $"kb-docs/dev/{safeFilename}",
            _                => $"kb-docs/personal/{userId}/{safeFilename}"
        };

        // Upload document
        var putReq = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            InputStream = uploadStream,
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

        // Track upload in DB for ingestion status monitoring (upsert — prevents duplicate S3Key on re-upload)
        // ProjectId = null for personal/team/corp uploads (project uploads tracked by DocumentService)
        try
        {
            await using var trackDb = await _dbContextFactory.CreateDbContextAsync();
            var existingRow = await trackDb.ProjectDocuments
                .FirstOrDefaultAsync(pd => pd.S3Key == key);

            if (existingRow != null)
            {
                // Re-upload of same file — update existing row instead of inserting duplicate
                existingRow.Filename = safeFilename;
                existingRow.IngestionStatus = "pending";
                existingRow.UploadedAt = DateTime.UtcNow;
                trackDb.ProjectDocuments.Update(existingRow);
                _logger.LogInformation("[KbDocumentService] Updated existing DB tracking row for re-upload S3Key={Key}", key);
            }
            else
            {
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
            }
            await trackDb.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upsert KB document tracking row for {Key} — non-fatal", key);
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

    /// <summary>Delete a document from S3 (also deletes .metadata.json companion) and removes the DB tracking row.</summary>
    public async Task DeleteDocumentAsync(string s3Key)
    {
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = s3Key });
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = $"{s3Key}.metadata.json" });
        _logger.LogInformation("Deleted KB document s3://{Bucket}/{Key}", BucketName, s3Key);

        // Remove DB tracking row(s) — prevents duplicate-S3Key bug on re-upload
        // Delete ALL rows for this S3Key (handles any pre-existing duplicates too)
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var rows = await db.ProjectDocuments
                .Where(pd => pd.S3Key == s3Key)
                .ToListAsync();
            if (rows.Any())
            {
                db.ProjectDocuments.RemoveRange(rows);
                await db.SaveChangesAsync();
                _logger.LogInformation("[KbDocumentService] Removed {Count} DB tracking row(s) for S3Key={S3Key}", rows.Count, s3Key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KbDocumentService] Failed to remove DB tracking row for S3Key={S3Key} — non-fatal, stale row may remain", s3Key);
        }
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
            KbTier.Developer  => (DevKbId, DevDataSourceId),
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
        // Guard: avoid pointless S3 call with empty userId for Personal tier
        if (userId == Guid.Empty && tier == KbTier.Personal) return new();

        var prefix = tier switch
        {
            KbTier.Team      => $"kb-docs/teams/{teamId}/",
            KbTier.Developer => "kb-docs/dev/",
            _                => $"kb-docs/personal/{userId}/"
        };

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
            _logger.LogInformation("ListDocumentsAsync: tier={Tier} userId={UserId} prefix={Prefix} → found {Count} objects",
                tier, userId, prefix, docs.Count);
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
            // Build deduplicated status map — warn if duplicates detected (indicates stale rows from delete without DB cleanup)
            var dbRows = await db.ProjectDocuments
                .Where(pd => pd.S3Key != null && s3Keys.Contains(pd.S3Key))
                .ToListAsync();

            var grouped = dbRows.GroupBy(r => r.S3Key!).ToList();
            var duplicates = grouped.Where(g => g.Count() > 1).ToList();
            if (duplicates.Any())
            {
                foreach (var dup in duplicates)
                    _logger.LogWarning("[KbDocumentService] Duplicate S3Key detected: {S3Key} ({Count} rows) — keeping most recent to avoid Dictionary collision", dup.Key, dup.Count());
            }

            var statusMap = grouped
                .Select(g => g.OrderByDescending(r => r.UploadedAt).First())
                .ToDictionary(r => r.S3Key!, r => r.IngestionStatus);
            foreach (var doc in docs)
            {
                if (statusMap.TryGetValue(doc.S3Key, out var status))
                    doc.IngestionStatus = string.IsNullOrEmpty(status) ? "pending" : status;
            }
        }

        return docs;
    }

    /// <summary>Convert a PPTX stream to PDF bytes using LibreOffice headless.</summary>
    private static async Task<byte[]?> ConvertPptxToPdfAsync(Stream pptxStream, string filename, ILogger logger)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var inputPath = Path.Combine(tmpDir, filename);
        var pdfPath = Path.ChangeExtension(inputPath, ".pdf");

        try
        {
            // Write PPTX to temp file
            await using (var fs = new FileStream(inputPath, FileMode.Create, FileAccess.Write))
                await pptxStream.CopyToAsync(fs);

            // Run LibreOffice headless conversion
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "libreoffice",
                Arguments = $"--headless --convert-to pdf --outdir \"{tmpDir}\" \"{inputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start LibreOffice process");
            // Read stderr concurrently to prevent deadlock on large error output
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var stderr = await stderrTask;

            if (proc.ExitCode != 0)
            {
                logger.LogWarning("LibreOffice conversion failed (exit {Code}): {Err}", proc.ExitCode, stderr);
                return null;
            }

            if (!File.Exists(pdfPath))
            {
                logger.LogWarning("LibreOffice did not produce expected output: {Path}", pdfPath);
                return null;
            }

            return await File.ReadAllBytesAsync(pdfPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PPTX→PDF conversion failed — will skip upload");
            return null;
        }
        finally
        {
            // Clean up temp directory
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }
}

public class KbDocumentInfo
{
    public string S3Key { get; set; } = "";
    public string Filename { get; set; } = "";
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string IngestionStatus { get; set; } = "ingested";  // default to ingested — no tracking row means it was uploaded before tracking was added
}
