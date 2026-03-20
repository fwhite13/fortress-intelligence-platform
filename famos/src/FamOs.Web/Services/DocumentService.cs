using Amazon.S3;
using Amazon.S3.Model;
using FamOs.Web.Data;
using FamOs.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FamOs.Web.Services;

public interface IDocumentService
{
    Task<string> GetDownloadUrlAsync(string s3Key);
    Task<Guid> RecordUploadAsync(Guid opportunityId, string fileName, string contentType,
        string s3Key, DocumentCategory category, string uploadedBy);
    Task DeleteAsync(Guid documentId, string actorUserId);
    Task UploadRawAsync(string s3Key, Stream data, string contentType);
}

public class DocumentService : IDocumentService
{
    private readonly IAmazonS3 _s3;
    private readonly IDbContextFactory<FamOsDbContext> _dbFactory;
    private readonly ILogger<DocumentService> _logger;

    private const string BucketName = "fip-cowork-workspaces";
    private const string KeyPrefix  = "famos/documents";

    public DocumentService(IAmazonS3 s3,
        IDbContextFactory<FamOsDbContext> dbFactory,
        ILogger<DocumentService> logger)
    {
        _s3        = s3;
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    public Task<string> GetDownloadUrlAsync(string s3Key)
    {
        var request = new GetPreSignedUrlRequest
        {
            BucketName = BucketName,
            Key        = s3Key,
            Verb       = HttpVerb.GET,
            Expires    = DateTime.UtcNow.AddMinutes(60)
        };
        return Task.FromResult(_s3.GetPreSignedURL(request));
    }

    public async Task<Guid> RecordUploadAsync(
        Guid opportunityId, string fileName, string contentType,
        string s3Key, DocumentCategory category, string uploadedBy)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var doc = new OpportunityDocument
        {
            OpportunityId    = opportunityId,
            FileName         = fileName,
            FileType         = contentType,
            S3Key            = s3Key,
            DocumentCategory = category,
            UploadedBy       = uploadedBy,
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();
        _logger.LogInformation("[Docs] Recorded {File} for opportunity {Opp}", fileName, opportunityId);
        return doc.Id;
    }

    public async Task DeleteAsync(Guid documentId, string actorUserId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var doc = await db.Documents.FindAsync(documentId)
            ?? throw new Exception($"Document {documentId} not found");

        try
        {
            await _s3.DeleteObjectAsync(BucketName, doc.S3Key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Docs] S3 delete failed for {Key} — removing DB record anyway", doc.S3Key);
        }

        db.Documents.Remove(doc);
        await db.SaveChangesAsync();
    }

    public async Task UploadRawAsync(string s3Key, Stream data, string contentType)
    {
        var request = new PutObjectRequest
        {
            BucketName  = BucketName,
            Key         = s3Key,
            InputStream = data,
            ContentType = contentType,
        };
        await _s3.PutObjectAsync(request);
        _logger.LogInformation("[Docs] S3 upload complete: {Key}", s3Key);
    }
}
