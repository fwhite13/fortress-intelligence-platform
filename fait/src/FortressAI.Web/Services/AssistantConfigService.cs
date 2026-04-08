using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;

namespace FortressAI.Web.Services;

public class AssistantConfigService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<AssistantConfigService> _logger;

    public AssistantConfigService(IDbContextFactory<AppDbContext> dbFactory, ILogger<AssistantConfigService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    public async Task<UserAssistantConfig> GetOrCreateConfigAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.UserAssistantConfigs.FirstOrDefaultAsync(c => c.UserId == userId);
        if (config == null)
        {
            config = new UserAssistantConfig { UserId = userId };
            db.UserAssistantConfigs.Add(config);
            await db.SaveChangesAsync();
        }
        return config;
    }

    public async Task<UserAssistantConfig> SaveConfigAsync(Guid userId, string assistantName, string avatarId, string colorHex, string personalityPreset)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.UserAssistantConfigs.FirstOrDefaultAsync(c => c.UserId == userId);
        if (config == null)
        {
            config = new UserAssistantConfig { UserId = userId };
            db.UserAssistantConfigs.Add(config);
        }
        config.AssistantName = assistantName;
        config.AvatarId = avatarId;
        config.ColorHex = colorHex;
        config.PersonalityPreset = personalityPreset;
        config.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("Saved assistant config for user {UserId}: name={Name}, preset={Preset}", userId, assistantName, personalityPreset);
        return config;
    }

    /// <summary>Save full assistant config including FIRM integration toggles.</summary>
    public async Task<UserAssistantConfig> SaveConfigAsync(Guid userId, string assistantName, string avatarId, string colorHex, string personalityPreset, bool firmAutoTranscript, bool firmAutoSummary)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var config = await db.UserAssistantConfigs.FirstOrDefaultAsync(c => c.UserId == userId);
        if (config == null)
        {
            config = new UserAssistantConfig { UserId = userId };
            db.UserAssistantConfigs.Add(config);
        }
        config.AssistantName = assistantName;
        config.AvatarId = avatarId;
        config.ColorHex = colorHex;
        config.PersonalityPreset = personalityPreset;
        config.FirmAutoTranscript = firmAutoTranscript;
        config.FirmAutoSummary = firmAutoSummary;
        config.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _logger.LogInformation("Saved assistant config for user {UserId}: name={Name}, preset={Preset}, firmAutoTranscript={AutoTranscript}, firmAutoSummary={AutoSummary}",
            userId, assistantName, personalityPreset, firmAutoTranscript, firmAutoSummary);
        return config;
    }

    public string GetPersonalitySystemPrompt(UserAssistantConfig config, string? userDisplayName = null, string? userEmail = null)
    {
        var todayStr = DateTimeOffset.Now.ToString("dddd, MMMM d, yyyy");
        var datePrefix = $"Today's date is {todayStr}.\n\n";

        var prefix = config.PersonalityPreset switch
        {
            "formal" => $"You are a formal, professional assistant named {config.AssistantName}. Maintain a polished, respectful tone. Be thorough and precise.",
            "concise" => $"You are a concise, efficient assistant named {config.AssistantName}. Keep responses brief and actionable. No filler.",
            _ => $"You are a friendly, helpful assistant named {config.AssistantName}. Be warm and approachable while remaining professional."
        };

        prefix = datePrefix + prefix;

        if (!string.IsNullOrWhiteSpace(userDisplayName))
            prefix += $" The user's name is {userDisplayName}. Address them by name occasionally to personalize responses.";

        if (!string.IsNullOrWhiteSpace(userEmail))
            prefix += $" The authenticated user's own email address is {userEmail}. Use this as the canonical source for the current user's email — do not look it up or guess it.";

        prefix += " When asked to create, write, or generate a document or file, output the content directly in your chat response as formatted markdown — do not attempt to use tools to save it. If tool calls are needed but keep failing, explain what you tried and provide the output directly in your response.";

        prefix += "\n\nWhen you create, rewrite, or generate a document, code file, or structured content, wrap it in an artifact tag:\n<artifact type=\"markdown\" title=\"Document Title\">\n...content...\n</artifact>\nFor code, use: <artifact type=\"code\" language=\"python\" title=\"Script Name\">\nFor plain text, use: <artifact type=\"text\" title=\"Note Title\">\nYou cannot save files to disk or modify the Knowledge Base. Artifacts are the correct output format for any document you produce.";

        return prefix;
    }

    // Briefing schedule methods
    public async Task<UserBriefingSchedule?> GetBriefingScheduleAsync(Guid userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserBriefingSchedules.FirstOrDefaultAsync(s => s.UserId == userId);
    }

    public async Task<UserBriefingSchedule> SaveBriefingScheduleAsync(Guid userId, TimeOnly deliveryTimeUtc, bool emailDigestEnabled)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var schedule = await db.UserBriefingSchedules.FirstOrDefaultAsync(s => s.UserId == userId);
        if (schedule == null)
        {
            schedule = new UserBriefingSchedule { UserId = userId };
            db.UserBriefingSchedules.Add(schedule);
        }
        schedule.DeliveryTimeUtc = deliveryTimeUtc;
        schedule.EmailDigestEnabled = emailDigestEnabled;
        await db.SaveChangesAsync();
        _logger.LogInformation("Saved briefing schedule for user {UserId}: time={Time}UTC, email={Email}", userId, deliveryTimeUtc, emailDigestEnabled);
        return schedule;
    }
}
