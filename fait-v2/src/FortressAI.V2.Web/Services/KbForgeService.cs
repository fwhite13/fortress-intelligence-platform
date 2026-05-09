using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public class KbForgeService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly ILogger<KbForgeService> _logger;
    private readonly IAmazonS3 _s3;
    private readonly KbDocumentService _kbDocumentService;

    private const string BucketName = "fortress-tools";

    public KbForgeService(IDbContextFactory<FaitV2DbContext> dbFactory, ILogger<KbForgeService> logger, IAmazonS3 s3, KbDocumentService kbDocumentService)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _s3 = s3;
        _kbDocumentService = kbDocumentService;
    }

    // ── S3 Sync Helpers ───────────────────────────────────────────────────────

    private static string GetNoteS3Key(KbEntry entry) => entry.Tier switch
    {
        KbTier.Team      => $"kb-docs/teams/{entry.TeamId}/note-{entry.Id}.txt",
        KbTier.Corporate => $"kb-docs/fortress/note-{entry.Id}.txt",
        KbTier.Developer => $"kb-docs/dev/note-{entry.Id}.txt",
        _                => $"kb-docs/personal/{entry.UserId}/note-{entry.Id}.txt"
    };

    private static string GetNoteMetadataKey(KbEntry entry) => $"{GetNoteS3Key(entry)}.metadata.json";

    private async Task UploadNoteToS3Async(KbEntry entry)
    {
        var s3Key = GetNoteS3Key(entry);
        var noteText = $"# {entry.Title}\n\n{entry.Content}";
        if (!string.IsNullOrWhiteSpace(entry.Tags))
            noteText += $"\n\nTags: {entry.Tags}";

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = s3Key,
            ContentBody = noteText,
            ContentType = "text/plain"
        });

        var metadataDict = entry.Tier == KbTier.Team
            ? new Dictionary<string, object> { ["teamId"] = entry.TeamId! }
            : new Dictionary<string, object> { ["ownerId"] = entry.UserId };

        var metadata = new { metadataAttributes = metadataDict };
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = GetNoteMetadataKey(entry),
            ContentBody = metadataJson,
            ContentType = "application/json"
        });

        _logger.LogInformation("[KbForgeService] Wrote note {EntryId} to S3: {S3Key}", entry.Id, s3Key);
    }

    private async Task DeleteNoteFromS3Async(KbEntry entry)
    {
        var s3Key = GetNoteS3Key(entry);
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = s3Key });
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = GetNoteMetadataKey(entry) });
        _logger.LogInformation("[KbForgeService] Deleted note {EntryId} from S3: {S3Key}", entry.Id, s3Key);
    }

    // ── Access Control ────────────────────────────────────────────────────────

    public async Task<bool> CanWriteEntryAsync(string userId, KbEntry entry)
    {
        return entry.Tier switch
        {
            KbTier.Personal  => entry.UserId == userId,
            KbTier.Team      => entry.TeamId != null && await IsTeamMemberAsync(userId, entry.TeamId),
            KbTier.Corporate => false,
            KbTier.Developer => entry.UserId == userId,
            _                => false
        };
    }

    public async Task<bool> IsTeamMemberAsync(string userId, string teamId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbTeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == userId);
    }

    // ── Entry Methods ─────────────────────────────────────────────────────────

    public async Task<List<KbEntry>> GetPersonalEntriesAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbEntries
            .Where(e => e.Tier == KbTier.Personal && e.UserId == userId)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();
    }

    public async Task<List<KbEntry>> GetTeamEntriesAsync(string userId, string teamId)
    {
        if (!await IsTeamMemberAsync(userId, teamId))
            throw new UnauthorizedAccessException($"User {userId} is not a member of team {teamId}.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbEntries
            .Where(e => e.Tier == KbTier.Team && e.TeamId == teamId)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();
    }

    public async Task<KbEntry> CreateEntryAsync(string userId, KbTier tier, string title, string content, string? tags = null, string? teamId = null)
    {
        if (tier == KbTier.Corporate)
            throw new InvalidOperationException("Corporate KB entries can only be created by administrators.");

        if (tier == KbTier.Team)
        {
            if (string.IsNullOrEmpty(teamId))
                throw new ArgumentException("TeamId is required for Team-tier entries.");
            if (!await IsTeamMemberAsync(userId, teamId))
                throw new UnauthorizedAccessException($"User {userId} is not a member of team {teamId}.");
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var entry = new KbEntry
        {
            UserId    = userId,
            Tier      = tier,
            TeamId    = tier == KbTier.Team ? teamId : null,
            Title     = title,
            Content   = content,
            Tags      = tags,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.KbEntries.Add(entry);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created KbEntry {Id} (Tier={Tier}) for user {UserId}", entry.Id, tier, userId);

        try
        {
            await UploadNoteToS3Async(entry);
            var ingestTier = entry.Tier switch
            {
                KbTier.Team      => KbTier.Team,
                KbTier.Corporate => KbTier.Corporate,
                KbTier.Developer => KbTier.Developer,
                _                => KbTier.Personal
            };
            _ = await _kbDocumentService.StartIngestionAsync(ingestTier);
            _logger.LogInformation("[KbForgeService] Note {EntryId} synced to S3 and ingestion triggered", entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KbForgeService] Failed to sync note {EntryId} to S3", entry.Id);
        }

        return entry;
    }

    public async Task<KbEntry> UpdateEntryAsync(string userId, string entryId, string title, string content, string? tags)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var entry = await db.KbEntries.FindAsync(entryId)
            ?? throw new KeyNotFoundException($"KbEntry {entryId} not found.");

        if (!await CanWriteEntryAsync(userId, entry))
            throw new UnauthorizedAccessException($"User {userId} cannot write KbEntry {entryId}.");

        entry.Title     = title;
        entry.Content   = content;
        entry.Tags      = tags;
        entry.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        _logger.LogInformation("Updated KbEntry {Id} by user {UserId}", entryId, userId);

        try
        {
            await UploadNoteToS3Async(entry);
            var ingestTier = entry.Tier switch
            {
                KbTier.Team      => KbTier.Team,
                KbTier.Corporate => KbTier.Corporate,
                KbTier.Developer => KbTier.Developer,
                _                => KbTier.Personal
            };
            _ = await _kbDocumentService.StartIngestionAsync(ingestTier);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KbForgeService] Failed to sync note {EntryId} to S3", entry.Id);
        }

        return entry;
    }

    public async Task DeleteEntryAsync(string userId, string entryId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var entry = await db.KbEntries.FindAsync(entryId)
            ?? throw new KeyNotFoundException($"KbEntry {entryId} not found.");

        if (!await CanWriteEntryAsync(userId, entry))
            throw new UnauthorizedAccessException($"User {userId} cannot delete KbEntry {entryId}.");

        try
        {
            await DeleteNoteFromS3Async(entry);
            var ingestTier = entry.Tier switch
            {
                KbTier.Team      => KbTier.Team,
                KbTier.Corporate => KbTier.Corporate,
                KbTier.Developer => KbTier.Developer,
                _                => KbTier.Personal
            };
            _ = await _kbDocumentService.StartIngestionAsync(ingestTier);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KbForgeService] Failed to delete note {EntryId} from S3", entryId);
        }

        db.KbEntries.Remove(entry);
        await db.SaveChangesAsync();
        _logger.LogInformation("Deleted KbEntry {Id} by user {UserId}", entryId, userId);
    }

    // ── Team Methods ──────────────────────────────────────────────────────────

    public async Task<List<KbTeam>> GetUserTeamsAsync(string userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbTeams
            .Where(p => db.KbTeamMembers.Any(m => m.TeamId == p.Id && m.UserId == userId))
            .Include(p => p.Members)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    public async Task<KbTeam> CreateTeamAsync(string userId, string name, string? description = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                var team = new KbTeam
                {
                    CreatorId   = userId,
                    Name        = name,
                    Description = description,
                    CreatedAt   = DateTime.UtcNow
                };
                db.KbTeams.Add(team);
                await db.SaveChangesAsync();

                var ownerMember = new KbTeamMember
                {
                    TeamId   = team.Id,
                    UserId   = userId,
                    Role     = KbTeamRole.Owner,
                    JoinedAt = DateTime.UtcNow
                };
                db.KbTeamMembers.Add(ownerMember);
                await db.SaveChangesAsync();

                await transaction.CommitAsync();
                _logger.LogInformation("Created KbTeam {Id} '{Name}' by user {UserId}", team.Id, name, userId);
                return team;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    public async Task AddMemberAsync(string requestingUserId, string teamId, string newMemberId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var requester = await db.KbTeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == requestingUserId)
            ?? throw new UnauthorizedAccessException($"User {requestingUserId} is not a member of team {teamId}.");

        if (requester.Role != KbTeamRole.Owner)
            throw new UnauthorizedAccessException($"User {requestingUserId} is not an Owner of team {teamId}.");

        var existing = await db.KbTeamMembers.AnyAsync(m => m.TeamId == teamId && m.UserId == newMemberId);
        if (existing)
        {
            _logger.LogInformation("User {NewMemberId} is already a member of team {TeamId} — skipping.", newMemberId, teamId);
            return;
        }

        db.KbTeamMembers.Add(new KbTeamMember
        {
            TeamId   = teamId,
            UserId   = newMemberId,
            Role     = KbTeamRole.Member,
            JoinedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        _logger.LogInformation("Added user {NewMemberId} to team {TeamId} by {RequestingUserId}", newMemberId, teamId, requestingUserId);
    }

    public async Task RemoveMemberAsync(string requestingUserId, string teamId, string memberId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var requester = await db.KbTeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == requestingUserId);

        bool isSelf  = requestingUserId == memberId;
        bool isOwner = requester?.Role == KbTeamRole.Owner;

        if (!isSelf && !isOwner)
            throw new UnauthorizedAccessException($"User {requestingUserId} cannot remove member {memberId} from team {teamId}.");

        var target = await db.KbTeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == memberId)
            ?? throw new KeyNotFoundException($"Member {memberId} not found in team {teamId}.");

        db.KbTeamMembers.Remove(target);
        await db.SaveChangesAsync();
        _logger.LogInformation("Removed user {MemberId} from team {TeamId} by {RequestingUserId}", memberId, teamId, requestingUserId);
    }
}
