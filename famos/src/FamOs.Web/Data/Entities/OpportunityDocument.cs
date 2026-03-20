namespace FamOs.Web.Data.Entities;

public class OpportunityDocument
{
    public Guid     Id                 { get; set; } = Guid.NewGuid();
    public Guid     OpportunityId      { get; set; }
    public string   FileName           { get; set; } = "";
    public string?  FileType           { get; set; }
    public string   S3Key              { get; set; } = "";
    public DocumentCategory DocumentCategory { get; set; } = DocumentCategory.Other;
    public DateTime UploadedAt         { get; set; } = DateTime.UtcNow;
    public string?  UploadedBy         { get; set; }

    public Opportunity Opportunity     { get; set; } = default!;
}

public enum DocumentCategory
{
    Application   = 0,
    Quote         = 1,
    Proposal      = 2,
    BindRequest   = 3,
    Policy        = 4,
    Correspondence = 5,
    Other         = 6
}
