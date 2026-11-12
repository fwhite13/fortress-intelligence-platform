namespace FortressIntelligenceRM.Web.Models;

/// <summary>
/// WI #9134 — Plaud import ledger (firm_plaud_imports, spec §4.4): one row per (FIRM user, Plaud file id).
/// A file id with a row is never imported again unless the row is in a retryable state (spec §4.5);
/// a meeting the user removed leaves a <see cref="PlaudImportStatus.DeletedByUser"/> tombstone.
/// Never holds the presigned audio URL. <see cref="Title"/> is shown in Settings only and never logged.
/// </summary>
public class FirmPlaudImport
{
    public long Id { get; set; }
    public Guid UserId { get; set; }
    public string PlaudFileId { get; set; } = "";
    /// <summary>Plaud recording name. Display only; never logged.</summary>
    public string? Title { get; set; }
    /// <summary>Plaud start_at (fallback created_at), UTC.</summary>
    public DateTime? RecordedAt { get; set; }
    public long? DurationMs { get; set; }
    /// <summary>See <see cref="PlaudImportStatus"/>.</summary>
    public string Status { get; set; } = PlaudImportStatus.Queued;
    public long? MeetingId { get; set; }
    /// <summary>auto | manual | backfill (<see cref="PlaudSyncTrigger"/>).</summary>
    public string TriggerKind { get; set; } = PlaudSyncTrigger.Manual;
    public int Attempts { get; set; }
    /// <summary>A short code only, never a Plaud body.</summary>
    public string? ErrorCode { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? ClaimedAt { get; set; }
    public DateTime FirstSeenAt { get; set; }
    public DateTime? ImportedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>D17 / P3: archive of Plaud's own transcript and note (ok | partial | failed). Never blocks the import.</summary>
    public string? PlaudArchiveStatus { get; set; }
}

/// <summary>Ledger statuses (spec §4.5).</summary>
public static class PlaudImportStatus
{
    public const string Queued = "queued";
    public const string Importing = "importing";
    public const string PendingAudio = "pending_audio";
    public const string NoAudio = "no_audio";
    public const string TooLong = "too_long";
    public const string Failed = "failed";
    public const string SubmitFailed = "submit_failed";
    public const string Imported = "imported";
    public const string DeletedByUser = "deleted_by_user";

    /// <summary>The only states a sync run may pick up again. Everything else is never re-imported.</summary>
    public static readonly string[] Retryable = { Queued, PendingAudio, Failed, SubmitFailed };

    /// <summary>Counted as "waiting" in Settings.</summary>
    public static readonly string[] Waiting = { Queued, Importing, PendingAudio, SubmitFailed };

    /// <summary>Counted as "skipped" in Settings.</summary>
    public static readonly string[] Skipped = { TooLong, NoAudio };
}

/// <summary>firm_plaud_imports.trigger_kind values.</summary>
public static class PlaudSyncTrigger
{
    public const string Auto = "auto";
    public const string Manual = "manual";
    public const string Backfill = "backfill";
}
