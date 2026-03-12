using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Web.Data;
using FortressAI.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Controllers;

/// <summary>
/// Internal admin endpoints for KB maintenance.
/// All endpoints are restricted to loopback (127.0.0.1 / ::1) — no auth token required.
/// </summary>
[ApiController]
[Route("api/kb")]
public class AdminKbController : ControllerBase
{
    private readonly IAmazonS3 _s3;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<AdminKbController> _logger;
    private readonly KbDocumentService _kbDocumentService;

    private const string BucketName = "fortress-tools";

    public AdminKbController(IAmazonS3 s3, IDbContextFactory<AppDbContext> dbFactory,
        ILogger<AdminKbController> logger, KbDocumentService kbDocumentService)
    {
        _s3 = s3;
        _dbFactory = dbFactory;
        _logger = logger;
        _kbDocumentService = kbDocumentService;
    }

    /// <summary>
    /// Backfill kb_document_tracking rows for any personal KB documents that are in S3
    /// but have no ProjectDocuments row. Useful when files were uploaded before tracking
    /// was introduced, or when a user's files appear under a different userId prefix.
    ///
    /// GET /api/kb/admin/backfill-tracking
    /// Restricted: loopback only.
    /// </summary>
    [HttpGet("admin/backfill-tracking")]
    public async Task<IActionResult> BackfillKbTracking()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();
        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
            return StatusCode(403, new { error = "Forbidden: internal endpoint" });

        _logger.LogInformation("BackfillKbTracking: starting S3 scan of kb-docs/personal/");

        // List all personal KB objects (paginated)
        var listReq = new ListObjectsV2Request { BucketName = BucketName, Prefix = "kb-docs/personal/" };
        var allObjects = new List<string>();
        ListObjectsV2Response listResp;
        do
        {
            listResp = await _s3.ListObjectsV2Async(listReq);
            allObjects.AddRange(listResp.S3Objects
                .Where(o => !o.Key.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase))
                .Select(o => o.Key));
            listReq.ContinuationToken = listResp.NextContinuationToken;
        } while (listResp.IsTruncated);

        _logger.LogInformation("BackfillKbTracking: found {Total} non-metadata objects in S3", allObjects.Count);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var existingKeys = (await db.ProjectDocuments
            .Where(pd => pd.S3Key != null)
            .Select(pd => pd.S3Key!)
            .ToListAsync()).ToHashSet();

        int created = 0;
        foreach (var key in allObjects)
        {
            if (existingKeys.Contains(key)) continue;

            var filename = key.Split('/').Last();
            db.ProjectDocuments.Add(new FortressAI.Shared.Models.ProjectDocument
            {
                Id = Guid.NewGuid(),
                ProjectId = null,             // not a project document
                Filename = filename,
                S3Key = key,
                FileSize = 0,                 // unknown — not tracked for backfill
                IngestionStatus = "ingested", // already in Bedrock — no tracking row = uploaded before tracking
                UploadedAt = DateTime.UtcNow
            });
            created++;
        }

        if (created > 0)
            await db.SaveChangesAsync();

        _logger.LogInformation("BackfillKbTracking: created {Created} tracking rows", created);

        return Ok(new { backfilled = created, total = allObjects.Count });
    }

    /// <summary>
    /// Trigger a Bedrock ingestion job for the Project KB (KB ID: A5U1GKN0TS).
    /// Use after directly inserting project documents into S3/DB without going through the upload UI.
    ///
    /// POST /api/kb/admin/sync-project
    /// Auth: loopback IP OR valid x-api-key header (AppKeys:Haven) — allows external callers (e.g. Jarvis)
    /// Returns: { jobId: "..." }
    /// </summary>
    [HttpPost("admin/sync-project")]
    public async Task<IActionResult> SyncProjectKb(
        [FromServices] IConfiguration config)
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        if (remoteIp != null && remoteIp.IsIPv4MappedToIPv6)
            remoteIp = remoteIp.MapToIPv4();
        var isLoopback = remoteIp != null && IPAddress.IsLoopback(remoteIp);

        // Accept loopback OR valid API key
        var apiKey = HttpContext.Request.Headers["x-api-key"].FirstOrDefault();
        var configuredKey = config["AppKeys:Haven"];
        var isValidApiKey = !string.IsNullOrEmpty(apiKey) &&
                            !string.IsNullOrEmpty(configuredKey) &&
                            string.Equals(apiKey, configuredKey, StringComparison.Ordinal);

        if (!isLoopback && !isValidApiKey)
            return StatusCode(403, new { error = "Forbidden: loopback or valid API key required" });

        _logger.LogInformation("SyncProjectKb: triggering Bedrock ingestion for Project KB");

        try
        {
            var jobId = await _kbDocumentService.StartProjectIngestionAsync(throwOnConflict: false);
            if (jobId is null)
            {
                _logger.LogWarning("SyncProjectKb: StartProjectIngestionAsync returned null — KB or DS ID may not be configured");
                return StatusCode(500, new { error = "Ingestion job not started — ProjectKbId or ProjectDataSourceId not configured" });
            }

            _logger.LogInformation("SyncProjectKb: ingestion job started, jobId={JobId}", jobId);
            return Ok(new { jobId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncProjectKb: failed to start ingestion job");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
