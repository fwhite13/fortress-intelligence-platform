using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

// WI #8295 — admin view over firm_speaker_embeddings, scoped to this deployment's Graph tenant
// (the same TENANT_ID BatchTranscriptionService hands the transcriber).
public class SpeakerEmbeddingService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<SpeakerEmbeddingService> _logger;

    public SpeakerEmbeddingService(IDbContextFactory<FirmDbContext> dbFactory, IConfiguration config, ILogger<SpeakerEmbeddingService> logger)
    {
        _dbFactory = dbFactory;
        _config = config;
        _logger = logger;
    }

    private string TenantId => _config["Firm:GraphTenantId"] ?? "";

    public async Task<List<FirmSpeakerEmbedding>> GetActiveAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.SpeakerEmbeddings.AsNoTracking()
            .Where(e => e.TenantId == TenantId && e.IsActive)
            .OrderBy(e => e.Name).ThenByDescending(e => e.CreatedAt)
            .ToListAsync();
    }

    // Soft delete — the transcriber only loads is_active rows; the S3 object is kept.
    public async Task<bool> DeactivateAsync(long id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var record = await db.SpeakerEmbeddings.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == TenantId);
        if (record == null)
            return false;
        record.IsActive = false;
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: Voiceprint {Id} ({Name}) deactivated", id, record.Name);
        return true;
    }

    // WI #8298 — self-service enrollment status for one user. meeting_auto rows are written by the
    // transcriber without an entra_oid, so they are matched on the user's display name.
    public async Task<VoiceEnrollmentStatus> GetEnrollmentStatusAsync(string entraOid, string displayName)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var voiceSample = await db.SpeakerEmbeddings.AsNoTracking()
            .Where(e => e.TenantId == TenantId && e.IsActive && e.Source == "voice_sample" && e.EntraOid == entraOid)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync();
        var autoMeetingCount = await db.SpeakerEmbeddings.AsNoTracking()
            .Where(e => e.TenantId == TenantId && e.IsActive && e.Source == "meeting_auto"
                        && (e.EntraOid == entraOid || (displayName != "" && e.Name == displayName)))
            .Select(e => e.MeetingId)
            .Distinct()
            .CountAsync();
        return new VoiceEnrollmentStatus(voiceSample?.Id, voiceSample?.CreatedAt, autoMeetingCount);
    }

    // WI #8298 — opt-out: soft-delete every voice_sample row for this user.
    public async Task<int> DeactivateVoiceSamplesAsync(string entraOid)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var records = await db.SpeakerEmbeddings
            .Where(e => e.TenantId == TenantId && e.IsActive && e.Source == "voice_sample" && e.EntraOid == entraOid)
            .ToListAsync();
        foreach (var record in records)
        {
            record.IsActive = false;
            record.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        _logger.LogInformation("FIRM: {Count} voice sample(s) deactivated for {EntraOid}", records.Count, entraOid);
        return records.Count;
    }
}

public record VoiceEnrollmentStatus(long? VoiceSampleId, DateTime? VoiceSampleRecordedAt, int AutoMeetingCount)
{
    public bool IsEnrolled => VoiceSampleId.HasValue;
}
