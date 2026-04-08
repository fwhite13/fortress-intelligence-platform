using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Services;

public interface ISubmissionService
{
    Task<Submission> CreateAsync(SubmissionCreateDto dto, string userUpn);
    Task<Submission?> GetByIdAsync(int id);
    Task<List<Submission>> GetByUserAsync(string userUpn);
    Task<List<Submission>> GetAllPendingReviewAsync();
    Task UpdateStatusAsync(int id, SubmissionStatus status);
    Task SetActiveSpecDocumentAsync(int submissionId, int specDocumentId);
    Task<UploadedFile> SaveUploadedFileAsync(UploadedFile file);
    Task UpdateNarrativeAsync(int submissionId, string narrativeText);
    Task DeleteUploadedFileAsync(int submissionId, int fileId);
}
