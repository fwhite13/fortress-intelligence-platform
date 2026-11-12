namespace FortressIntelligenceRM.Web.Models;

/// <summary>
/// WI #9134 — per-user Plaud sync settings, claim and status (firm_plaud_sync, spec §5.5). Separate from
/// firm_plaud_accounts so settings and history survive a disconnect and reconnect.
/// </summary>
public class FirmPlaudSync
{
    /// <summary>D13: the first sync imports the last 3 days unless the user picks another window.</summary>
    public const int DefaultBackfillDays = 3;
    public static readonly int[] BackfillDayChoices = { 1, 3, 7, 30, 90 };

    public Guid UserId { get; set; }
    public bool AutoSync { get; set; }
    public int IntervalMinutes { get; set; } = 30;
    public int BackfillDays { get; set; } = DefaultBackfillDays;
    /// <summary>Start of the listing window; set on the first run to now − BackfillDays.</summary>
    public DateTime? SyncFrom { get; set; }
    public DateTime? NextSyncAt { get; set; }
    public string? ClaimedBy { get; set; }
    public DateTime? ClaimedAt { get; set; }
    /// <summary>idle | ok | backoff | rate_limited | reconnect_needed | disconnected</summary>
    public string State { get; set; } = "idle";
    public int ConsecutiveFailures { get; set; }
    public string? LastErrorCode { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastImportAt { get; set; }
    public int LastImportedCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}
