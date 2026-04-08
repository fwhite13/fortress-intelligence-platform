namespace FortressNexus.Web.Models.Enums;

/// <summary>
/// String status constants for DiscoverySession.Status (stored as varchar(50)).
/// </summary>
public static class DiscoverySessionStatus
{
    public const string Pending = "Pending";
    public const string QuestionsReady = "QuestionsReady";
    public const string Answered = "Answered";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
    public const string Superseded = "Superseded";
}
