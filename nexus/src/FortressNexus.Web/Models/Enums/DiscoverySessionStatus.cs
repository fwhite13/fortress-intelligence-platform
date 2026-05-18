namespace FortressNexus.Web.Models.Enums;

/// <summary>
/// String status constants for DiscoverySession.Status (stored as varchar(50)).
/// </summary>
public static class DiscoverySessionStatus
{
    // Legacy statuses — retained for backward compatibility with pre-iterative-discovery sessions
    public const string Pending = "Pending";
    public const string QuestionsReady = "QuestionsReady";
    public const string Answered = "Answered";
    public const string Skipped = "Skipped";
    public const string Failed = "Failed";
    public const string Superseded = "Superseded";

    // Two-phase iterative discovery statuses (ND-1)
    public const string Phase1Active   = "Phase1Active";
    public const string Phase1Complete = "Phase1Complete";
    public const string Phase2Active   = "Phase2Active";
    public const string Phase2Complete = "Phase2Complete";
}
