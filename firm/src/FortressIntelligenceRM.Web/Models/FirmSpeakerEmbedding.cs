namespace FortressIntelligenceRM.Web.Models;

// WI #8295 — a persisted speaker voiceprint. The embedding itself lives in S3 as .npy (S3Key);
// the Batch transcriber loads active rows per tenant and inserts meeting_auto rows.
public class FirmSpeakerEmbedding
{
    public long Id { get; set; }
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string? EntraOid { get; set; }
    public string S3Key { get; set; } = "";
    public string Source { get; set; } = "";
    public long? MeetingId { get; set; }
    public float? Confidence { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
