using Amazon.S3;
using Amazon.S3.Model;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using FortressAI.V2.Web.Services.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FortressAI.V2.Web.Services;

public class UserProvisioningService : IUserProvisioningService
{
    private readonly FaitV2DbContext _db;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<UserProvisioningService> _logger;

    // ── Template constants ────────────────────────────────────────────────
    private const string SoulMdTemplate = """
        # SOUL.md — {DisplayName}'s Assistant

        ## Identity
        I am your personal AI assistant on the Fortress Intelligence Platform.

        ## Purpose
        I help you work smarter — drafting, researching, analyzing, and executing complex tasks.

        ## Personality
        Precise, proactive, and honest. I surface what matters and flag what's uncertain.
        """;

    private const string UserMdTemplate = """
        # USER.md — About {DisplayName}

        - **Name:** {DisplayName}
        - **Email:** {Email}
        - **Timezone:** (set during onboarding)
        """;

    private const string AgentsMdTemplate = """
        # AGENTS.md

        ## Your Workspace

        This is your persistent AI assistant on the Fortress Intelligence Platform.

        ## Capabilities

        - Chat and answer questions
        - Draft documents, emails, and reports
        - Analyze data and research topics
        - Execute complex multi-step tasks via CC sandbox
        """;

    private const string MemoryMdTemplate = """
        # MEMORY.md

        _Your assistant's long-term memory. Updated as you work together._
        """;

    // ── Default memory topics ─────────────────────────────────────────────
    private static readonly (string Slug, string Name, string FileName)[] DefaultTopics =
    [
        ("soul",   "Assistant Identity",   "assistants/SOUL.md"),
        ("user",   "About You",            "assistants/USER.md"),
        ("memory", "Long-Term Memory",     "memory/MEMORY.md"),
        ("agents", "Assistant Agents",     "assistants/AGENTS.md"),
    ];

    public UserProvisioningService(
        FaitV2DbContext db,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<UserProvisioningService> logger)
    {
        _db = db;
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    // ── Public entry point ────────────────────────────────────────────────
    public async Task<ProvisioningResult> ProvisionAsync(
        string userId,
        string entraOid,
        string email,
        string displayName,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(userId, out _))
            throw new ArgumentException($"userId must be a valid GUID, got: {userId}", nameof(userId));

        // Step 1 — Idempotency check
        var existing = await _db.Users
            .Include(u => u.MainAssistant)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (existing?.OnboardingCompletedAt != null)
        {
            var existingPrefix = existing.MainAssistant?.WorkspaceS3Prefix
                ?? $"workspaces/{userId}/";
            _logger.LogInformation("User {UserId} already provisioned — skipping", userId);
            return new ProvisioningResult(false, existingPrefix, GetPgSchemaName(userId));
        }

        var now = DateTime.UtcNow;
        var s3Prefix = $"workspaces/{userId}/";
        var bucket = _config["AWS:WorkspaceBucket"]
            ?? throw new InvalidOperationException("AWS:WorkspaceBucket not configured");
        var pgConnString = _config.GetConnectionString("PostgresConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:PostgresConnection not configured");
        var schemaName = GetPgSchemaName(userId);

        // Track what we've successfully created for rollback
        var s3ObjectsWritten = new List<string>();
        var pgSchemaCreated = false;

        // Per-step diagnostic flags (set ONLY after each step fully completes)
        var s3Complete = false;        // set after ALL 4 S3 files written
        var pgComplete = false;        // set after CreatePgSchemaAsync returns
        var auroraAddComplete = false;  // set after _db.MainAssistants.Add() (step 5)
        var seedComplete = false;       // set after memory_topics loop completes (step 6)

        try
        {
            // Step 2 — Upsert users record
            User user;
            if (existing == null)
            {
                user = new User
                {
                    Id = userId,
                    EntraOid = entraOid,
                    Email = email,
                    DisplayName = displayName,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                _db.Users.Add(user);
            }
            else
            {
                user = existing;
                user.EntraOid = entraOid;
                user.Email = email;
                user.DisplayName = displayName;
                user.UpdatedAt = now;
            }

            // Step 3 — Write initial memory files to S3
            _logger.LogInformation("Writing S3 workspace files for user {UserId} at {Prefix}", userId, s3Prefix);

            var files = new Dictionary<string, string>
            {
                [$"{s3Prefix}assistants/SOUL.md"]   = SoulMdTemplate.Replace("{DisplayName}", displayName),
                [$"{s3Prefix}assistants/USER.md"]   = UserMdTemplate
                    .Replace("{DisplayName}", displayName)
                    .Replace("{Email}", email),
                [$"{s3Prefix}assistants/AGENTS.md"] = AgentsMdTemplate,
                [$"{s3Prefix}memory/MEMORY.md"]     = MemoryMdTemplate,
            };

            foreach (var (key, content) in files)
            {
                await _s3.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucket,
                    Key = key,
                    ContentBody = content,
                    ContentType = "text/markdown"
                }, ct);
                s3ObjectsWritten.Add(key);
            }
            s3Complete = true;

            // Step 4 — RDS PostgreSQL per-user schema
            _logger.LogInformation("Creating PostgreSQL schema {Schema} for user {UserId}", schemaName, userId);
            await CreatePgSchemaAsync(pgConnString, schemaName, ct);
            pgSchemaCreated = true;
            pgComplete = true;

            // Step 5 — Aurora main_assistants record
            var assistant = new MainAssistant
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                SoulBlobPath = $"{s3Prefix}assistants/SOUL.md",
                MemoryBlobPath = $"{s3Prefix}memory/MEMORY.md",
                WorkspaceS3Prefix = s3Prefix,
                FargateSessionId = null,
                FargateTaskArn = null,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.MainAssistants.Add(assistant);
            auroraAddComplete = true;

            // Step 6 — Seed memory_topics rows
            foreach (var (slug, name, fileName) in DefaultTopics)
            {
                // Idempotent: skip if already exists
                var topicExists = await _db.MemoryTopics
                    .AnyAsync(t => t.UserId == userId && t.TopicSlug == slug, ct);
                if (!topicExists)
                {
                    _db.MemoryTopics.Add(new MemoryTopic
                    {
                        Id = Guid.NewGuid().ToString(),
                        UserId = userId,
                        TopicName = name,
                        TopicSlug = slug,
                        BlobPath = $"{s3Prefix}{fileName}",
                        CreatedAt = now,
                        LastUpdatedAt = now
                    });
                }
            }
            seedComplete = true;

            // Step 7 — Mark onboarding complete
            user.OnboardingCompletedAt = now;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "User {UserId} provisioned successfully. S3 prefix: {Prefix}, PG schema: {Schema}",
                userId, s3Prefix, schemaName);

            return new ProvisioningResult(true, s3Prefix, schemaName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provisioning failed for user {UserId} at step — attempting rollback", userId);

            // Rollback: PG schema (if created)
            if (pgSchemaCreated)
            {
                try
                {
                    _logger.LogWarning("Rolling back PostgreSQL schema {Schema}", schemaName);
                    await DropPgSchemaAsync(pgConnString, schemaName, ct);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(rollbackEx, "PG schema rollback failed for schema {Schema} — manual cleanup required", schemaName);
                }
            }

            // Rollback: S3 objects (if written)
            if (s3ObjectsWritten.Count > 0)
            {
                _logger.LogWarning("Rolling back {Count} S3 objects at prefix {Prefix}", s3ObjectsWritten.Count, s3Prefix);
                foreach (var key in s3ObjectsWritten)
                {
                    try
                    {
                        await _s3.DeleteObjectAsync(new DeleteObjectRequest
                        {
                            BucketName = bucket,
                            Key = key
                        });
                    }
                    catch (Exception s3Ex)
                    {
                        _logger.LogError(s3Ex, "Failed to delete S3 object {Key} during rollback — manual cleanup required", key);
                    }
                }
            }

            // Rollback: EF Core tracked changes (discard)
            foreach (var entry in _db.ChangeTracker.Entries())
            {
                entry.State = EntityState.Detached;
            }

            throw new ProvisioningException(
                userId,
                !s3Complete ? "s3-write"
                    : !pgComplete ? "pg-schema"
                    : !auroraAddComplete ? "aurora-record"
                    : !seedComplete ? "memory-topics-seed"
                    : "aurora-save",
                $"Provisioning failed for user {userId}: {ex.Message}",
                ex);
        }
    }

    // ── PG helpers ────────────────────────────────────────────────────────

    private static string GetPgSchemaName(string userId) =>
        "user_" + userId.Replace("-", "_");

    private static async Task CreatePgSchemaAsync(string connString, string schemaName, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);

        // Enable pgvector extension (idempotent)
        await using (var cmd = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector;", conn))
            await cmd.ExecuteNonQueryAsync(ct);

        // Schema (idempotent)
        await using (var cmd = new NpgsqlCommand($"""CREATE SCHEMA IF NOT EXISTS "{schemaName}";""", conn))
            await cmd.ExecuteNonQueryAsync(ct);

        // memory_chunks table
        await using (var cmd = new NpgsqlCommand($"""
            CREATE TABLE IF NOT EXISTS "{schemaName}".memory_chunks (
                id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                topic_slug VARCHAR(200) NOT NULL,
                chunk_text TEXT NOT NULL,
                embedding vector(1536),
                created_at TIMESTAMP(6) NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMP(6) NOT NULL DEFAULT NOW()
            );
            """, conn))
            await cmd.ExecuteNonQueryAsync(ct);

        // memory_topics table
        await using (var cmd = new NpgsqlCommand($"""
            CREATE TABLE IF NOT EXISTS "{schemaName}".memory_topics (
                topic_slug VARCHAR(200) PRIMARY KEY,
                topic_name VARCHAR(200) NOT NULL,
                blob_path VARCHAR(500) NOT NULL,
                last_indexed_at TIMESTAMP(6)
            );
            """, conn))
            await cmd.ExecuteNonQueryAsync(ct);

        // Index on topic_slug
        await using (var cmd = new NpgsqlCommand($"""
            CREATE INDEX IF NOT EXISTS idx_memory_chunks_topic
                ON "{schemaName}".memory_chunks(topic_slug);
            """, conn))
            await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DropPgSchemaAsync(string connString, string schemaName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand($"""DROP SCHEMA IF EXISTS "{schemaName}" CASCADE;""", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
