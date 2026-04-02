namespace FortressNexus.Web.Models.Entities;

public class SubmissionFile
{
    public int Id { get; set; }
    public int SubmissionId { get; set; }
    public int UploadedFileId { get; set; }
    public int SortOrder { get; set; }

    // Navigation
    public Submission? Submission { get; set; }
    public UploadedFile? UploadedFile { get; set; }
}
