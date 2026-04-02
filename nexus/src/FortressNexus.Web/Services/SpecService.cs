using FortressNexus.Web.Data;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FortressNexus.Web.Services;

public class SpecService : ISpecService
{
    private readonly NexusDbContext _db;
    private readonly ILogger<SpecService> _logger;

    public SpecService(NexusDbContext db, ILogger<SpecService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SaveDraftAsync(int specDocumentId, string editedContent, string userUpn)
    {
        var doc = await _db.SpecDocuments.FindAsync(specDocumentId)
            ?? throw new KeyNotFoundException($"SpecDocument {specDocumentId} not found.");

        doc.EditedContent = editedContent;
        doc.EditedAt = DateTime.UtcNow;
        doc.EditedBy = userUpn;
        await _db.SaveChangesAsync();
        _logger.LogInformation("NEXUS: Draft saved for SpecDocument {Id} by {Upn}", specDocumentId, userUpn);
    }

    public async Task<SpecDocument> ApproveAsync(int specDocumentId, string approverOid)
    {
        var doc = await _db.SpecDocuments
            .Include(d => d.Submission)
            .FirstOrDefaultAsync(d => d.Id == specDocumentId)
            ?? throw new KeyNotFoundException($"SpecDocument {specDocumentId} not found.");

        doc.IsApproved = true;
        doc.ApprovedBy = approverOid;
        doc.ApprovedAt = DateTime.UtcNow;

        if (doc.Submission is not null)
            doc.Submission.Status = SubmissionStatus.Approved;

        await _db.SaveChangesAsync();
        _logger.LogInformation("NEXUS: SpecDocument {Id} approved by {OID}", specDocumentId, approverOid);
        return doc;
    }
}
