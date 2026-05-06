using FortressNexus.Web.Models.DTOs;
using FortressNexus.Web.Models.Entities;
using FortressNexus.Web.Models.Enums;

namespace FortressNexus.Web.Services;

public interface ISubmissionService
{
    Task<Submission> CreateAsync(SubmissionCreateDto dto, string userUpn);
    Task<Submission?> GetByIdAsync(int id);
    Task<List<Submission>> GetByUserAsync(string userUpn, bool isAdmin = false);
    Task<List<Submission>> GetAllPendingReviewAsync();
    Task UpdateStatusAsync(int id, SubmissionStatus status, string callerUpn, bool isAdmin = false);
    Task SetActiveSpecDocumentAsync(int submissionId, int specDocumentId, string callerUpn, bool isAdmin = false);
    Task<UploadedFile> SaveUploadedFileAsync(UploadedFile file);
    Task UpdateNarrativeAsync(int submissionId, string narrativeText, string callerUpn, bool isAdmin = false);
    Task DeleteUploadedFileAsync(int submissionId, int fileId);
    Task DeleteSubmissionAsync(int id, string callerUpn, bool callerIsAdmin);
    Task AddFileToSubmissionAsync(int submissionId, int uploadedFileId, int sortOrder);
    Task UpdateUploadedFileAsync(UploadedFile file);
}
