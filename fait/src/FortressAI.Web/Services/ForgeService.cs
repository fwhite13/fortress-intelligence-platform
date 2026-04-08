using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.Web.Services;

public class ForgeService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ForgeService> _logger;
    private readonly IAmazonS3 _s3;
    private readonly KbDocumentService _kbDocumentService;

    public ForgeService(IDbContextFactory<AppDbContext> dbFactory, ILogger<ForgeService> logger, IAmazonS3 s3, KbDocumentService kbDocumentService)
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
        const string BucketName = "fortress-tools";
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
            ? new Dictionary<string, object> { ["teamId"] = entry.TeamId!.Value.ToString() }
            : new Dictionary<string, object> { ["ownerId"] = entry.UserId.ToString() };

        var metadata = new { metadataAttributes = metadataDict };
        var metadataJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        await _s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = BucketName,
            Key = GetNoteMetadataKey(entry),
            ContentBody = metadataJson,
            ContentType = "application/json"
        });

        _logger.LogInformation("[ForgeService] Wrote note {EntryId} to S3: {S3Key}", entry.Id, s3Key);
    }

    private async Task DeleteNoteFromS3Async(KbEntry entry)
    {
        const string BucketName = "fortress-tools";
        var s3Key = GetNoteS3Key(entry);

        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = s3Key });
        await _s3.DeleteObjectAsync(new DeleteObjectRequest { BucketName = BucketName, Key = GetNoteMetadataKey(entry) });

        _logger.LogInformation("[ForgeService] Deleted note {EntryId} from S3: {S3Key}", entry.Id, s3Key);
    }

    // ── Access Control ────────────────────────────────────────────────────────

    /// <summary>Returns true if userId can read the given entry.</summary>
    public async Task<bool> CanReadEntryAsync(Guid userId, KbEntry entry)
    {
        return entry.Tier switch
        {
            KbTier.Personal  => entry.UserId == userId,
            KbTier.Corporate => true,
            KbTier.Team      => entry.TeamId.HasValue && await IsTeamMemberAsync(userId, entry.TeamId.Value),
            _                => false
        };
    }

    /// <summary>Returns true if userId can write (update/delete) the given entry.</summary>
    public async Task<bool> CanWriteEntryAsync(Guid userId, KbEntry entry)
    {
        return entry.Tier switch
        {
            KbTier.Personal  => entry.UserId == userId,
            KbTier.Team      => entry.TeamId.HasValue && await IsTeamMemberAsync(userId, entry.TeamId.Value),
            KbTier.Corporate => false, // admin-only; not implemented yet
            _                => false
        };
    }

    /// <summary>Returns true if userId is a member of the given team.</summary>
    public async Task<bool> IsTeamMemberAsync(Guid userId, int teamId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbTeamMembers
            .AnyAsync(m => m.TeamId == teamId && m.UserId == userId);
    }

    // ── Entry Methods ─────────────────────────────────────────────────────────

    /// <summary>Returns all entries accessible to userId: personal + team + corporate.</summary>
    public async Task<List<KbEntry>> GetAccessibleEntriesAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Get teams the user is a member of
        var memberTeamIds = await db.KbTeamMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.TeamId)
            .ToListAsync();

        return await db.KbEntries
            .Where(e =>
                // Personal: own entries
                (e.Tier == KbTier.Personal && e.UserId == userId) ||
                // Team: entries in teams user belongs to
                (e.Tier == KbTier.Team && e.TeamId.HasValue && memberTeamIds.Contains(e.TeamId.Value)) ||
                // Corporate: all
                e.Tier == KbTier.Corporate)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();
    }

    /// <summary>Returns personal entries belonging to userId.</summary>
    public async Task<List<KbEntry>> GetPersonalEntriesAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbEntries
            .Where(e => e.Tier == KbTier.Personal && e.UserId == userId)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();
    }

    /// <summary>Returns team entries for a team the user is a member of.</summary>
    public async Task<List<KbEntry>> GetTeamEntriesAsync(Guid userId, int teamId)
    {
        if (!await IsTeamMemberAsync(userId, teamId))
            throw new UnauthorizedAccessException($"User {userId} is not a member of team {teamId}.");

        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbEntries
            .Where(e => e.Tier == KbTier.Team && e.TeamId == teamId)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();
    }

    /// <summary>Returns all corporate-tier entries.</summary>
    public async Task<List<KbEntry>> GetCorporateEntriesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbEntries
            .Where(e => e.Tier == KbTier.Corporate)
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();
    }

    /// <summary>Creates a new KB entry.</summary>
    public async Task<KbEntry> CreateEntryAsync(
        Guid userId,
        KbTier tier,
        string title,
        string content,
        string? tags = null,
        int? teamId = null)
    {
        // Corporate tier is admin-only; block until a proper admin role is wired up
        if (tier == KbTier.Corporate)
            throw new InvalidOperationException("Corporate KB entries can only be created by administrators. This feature is not yet available.");

        // Validate team membership for Team tier
        if (tier == KbTier.Team)
        {
            if (!teamId.HasValue)
                throw new ArgumentException("TeamId is required for Team-tier entries.");
            if (!await IsTeamMemberAsync(userId, teamId.Value))
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

        // Write note to S3 + trigger ingestion so it's retrievable by Bedrock
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
            _logger.LogInformation("[ForgeService] Note {EntryId} synced to S3 and ingestion triggered", entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ForgeService] Failed to sync note {EntryId} to S3 — note saved in DB but may not be retrievable by AI", entry.Id);
        }

        return entry;
    }

    /// <summary>Updates title, content, and tags of an existing entry. Verifies write access.</summary>
    public async Task<KbEntry> UpdateEntryAsync(Guid userId, int entryId, string title, string content, string? tags)
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
            _logger.LogInformation("[ForgeService] Note {EntryId} synced to S3 and ingestion triggered", entry.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ForgeService] Failed to sync note {EntryId} to S3 — note saved in DB but may not be retrievable by AI", entry.Id);
        }

        return entry;
    }

    /// <summary>Deletes an entry. Verifies write access.</summary>
    public async Task DeleteEntryAsync(Guid userId, int entryId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var entry = await db.KbEntries.FindAsync(entryId)
            ?? throw new KeyNotFoundException($"KbEntry {entryId} not found.");

        if (!await CanWriteEntryAsync(userId, entry))
            throw new UnauthorizedAccessException($"User {userId} cannot delete KbEntry {entryId}.");

        // Remove from S3 so Bedrock re-ingestion will exclude this note
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
            _logger.LogWarning(ex, "[ForgeService] Failed to delete note {EntryId} from S3 — stale content may remain in vector store", entryId);
        }

        db.KbEntries.Remove(entry);
        await db.SaveChangesAsync();
        _logger.LogInformation("Deleted KbEntry {Id} by user {UserId}", entryId, userId);
    }

    // ── Team Methods ──────────────────────────────────────────────────────────

    /// <summary>Returns all teams where userId is a member.</summary>
    public async Task<List<KbTeam>> GetUserTeamsAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.KbTeams
            .Where(p => db.KbTeamMembers.Any(m => m.TeamId == p.Id && m.UserId == userId))
            .Include(p => p.Members)
            .OrderBy(p => p.Name)
            .ToListAsync();
    }

    /// <summary>Creates a new team and adds the creator as an Owner member.</summary>
    public async Task<KbTeam> CreateTeamAsync(Guid userId, string name, string? description = null)
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

    /// <summary>Adds a member to a team. Requesting user must be an Owner.</summary>
    public async Task AddMemberAsync(Guid requestingUserId, int teamId, Guid newMemberId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var requester = await db.KbTeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == requestingUserId)
            ?? throw new UnauthorizedAccessException($"User {requestingUserId} is not a member of team {teamId}.");

        if (requester.Role != KbTeamRole.Owner)
            throw new UnauthorizedAccessException($"User {requestingUserId} is not an Owner of team {teamId}.");

        // Idempotent: skip if already a member
        var existing = await db.KbTeamMembers
            .AnyAsync(m => m.TeamId == teamId && m.UserId == newMemberId);

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

    /// <summary>Removes a member from a team. Requester must be Owner or the member themselves.</summary>
    public async Task RemoveMemberAsync(Guid requestingUserId, int teamId, Guid memberId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var requester = await db.KbTeamMembers
            .FirstOrDefaultAsync(m => m.TeamId == teamId && m.UserId == requestingUserId);

        // Must be Owner or removing yourself
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

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>LIKE search on Title+Content across all entries accessible to userId.</summary>
    public async Task<List<KbEntry>> SearchAsync(Guid userId, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<KbEntry>();

        await using var db = await _dbFactory.CreateDbContextAsync();

        var memberTeamIds = await db.KbTeamMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.TeamId)
            .ToListAsync();

        var pattern = $"%{query}%";

        return await db.KbEntries
            .Where(e =>
                // Access filter
                ((e.Tier == KbTier.Personal && e.UserId == userId) ||
                 (e.Tier == KbTier.Team && e.TeamId.HasValue && memberTeamIds.Contains(e.TeamId.Value)) ||
                 e.Tier == KbTier.Corporate)
                &&
                // Text search
                (EF.Functions.Like(e.Title, pattern) || EF.Functions.Like(e.Content, pattern)))
            .OrderByDescending(e => e.UpdatedAt)
            .ToListAsync();
    }
}
