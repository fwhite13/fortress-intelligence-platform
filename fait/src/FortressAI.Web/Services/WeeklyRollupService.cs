using Microsoft.EntityFrameworkCore;
using FortressAI.Shared.Models;
using FortressAI.Web.Data;
using System.Text;

namespace FortressAI.Web.Services;

/// <summary>
/// Aggregates the past 7 days of FAIT activity and synthesizes a narrative weekly rollup
/// via Bedrock. Generated on-demand; not persisted (can be regenerated anytime).
/// </summary>
public class WeeklyRollupService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly BedrockService _bedrock;
    private readonly AssistantConfigService _configSvc;
    private readonly ILogger<WeeklyRollupService> _logger;

    public WeeklyRollupService(
        IDbContextFactory<AppDbContext> dbFactory,
        BedrockService bedrock,
        AssistantConfigService configSvc,
        ILogger<WeeklyRollupService> logger)
    {
        _dbFactory = dbFactory;
        _bedrock = bedrock;
        _configSvc = configSvc;
        _logger = logger;
    }

    /// <summary>
    /// Generates a weekly rollup for the given user covering the past 7 days.
    /// </summary>
    public async Task<WeeklyRollup> GenerateRollupAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var weekStart = now.AddDays(-7);

        _logger.LogInformation("[WeeklyRollup] Generating rollup for user {UserId} ({WeekStart} – {WeekEnd})",
            userId, weekStart.ToString("yyyy-MM-dd"), now.ToString("yyyy-MM-dd"));

        await using var db = await _dbFactory.CreateDbContextAsync();

        // 1. Briefings delivered this week
        var briefings = await db.BriefingHistories
            .Where(b => b.UserId == userId && b.CreatedAt >= weekStart)
            .OrderBy(b => b.BriefingDate)
            .ToListAsync();

        // 2. Email alerts this week
        var emailAlerts = await db.EmailAlerts
            .Where(e => e.UserId == userId && e.CreatedAt >= weekStart)
            .ToListAsync();

        // 3. Post-meeting notes this week
        var meetingNotes = await db.PostMeetingNotes
            .Where(n => n.UserId == userId && n.CreatedAt >= weekStart)
            .ToListAsync();

        // 4. KB entries created this week
        var kbEntries = await db.KbEntries
            .Where(e => e.UserId == userId && e.CreatedAt >= weekStart)
            .ToListAsync();

        // 5. Build stats
        var stats = new RollupStats
        {
            BriefingsDelivered    = briefings.Count,
            EmailAlertsTriaged    = emailAlerts.Count,
            HighPriorityEmails    = emailAlerts.Count(e => e.Importance == "HIGH"),
            MeetingNotesCaptures  = meetingNotes.Count,
            MeetingsSummarized    = meetingNotes.Count(n => n.Summary != null),
            KbEntriesAdded        = kbEntries.Count,
            WeekStart             = DateOnly.FromDateTime(weekStart),
            WeekEnd               = DateOnly.FromDateTime(now)
        };

        _logger.LogInformation(
            "[WeeklyRollup] Stats — Briefings:{B} Emails:{E} HighPri:{H} MeetingNotes:{M} Summarized:{S} KB:{K}",
            stats.BriefingsDelivered, stats.EmailAlertsTriaged, stats.HighPriorityEmails,
            stats.MeetingNotesCaptures, stats.MeetingsSummarized, stats.KbEntriesAdded);

        // 6. Get assistant config for narrative tone
        var config = await _configSvc.GetOrCreateConfigAsync(userId);

        // 7. Bedrock synthesis — generate narrative summary
        var narrative = await SynthesizeNarrativeAsync(
            stats, briefings, emailAlerts, meetingNotes, kbEntries, config.AssistantName);

        return new WeeklyRollup
        {
            UserId      = userId,
            GeneratedAt = now,
            Stats       = stats,
            Narrative   = narrative,
            WeekStart   = stats.WeekStart,
            WeekEnd     = stats.WeekEnd
        };
    }

    /// <summary>
    /// Calls Bedrock to generate a 2–3 sentence warm narrative summary of the week's activity.
    /// Includes top highlights extracted from actual data when available. Max 400 tokens.
    /// </summary>
    private async Task<string> SynthesizeNarrativeAsync(
        RollupStats stats,
        List<BriefingHistory> briefings,
        List<EmailAlert> emailAlerts,
        List<PostMeetingNote> meetingNotes,
        List<KbEntry> kbEntries,
        string assistantName)
    {
        var sb = new StringBuilder();

        var todayStr = DateTimeOffset.Now.ToString("dddd, MMMM d, yyyy");
        var weekStartStr = stats.WeekStart.ToString("MMMM d, yyyy");
        var weekEndStr = stats.WeekEnd.ToString("MMMM d, yyyy");
        sb.AppendLine($"Today is {todayStr}. You are summarizing activity from {weekStartStr} to {weekEndStr}.");
        sb.AppendLine($"You are {assistantName}, a personal AI assistant. Generate a concise weekly summary for your user.");
        sb.AppendLine();
        sb.AppendLine($"## This Week's Activity ({stats.WeekStart} – {stats.WeekEnd})");
        sb.AppendLine();
        sb.AppendLine($"📧 Email: {stats.EmailAlertsTriaged} emails triaged, {stats.HighPriorityEmails} flagged as high priority");
        sb.AppendLine($"📅 Meetings: {stats.MeetingNotesCaptures} post-meeting notes captured, {stats.MeetingsSummarized} AI summaries generated");
        sb.AppendLine($"📋 Briefings: {stats.BriefingsDelivered} morning briefings delivered");
        sb.AppendLine($"🧠 Knowledge Base: {stats.KbEntriesAdded} new entries added");

        // Inject top highlights if available
        var highlights = new List<string>();

        var topEmails = emailAlerts
            .Where(e => e.Importance == "HIGH" && !string.IsNullOrWhiteSpace(e.Subject))
            .Take(2)
            .Select(e => $"Email: \"{e.Subject}\" from {e.SenderEmail}");
        highlights.AddRange(topEmails);

        var topMeetings = meetingNotes
            .Where(n => !string.IsNullOrWhiteSpace(n.EventSubject))
            .Take(2)
            .Select(n => $"Meeting: \"{n.EventSubject}\"");
        highlights.AddRange(topMeetings);

        var topKb = kbEntries
            .Where(e => !string.IsNullOrWhiteSpace(e.Title))
            .Take(1)
            .Select(e => $"KB Entry: \"{e.Title}\"");
        highlights.AddRange(topKb);

        if (highlights.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Notable Items This Week");
            foreach (var h in highlights.Take(3))
                sb.AppendLine($"- {h}");
        }

        sb.AppendLine();
        sb.AppendLine("Write a 2-3 sentence narrative summary of the week. Be warm but concise. " +
                      "Mention specific highlights if available. End with one forward-looking note.");

        var prompt = sb.ToString();

        _logger.LogInformation("[WeeklyRollup] Invoking Bedrock for narrative synthesis ({PromptLen} chars)", prompt.Length);

        try
        {
            var narrative = await _bedrock.InvokeClaudeAsync(
                prompt: prompt,
                maxTokens: 400,
                systemPrompt: $"You are {assistantName}, a concise and warm personal AI assistant.");

            return narrative;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WeeklyRollup] Bedrock synthesis failed for user");
            return $"Unable to generate narrative summary. " +
                   $"This week: {stats.BriefingsDelivered} briefings, {stats.EmailAlertsTriaged} emails triaged, " +
                   $"{stats.MeetingNotesCaptures} meeting notes captured.";
        }
    }
}

/// <summary>
/// The generated weekly rollup — stats + narrative. Generated on-demand, not persisted.
/// </summary>
public class WeeklyRollup
{
    public Guid UserId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public RollupStats Stats { get; set; } = new();
    public string Narrative { get; set; } = "";
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
}

/// <summary>
/// Activity counts for the past 7 days, used to build the rollup.
/// </summary>
public class RollupStats
{
    public int BriefingsDelivered { get; set; }
    public int EmailAlertsTriaged { get; set; }
    public int HighPriorityEmails { get; set; }
    public int MeetingNotesCaptures { get; set; }
    public int MeetingsSummarized { get; set; }
    public int KbEntriesAdded { get; set; }
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
}
