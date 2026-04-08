using Amazon.S3;
using Amazon.S3.Model;
using FortressNexus.Web.Data;
using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Services;

public class SubmissionService : ISubmissionService
{
    private readonly NexusDbContext _db;
    private readonly ILogger<SubmissionService> _logger;
    private readonly IAmazonS3 _s3;

    public SubmissionService(NexusDbContext db, ILogger<SubmissionService> logger, IAmazonS3 s3)
    {
        _db = db;
        _logger = logger;
        _s3 = s3;
    }

    public async Task<Submission> CreateAsync(SubmissionCreateDto dto, string userUpn)
    {
        var submission = new Submission
        {
            Title = dto.Title,
            FeatureArea = dto.FeatureArea,
            NarrativeText = dto.NarrativeText,
            MockupFileId = null,
            SubmittedBy = userUpn,
            SubmittedAt = DateTime.UtcNow,
            Status = SubmissionStatus.Pending
        };
        _db.Submissions.Add(submission);
        await _db.SaveChangesAsync();

        // Create SubmissionFile junction records
        var fileIds = dto.FileIds.ToList();
        for (int i = 0; i < fileIds.Count; i++)
        {
            var sf = new SubmissionFile
            {
                SubmissionId = submission.Id,
                UploadedFileId = fileIds[i],
                SortOrder = i
            };
            _db.SubmissionFiles.Add(sf);
        }
        if (fileIds.Count > 0)
            await _db.SaveChangesAsync();

        _logger.LogInformation("[SUBMISSION] Created submission {SubmissionId} Title={Title} for {UserUpn} with {FileCount} files",
            submission.Id, submission.Title, userUpn, fileIds.Count);
        return submission;
    }

    public async Task<Submission?> GetByIdAsync(int id)
    {
        return await _db.Submissions
            .Include(s => s.MockupFile)
            .Include(s => s.SubmissionFiles)
                .ThenInclude(sf => sf.UploadedFile)
            .Include(s => s.SpecDocuments)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<List<Submission>> GetByUserAsync(string userUpn)
    {
        return await _db.Submissions
            .Where(s => s.SubmittedBy == userUpn)
            .OrderByDescending(s => s.SubmittedAt)
            .ToListAsync();
    }

    public async Task<List<Submission>> GetAllPendingReviewAsync()
    {
        return await _db.Submissions
            .Where(s => s.Status == SubmissionStatus.AwaitingReview)
            .OrderBy(s => s.SubmittedAt)
            .ToListAsync();
    }

    public async Task UpdateStatusAsync(int id, SubmissionStatus status)
    {
        var submission = await _db.Submissions.FindAsync(id);
        if (submission is null)
        {
            _logger.LogWarning("[SUBMISSION] UpdateStatusAsync — submission {SubmissionId} not found", id);
            return;
        }
        submission.Status = status;
        await _db.SaveChangesAsync();
        _logger.LogInformation("[SUBMISSION] Status changed for submission {SubmissionId} to {Status}", id, status);
    }

    public async Task SetActiveSpecDocumentAsync(int submissionId, int specDocumentId)
    {
        var submission = await _db.Submissions.FindAsync(submissionId);
        if (submission is null)
        {
            _logger.LogWarning("[SUBMISSION] SetActiveSpecDocumentAsync — submission {SubmissionId} not found", submissionId);
            return;
        }
        submission.ActiveSpecDocumentId = specDocumentId;
        await _db.SaveChangesAsync();
    }

    public async Task<UploadedFile> SaveUploadedFileAsync(UploadedFile file)
    {
        _db.UploadedFiles.Add(file);
        await _db.SaveChangesAsync();
        return file;
    }

    public async Task UpdateNarrativeAsync(int submissionId, string narrativeText)
    {
        var submission = await _db.Submissions.FindAsync(submissionId);
        if (submission is null)
        {
            _logger.LogWarning("[SUBMISSION] UpdateNarrativeAsync — submission {SubmissionId} not found", submissionId);
            return;
        }
        submission.NarrativeText = narrativeText;
        await _db.SaveChangesAsync();
        _logger.LogInformation("[SUBMISSION] Narrative updated for submission {SubmissionId}", submissionId);
    }

    public async Task DeleteUploadedFileAsync(int submissionId, int fileId)
    {
        // Load the UploadedFile with its S3 metadata
        var uploadedFile = await _db.UploadedFiles
            .Include(f => f.SubmissionFiles)
            .FirstOrDefaultAsync(f => f.Id == fileId);

        if (uploadedFile is null)
        {
            _logger.LogWarning("[SUBMISSION] DeleteUploadedFileAsync — UploadedFile {FileId} not found", fileId);
            return;
        }

        // 1. Delete from S3 (non-fatal)
        try
        {
            var deleteRequest = new DeleteObjectRequest
            {
                BucketName = uploadedFile.S3Bucket,
                Key = uploadedFile.S3Key
            };
            await _s3.DeleteObjectAsync(deleteRequest);
            _logger.LogInformation("[SUBMISSION] S3 object deleted: bucket={Bucket} key={Key} (fileId={FileId})",
                uploadedFile.S3Bucket, uploadedFile.S3Key, fileId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SUBMISSION] S3 delete failed for fileId={FileId} key={Key} — proceeding with DB deletion",
                fileId, uploadedFile.S3Key);
        }

        // 2. Delete SubmissionFile junction record(s) for this submission (cascade is Restrict, must delete manually)
        try
        {
            var junctionRecords = uploadedFile.SubmissionFiles
                .Where(sf => sf.SubmissionId == submissionId)
                .ToList();
            if (junctionRecords.Count > 0)
            {
                _db.SubmissionFiles.RemoveRange(junctionRecords);
                await _db.SaveChangesAsync();
                _logger.LogInformation("[SUBMISSION] Deleted {Count} SubmissionFile record(s) for fileId={FileId} submissionId={SubmissionId}",
                    junctionRecords.Count, fileId, submissionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SUBMISSION] SubmissionFile deletion failed for fileId={FileId} submissionId={SubmissionId} — proceeding",
                fileId, submissionId);
        }

        // 3. Delete the UploadedFile record
        try
        {
            _db.UploadedFiles.Remove(uploadedFile);
            await _db.SaveChangesAsync();
            _logger.LogInformation("[SUBMISSION] UploadedFile record deleted: fileId={FileId}", fileId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[SUBMISSION] UploadedFile record deletion failed for fileId={FileId} — orphaned DB record accepted",
                fileId);
        }
    }

    public async Task DeleteSubmissionAsync(int id)
    {
        var submission = await _db.Submissions
            .Include(s => s.SubmissionFiles)
                .ThenInclude(sf => sf.UploadedFile)
            .Include(s => s.SpecDocuments)
                .ThenInclude(sd => sd.ArtifactSets)
                    .ThenInclude(a => a.WorkItemRecords)
            .Include(s => s.DiscoverySessions)
                .ThenInclude(ds => ds.Questions)
                    .ThenInclude(q => q.Answer)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (submission is null) return;

        // Collect UploadedFile records before cascade removes SubmissionFiles
        var uploadedFiles = submission.SubmissionFiles
            .Where(sf => sf.UploadedFile is not null)
            .Select(sf => sf.UploadedFile!)
            .DistinctBy(uf => uf.Id)
            .ToList();

        // S3 cleanup (non-fatal)
        foreach (var uf in uploadedFiles)
        {
            try
            {
                await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = uf.S3Bucket, Key = uf.S3Key });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SUBMISSION_DELETE] S3 delete failed for key={Key}", uf.S3Key);
            }
        }

        // Clear ActiveSpecDocumentId to allow SpecDocument deletion
        submission.ActiveSpecDocumentId = null;

        // Explicit delete: WorkItemRecords → ArtifactSets → SpecDocuments
        foreach (var sd in submission.SpecDocuments)
        {
            foreach (var a in sd.ArtifactSets)
            {
                _db.WorkItemRecords.RemoveRange(a.WorkItemRecords);
                _db.ArtifactSets.Remove(a);
            }
            _db.SpecDocuments.Remove(sd);
        }
        await _db.SaveChangesAsync();

        // Remove Submission — cascades SubmissionFiles + DiscoverySessions (→Questions→Answers)
        _db.Submissions.Remove(submission);
        await _db.SaveChangesAsync();

        // Now safe to delete UploadedFile records (SubmissionFiles junction already gone)
        _db.UploadedFiles.RemoveRange(uploadedFiles);
        await _db.SaveChangesAsync();

        _logger.LogInformation("[SUBMISSION_DELETE] Submission {SubmissionId} hard-deleted", id);
    }
}
