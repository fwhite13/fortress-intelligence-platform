using Amazon.BedrockAgentRuntime;
using Amazon.BedrockAgentRuntime.Model;
using Amazon.BedrockAgent;
using Amazon.BedrockAgent.Model;
using Amazon.S3;
using Amazon.S3.Model;

namespace FortressAI.V2.Web.Services;

/// <summary>
/// Direct AWS Bedrock Knowledge Base implementation.
/// Replaces the old fip-mcp HTTP client approach (which required Entra auth that isn't wired yet).
/// Reads KB IDs from config: KnowledgeBase:CorpKbId, KnowledgeBase:PersonalKbId, KnowledgeBase:TeamKbId.
/// </summary>
public class ForgeKbService : IForgeKbService
{
    private readonly IAmazonBedrockAgentRuntime _bedrockRuntime;
    private readonly IAmazonBedrockAgent _bedrockAgent;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<ForgeKbService> _logger;

    private readonly string _corpKbId;
    private readonly string _personalKbId;
    private readonly string _teamKbId;
    private readonly string _s3Bucket;

    public ForgeKbService(
        IAmazonBedrockAgentRuntime bedrockRuntime,
        IAmazonBedrockAgent bedrockAgent,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<ForgeKbService> logger)
    {
        _bedrockRuntime = bedrockRuntime;
        _bedrockAgent = bedrockAgent;
        _s3 = s3;
        _config = config;
        _logger = logger;

        _corpKbId = config["KnowledgeBase:CorpKbId"] ?? "";
        _personalKbId = config["KnowledgeBase:PersonalKbId"] ?? "";
        _teamKbId = config["KnowledgeBase:TeamKbId"] ?? "";
        _s3Bucket = config["AWS:S3Bucket"] ?? "fortress-tools";
    }

    public Task<IReadOnlyList<KbInfo>> ListKbsAsync(string entraOid, CancellationToken ct = default)
    {
        var kbs = new List<KbInfo>();

        if (!string.IsNullOrEmpty(_corpKbId))
            kbs.Add(new KbInfo(_corpKbId, "corp", "Fortress Corporate Knowledge Base", false));

        if (!string.IsNullOrEmpty(_personalKbId))
            kbs.Add(new KbInfo(_personalKbId, "personal", "Personal Knowledge Base", true));

        if (!string.IsNullOrEmpty(_teamKbId))
            kbs.Add(new KbInfo(_teamKbId, "team", "Team Knowledge Base", false));

        return Task.FromResult<IReadOnlyList<KbInfo>>(kbs);
    }

    public async Task<IReadOnlyList<KbSearchResult>> SearchKbAsync(
        string kbId, string query, int topK = 5, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(kbId))
            return Array.Empty<KbSearchResult>();

        try
        {
            var response = await _bedrockRuntime.RetrieveAsync(new RetrieveRequest
            {
                KnowledgeBaseId = kbId,
                RetrievalQuery = new KnowledgeBaseQuery { Text = query },
                RetrievalConfiguration = new KnowledgeBaseRetrievalConfiguration
                {
                    VectorSearchConfiguration = new KnowledgeBaseVectorSearchConfiguration
                    {
                        NumberOfResults = Math.Min(topK, 10)
                    }
                }
            }, ct);

            _logger.LogInformation("KB search: kbId={KbId} results={Count} query='{Query}'",
                kbId, response.RetrievalResults.Count,
                query.Length > 50 ? query[..50] + "..." : query);

            return response.RetrievalResults
                .Where(r => r.Score > 0.3)
                .Select(r => new KbSearchResult(
                    r.Content.Text,
                    r.Location?.S3Location?.Uri ?? string.Empty,
                    r.Score))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "KB search failed for kbId={KbId}", kbId);
            return Array.Empty<KbSearchResult>();
        }
    }

    public async Task<string> AddToKbAsync(
        string kbId, string content, Dictionary<string, string> metadata, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(kbId))
            return string.Empty;

        try
        {
            var s3Key = $"kb-uploads/{kbId}/{Guid.NewGuid()}.txt";
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _s3Bucket,
                Key = s3Key,
                InputStream = stream,
                ContentType = "text/plain"
            }, ct);

            _logger.LogInformation("Uploaded KB content to S3: {Key}", s3Key);

            var dataSourceId = _config[$"KnowledgeBase:DataSourceId:{kbId}"]
                            ?? _config["KnowledgeBase:DefaultDataSourceId"] ?? "";

            if (!string.IsNullOrEmpty(dataSourceId))
            {
                var ingestionResponse = await _bedrockAgent.StartIngestionJobAsync(
                    new StartIngestionJobRequest
                    {
                        KnowledgeBaseId = kbId,
                        DataSourceId = dataSourceId,
                    }, ct);

                var jobId = ingestionResponse.IngestionJob.IngestionJobId;
                _logger.LogInformation("Started KB ingestion job {JobId} for kbId={KbId}", jobId, kbId);
                return jobId;
            }

            _logger.LogWarning("No DataSourceId configured for kbId={KbId}; S3 upload complete but ingestion not triggered", kbId);
            return s3Key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add content to KB {KbId}", kbId);
            return string.Empty;
        }
    }

    public async Task<string> UploadFileAsync(string kbId, Stream fileStream, string filename, string contentType, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(filename).ToLowerInvariant();
        var s3Key = $"kb-uploads/{kbId}/{Guid.NewGuid()}{ext}";

        // PPTX → PDF conversion (best-effort)
        Stream uploadStream = fileStream;
        string uploadContentType = contentType;
        string uploadKey = s3Key;

        if (ext == ".pptx")
        {
            var pdfBytes = await ConvertPptxToPdfAsync(fileStream, filename, _logger);
            if (pdfBytes != null)
            {
                uploadStream = new MemoryStream(pdfBytes);
                uploadContentType = "application/pdf";
                uploadKey = Path.ChangeExtension(s3Key, ".pdf");
            }
            // If conversion fails, fall through and upload original .pptx
        }

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _s3Bucket,
            Key = uploadKey,
            InputStream = uploadStream,
            ContentType = uploadContentType
        }, ct);

        _logger.LogInformation("Uploaded KB file to S3: {Key}", uploadKey);

        // Start ingestion job
        var dataSourceId = _config[$"KnowledgeBase:DataSourceId:{kbId}"]
                        ?? _config["KnowledgeBase:DefaultDataSourceId"] ?? "";

        if (!string.IsNullOrEmpty(dataSourceId))
        {
            var ingestionResponse = await _bedrockAgent.StartIngestionJobAsync(
                new StartIngestionJobRequest
                {
                    KnowledgeBaseId = kbId,
                    DataSourceId = dataSourceId
                }, ct);
            return ingestionResponse.IngestionJob?.IngestionJobId ?? string.Empty;
        }

        return string.Empty;
    }

    private static async Task<byte[]?> ConvertPptxToPdfAsync(Stream pptxStream, string filename, ILogger logger)
    {
        try
        {
            var tempDir = Path.GetTempPath();
            var pptxPath = Path.Combine(tempDir, filename);
            var pdfPath = Path.ChangeExtension(pptxPath, ".pdf");
            await using (var fs = File.Create(pptxPath)) await pptxStream.CopyToAsync(fs);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "libreoffice",
                Arguments = $"--headless --convert-to pdf --outdir \"{tempDir}\" \"{pptxPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;
            await proc.WaitForExitAsync();
            if (proc.ExitCode == 0 && File.Exists(pdfPath))
                return await File.ReadAllBytesAsync(pdfPath);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PPTX→PDF conversion failed — uploading original");
            return null;
        }
    }

    public async Task<KbMetadata> GetKbMetadataAsync(string kbId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(kbId))
            return new KbMetadata(kbId, "unknown", 0, DateTime.UtcNow, string.Empty);

        try
        {
            var response = await _bedrockAgent.GetKnowledgeBaseAsync(
                new GetKnowledgeBaseRequest { KnowledgeBaseId = kbId }, ct);

            var kb = response.KnowledgeBase;
            var kbType = kbId == _corpKbId ? "corp" :
                         kbId == _personalKbId ? "personal" :
                         kbId == _teamKbId ? "team" : "unknown";

            return new KbMetadata(
                kbId,
                kbType,
                0,
                kb.UpdatedAt,
                kb.KnowledgeBaseId ?? string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get KB metadata for kbId={KbId}", kbId);
            return new KbMetadata(kbId, "unknown", 0, DateTime.UtcNow, string.Empty);
        }
    }
}
