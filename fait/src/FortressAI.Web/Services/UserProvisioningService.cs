using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using System.Text.Json;

namespace FortressAI.Web.Services;

public class UserProvisioningService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAmazonS3 _s3;
    private readonly IConfiguration _config;
    private readonly ILogger<UserProvisioningService> _logger;

    // S3 bucket: same as harness uses — WORKSPACE_S3_BUCKET env var, default fortress-user-workspaces
    private string BucketName => _config["WORKSPACE_S3_BUCKET"] ?? "fortress-user-workspaces";
    private string S3Prefix => _config["WORKSPACE_S3_PREFIX"] ?? "";

    public UserProvisioningService(
        IDbContextFactory<AppDbContext> dbFactory,
        IAmazonS3 s3,
        IConfiguration config,
        ILogger<UserProvisioningService> logger)
    {
        _dbFactory = dbFactory;
        _s3 = s3;
        _config = config;
        _logger = logger;
    }

    public async Task ProvisionAsync(Guid userId)
    {
        // Idempotency: if already provisioned, return
        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) throw new InvalidOperationException($"User {userId} not found");
        if (user.OnboardingCompletedAt.HasValue)
        {
            _logger.LogInformation("[Provision] User {UserId} already provisioned — skipping", userId);
            return;
        }

        var config = await db.UserAssistantConfigs.FirstOrDefaultAsync(c => c.UserId == userId);
        if (config == null) throw new InvalidOperationException($"UserAssistantConfig not found for {userId}");

        // Step 2: write S3 files — track written keys for rollback
        var writtenKeys = new List<string>();
        try
        {
            var userIdStr = userId.ToString();
            var prefix = $"{S3Prefix}workspaces/{userIdStr}/";

            // SOUL.md
            var soulContent = BuildSoulMd(config, user.DisplayName ?? "User");
            await WriteS3Async($"{prefix}assistants/SOUL.md", soulContent);
            writtenKeys.Add($"{prefix}assistants/SOUL.md");

            // USER.md
            var userContent = BuildUserMd(config, user.DisplayName ?? "User");
            await WriteS3Async($"{prefix}assistants/USER.md", userContent);
            writtenKeys.Add($"{prefix}assistants/USER.md");

            // AGENTS.md (static boilerplate)
            await WriteS3Async($"{prefix}assistants/AGENTS.md", AgentsBoilerplate);
            writtenKeys.Add($"{prefix}assistants/AGENTS.md");

            // MEMORY.md (empty index)
            var memContent = BuildMemoryMd(config);
            await WriteS3Async($"{prefix}memory/MEMORY.md", memContent);
            writtenKeys.Add($"{prefix}memory/MEMORY.md");

            _logger.LogInformation("[Provision] S3 files written for user {UserId}", userId);
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.ErrorCode == "AccessDenied")
        {
            _logger.LogError(ex, "[Provision] AccessDeniedException writing S3 for user {UserId} — halting", userId);
            throw; // halt and report — do NOT proceed to DB writes
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Provision] S3 write failed for user {UserId} — rolling back", userId);
            // Rollback: delete written S3 files
            foreach (var key in writtenKeys)
            {
                try { await _s3.DeleteObjectAsync(BucketName, key); }
                catch (Exception delEx) { _logger.LogWarning(delEx, "[Provision] Rollback: failed to delete {Key}", key); }
            }
            throw;
        }

        // Step 3: write initial memory_topics rows — SKIPPED (table not in v1 schema)

        // Step 4: set onboarding_completed_at
        user.OnboardingCompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("[Provision] Provisioning complete for user {UserId}", userId);
    }

    private async Task WriteS3Async(string key, string content)
    {
        var request = new PutObjectRequest
        {
            BucketName = BucketName,
            Key = key,
            ContentBody = content,
            ContentType = "text/markdown"
        };
        await _s3.PutObjectAsync(request);
        _logger.LogDebug("[Provision] Wrote s3://{Bucket}/{Key}", BucketName, key);
    }

    private static string BuildSoulMd(UserAssistantConfig config, string displayName)
    {
        var name = config.AssistantName ?? "Assistant";
        var preset = config.PersonalityPreset switch
        {
            "formal" => "formal and professional — polished, respectful, precise",
            "concise" => "concise and efficient — brief, actionable, no filler",
            _ => "friendly and helpful — warm, approachable, professional"
        };
        var commStyle = string.IsNullOrWhiteSpace(config.CommunicationStyle)
            ? "balanced" : config.CommunicationStyle;
        var respFormat = string.IsNullOrWhiteSpace(config.ResponseFormat)
            ? "mixed" : config.ResponseFormat;

        return $"""
# {name} — Identity

You are {name}, {displayName}'s personal AI assistant.

## Personality
{preset}

## Communication Style
{commStyle}

## Response Format
{respFormat}
""";
    }

    private static string BuildUserMd(UserAssistantConfig config, string displayName)
    {
        var preferred = string.IsNullOrWhiteSpace(config.PreferredName) ? displayName : config.PreferredName;
        var role = string.IsNullOrWhiteSpace(config.Role) ? "(not specified)" : config.Role;
        var responsibilities = string.IsNullOrWhiteSpace(config.Responsibilities) ? "(not specified)" : config.Responsibilities;
        var commStyle = string.IsNullOrWhiteSpace(config.CommunicationStyle) ? "balanced" : config.CommunicationStyle;
        var respFormat = string.IsNullOrWhiteSpace(config.ResponseFormat) ? "mixed" : config.ResponseFormat;
        var additionalContext = string.IsNullOrWhiteSpace(config.AdditionalContext) ? "" : $"\n## Additional Context\n{config.AdditionalContext}";

        // Parse use cases from JSON array
        var useCasesList = "(not specified)";
        if (!string.IsNullOrWhiteSpace(config.UseCasesJson))
        {
            try
            {
                var cases = JsonSerializer.Deserialize<List<string>>(config.UseCasesJson);
                if (cases?.Count > 0)
                    useCasesList = string.Join("\n", cases.Select(c => $"- {c}"));
            }
            catch { /* ignore parse errors */ }
        }

        return $"""
# About {displayName}

## Role
{role}

## Responsibilities
{responsibilities}

## Preferences
- Preferred name: {preferred}
- Communication style: {commStyle}
- Response format: {respFormat}

## Use Cases
{useCasesList}{additionalContext}
""";
    }

    private static string BuildMemoryMd(UserAssistantConfig config)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC";

        // Seed use cases section if available
        var useCasesSection = "";
        if (!string.IsNullOrWhiteSpace(config.UseCasesJson))
        {
            try
            {
                var cases = JsonSerializer.Deserialize<List<string>>(config.UseCasesJson);
                if (cases?.Count > 0)
                    useCasesSection = $"\n## Use Cases\n{string.Join("\n", cases.Select(c => $"- {c}"))}\n";
            }
            catch { /* ignore */ }
        }

        return $"""
# Memory Index
_Last updated: {timestamp}_
{useCasesSection}
## Topics
(populated as memory grows)
""";
    }

    private const string AgentsBoilerplate = """
# AGENTS.md — Harness Configuration

This file contains operational configuration for the AI assistant harness.

## Available Tools
- search_knowledge_base: Search the user's personal knowledge base
- list_workspace_files: List files in the user's S3 workspace
- MS365 Graph tools: email, calendar (when configured)
- ADO tools: work item management (when configured)
- Brave Search: web search

## Memory
User memory is stored in workspaces/{userId}/memory/MEMORY.md.
Update MEMORY.md when the user states persistent preferences or facts about themselves.

## Workspace
User workspace files are stored in workspaces/{userId}/artifacts/.
""";
}
