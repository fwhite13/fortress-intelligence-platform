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

    public SubmissionService(NexusDbContext db, ILogger<SubmissionService> logger)
    {
        _db = db;
        _logger = logger;
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

        _logger.LogInformation("NEXUS: Created submission {Id} for {Upn} with {Count} files",
            submission.Id, userUpn, fileIds.Count);
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
            _logger.LogWarning("NEXUS: UpdateStatus — submission {Id} not found", id);
            return;
        }
        submission.Status = status;
        await _db.SaveChangesAsync();
    }

    public async Task SetActiveSpecDocumentAsync(int submissionId, int specDocumentId)
    {
        var submission = await _db.Submissions.FindAsync(submissionId);
        if (submission is null)
        {
            _logger.LogWarning("NEXUS: SetActiveSpec — submission {Id} not found", submissionId);
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
}
