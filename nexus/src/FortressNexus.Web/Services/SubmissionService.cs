using FortressNexus.Web.Data;
using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;

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

    public Task<Submission> CreateAsync(SubmissionCreateDto dto, string userUpn) =>
        throw new NotImplementedException("WI-2");

    public Task<Submission?> GetByIdAsync(int id) =>
        throw new NotImplementedException("WI-2");

    public Task<List<Submission>> GetByUserAsync(string userUpn) =>
        throw new NotImplementedException("WI-2");

    public Task<List<Submission>> GetAllPendingReviewAsync() =>
        throw new NotImplementedException("WI-2");

    public Task UpdateStatusAsync(int id, SubmissionStatus status) =>
        throw new NotImplementedException("WI-2");

    public Task SetActiveSpecDocumentAsync(int submissionId, int specDocumentId) =>
        throw new NotImplementedException("WI-2");
}
