using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public class KbDocumentService
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonBedrockAgent _bedrockAgent;
    private readonly IConfiguration _config;
    private readonly ILogger<KbDocumentService> _logger;
    private readonly KbSyncRetryService _syncRetryService;

    private const string BucketName = "fortress-tools";

    private string CorpKbId => _config["KnowledgeBase:CorpKbId"] ?? "";
    private string PersonalKbId => _config["KnowledgeBase:PersonalKbId"] ?? "";
    private string TeamKbId => _config["KnowledgeBase:TeamKbId"] ?? "";
    private string ProjectKbId => _config["KnowledgeBase:ProjectKbId"] ?? "";
    private string DevKbId => _config["KnowledgeBase:DevKbId"] ?? "";

    private string PersonalDataSourceId => _config["KnowledgeBase:PersonalDataSourceId"] ?? "";
    private string TeamDataSourceId => _config["KnowledgeBase:TeamDataSourceId"] ?? "";
    private string ProjectDataSourceId => _config["KnowledgeBase:ProjectDataSourceId"] ?? "";
    private string CorpDataSourceId => _config["KnowledgeBase:CorpDataSourceId"] ?? "";
    private string DevDataSourceId => _config["KnowledgeBase:DevDataSourceId"] ?? "";

    public KbDocumentService(IAmazonS3 s3, IAmazonBedrockAgent bedrockAgent, IConfiguration config, ILogger<KbDocumentService> logger, KbSyncRetryService syncRetryService)
    {
        _s3 = s3;
        _bedrockAgent = bedrockAgent;
        _config = config;
        _logger = logger;
        _syncRetryService = syncRetryService;
    }

    public async Task<string> UploadDocumentAsync(Stream fileStream, string filename, string contentType, KbTier tier, string userId, string? teamId = null)
    {
        var safeFilename = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeFilename))
            throw new ArgumentException("Invalid filename.", nameof(filename));

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
                _logger.LogWarning("PPTX→PDF conversion failed — uploading original PPTX");
            }
        }

        var key = tier switch
        {
            KbTier.Team      => $"kb-docs/teams/{teamId}/{safeFilename}",
            KbTier.Corporate => $"kb-docs/fortress/{safeFilename}",
            KbTier.Developer => $"kb-docs/dev/{safeFilename}",
            _                => $"kb-docs/personal/{userId}/{safeFilename}"
        };

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            InputStream = uploadStream,
            ContentType = contentType
        });
        _logger.LogInformation("Uploaded KB document to s3://{Bucket}/{Key}", BucketName, key);

        if (tier != KbTier.Corporate)
        {
            var metadataDict = tier == KbTier.Team
                ? new Dictionary<string, object> { ["teamId"] = teamId! }
                : new Dictionary<string, object> { ["ownerId"] = userId };
            var metadata = new { metadataAttributes = metadataDict };
            var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = BucketName,
                Key = $"{key}.metadata.json",
                ContentBody = metadataJson,
                ContentType = "application/json"
            });
        }

        return key;
    }

    public async Task<string> UploadProjectDocumentAsync(Stream fileStream, string filename, string contentType, string projectId, string userId)
    {
        var safeFilename = Path.GetFileName(filename);
        if (string.IsNullOrEmpty(safeFilename))
            throw new ArgumentException("Invalid filename.", nameof(filename));

        Stream uploadStream = fileStream;
        if (safeFilename.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("PPTX detected (project) — converting to PDF via LibreOffice: {Filename}", safeFilename);
            var pdfBytes = await ConvertPptxToPdfAsync(fileStream, safeFilename, _logger);
            if (pdfBytes != null)
            {
                var convertedFilename = Path.ChangeExtension(safeFilename, ".pdf");
                uploadStream = new MemoryStream(pdfBytes);
                safeFilename = convertedFilename;
                contentType = "application/pdf";
                _logger.LogInformation("PPTX converted to PDF (project): {Filename} ({Bytes} bytes)", convertedFilename, pdfBytes.Length);
            }
            else
            {
                _logger.LogWarning("PPTX→PDF conversion failed (project) — uploading original PPTX");
            }
        }

        var key = $"kb-docs/project/{projectId}/{safeFilename}";
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            InputStream = uploadStream,
            ContentType = contentType
        });
        _logger.LogInformation("Uploaded project KB document to s3://{Bucket}/{Key}", BucketName, key);

        var metadata = new { metadataAttributes = new Dictionary<string, object> { ["projectId"] = projectId } };
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = $"{key}.metadata.json",
            ContentBody = metadataJson,
            ContentType = "application/json"
        });

        return key;
    }

    public async Task DeleteDocumentAsync(string s3Key)
    {
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = s3Key });
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = $"{s3Key}.metadata.json" });
        _logger.LogInformation("Deleted KB document s3://{Bucket}/{Key}", BucketName, s3Key);
    }

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

    public async Task<string> PollIngestionJobAsync(string jobId, string kbId, string dsId)
    {
        try
        {
            var response = await _bedrockAgent.GetIngestionJobAsync(new Amazon.BedrockAgent.Model.GetIngestionJobRequest
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

    public async Task<string> PollIngestionJobAsync(string jobId)
        => await PollIngestionJobAsync(jobId, PersonalKbId, PersonalDataSourceId);

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

    public async Task<List<KbDocumentInfo>> ListDocumentsAsync(KbTier tier, string userId, string? teamId = null)
    {
        if (string.IsNullOrEmpty(userId) && tier == KbTier.Personal) return new();

        var prefix = tier switch
        {
            KbTier.Team      => $"kb-docs/teams/{teamId}/",
            KbTier.Corporate => "kb-docs/fortress/",
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

        return docs;
    }

    private static async Task<byte[]?> ConvertPptxToPdfAsync(Stream pptxStream, string filename, ILogger logger)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        var inputPath = Path.Combine(tmpDir, filename);
        var pdfPath = Path.ChangeExtension(inputPath, ".pdf");

        try
        {
            await using (var fs = new FileStream(inputPath, FileMode.Create, FileAccess.Write))
                await pptxStream.CopyToAsync(fs);

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
    public string IngestionStatus { get; set; } = "ingested";
}
