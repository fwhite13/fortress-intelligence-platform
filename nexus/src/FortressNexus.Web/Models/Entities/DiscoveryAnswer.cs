namespace FortressNexus.Web.Models.Entities;

public class DiscoveryAnswer
{
    public Guid Id { get; set; }
    public Guid DiscoveryQuestionId { get; set; }
    public string? AnswerText { get; set; }
    public string AnsweredBy { get; set; } = string.Empty;
    public DateTime AnsweredAt { get; set; }

    public DiscoveryQuestion Question { get; set; } = null!;
}
