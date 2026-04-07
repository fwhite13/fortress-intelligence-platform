namespace FortressNexus.Web.Models.Entities;

public class DiscoverySession
{
    public Guid Id { get; set; }
    public int SubmissionId { get; set; }   // int — FK to submissions.id (INT AUTO_INCREMENT)
    public string Status { get; set; } = "Pending";
    public string? KbQueryUsed { get; set; }
    public int? KbPassagesRetrieved { get; set; }
    public int? QuestionCount { get; set; }
    public bool SkippedByUser { get; set; }
    public DateTime? GeneratedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Submission Submission { get; set; } = null!;
    public ICollection<DiscoveryQuestion> Questions { get; set; } = new List<DiscoveryQuestion>();
}
