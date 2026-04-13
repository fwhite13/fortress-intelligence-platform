using System.Text.Json;
using FortressIntelligenceRM.Web.Data;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Schema;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace FortressIntelligenceRM.Web.Services;

public interface IFirmBotService
{
    Task StoreInstallationAsync(string? teamId, string? teamName, string? channelId,
        string? channelName, string conversationReferenceJson, string serviceUrl, string? tenantId);
    Task RemoveInstallationAsync(string? teamId, string? channelId);
    Task PostToChannelAsync(string teamId, string channelId, string content, string docType);
    Task<List<BotInstallationInfo>> GetInstallationsAsync();
    Task<List<ChannelPostHistoryItem>> GetChannelPostHistoryAsync(long meetingId);
    Task PostMeetingToChannelAsync(long meetingId, Guid initiatedByUserId, string teamId, string teamName, string channelId, string channelName, string docType);
}

public record BotInstallationInfo(long Id, string TeamId, string TeamName, string ChannelId, string ChannelName);
public record ChannelPostHistoryItem(string TeamName, string ChannelName, string DocType, DateTime PostedAt, bool Success);

public class FirmBotService : IFirmBotService
{
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;
    private readonly IBotFrameworkHttpAdapter _adapter;
    private readonly IConfiguration _config;
    private readonly S3Service _s3Service;
    private readonly ILogger<FirmBotService> _logger;

    public FirmBotService(
        IDbContextFactory<FirmDbContext> dbFactory,
        IBotFrameworkHttpAdapter adapter,
        IConfiguration config,
        S3Service s3Service,
        ILogger<FirmBotService> logger)
    {
        _dbFactory = dbFactory;
        _adapter = adapter;
        _config = config;
        _s3Service = s3Service;
        _logger = logger;
    }

    public async Task StoreInstallationAsync(string? teamId, string? teamName, string? channelId,
        string? channelName, string conversationReferenceJson, string serviceUrl, string? tenantId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var sql = @"INSERT INTO firm_bot_installations
            (team_id, team_name, channel_id, channel_name, conversation_reference, service_url, tenant_id)
            VALUES (@teamId, @teamName, @channelId, @channelName, @convRef, @serviceUrl, @tenantId)
            ON DUPLICATE KEY UPDATE
                team_name = VALUES(team_name),
                channel_name = VALUES(channel_name),
                conversation_reference = VALUES(conversation_reference),
                service_url = VALUES(service_url),
                tenant_id = VALUES(tenant_id)";
        await db.Database.ExecuteSqlRawAsync(sql,
            new MySqlParameter("@teamId", teamId ?? ""),
            new MySqlParameter("@teamName", teamName ?? ""),
            new MySqlParameter("@channelId", channelId ?? ""),
            new MySqlParameter("@channelName", channelName ?? ""),
            new MySqlParameter("@convRef", conversationReferenceJson),
            new MySqlParameter("@serviceUrl", serviceUrl),
            new MySqlParameter("@tenantId", tenantId ?? ""));
        _logger.LogInformation("Bot installation stored: team={TeamId} channel={ChannelId}", teamId, channelId);
    }

    public async Task RemoveInstallationAsync(string? teamId, string? channelId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM firm_bot_installations WHERE team_id = @teamId AND channel_id = @channelId",
            new MySqlParameter("@teamId", teamId ?? ""),
            new MySqlParameter("@channelId", channelId ?? ""));
        _logger.LogInformation("Bot installation removed: team={TeamId} channel={ChannelId}", teamId, channelId);
    }

    public async Task PostToChannelAsync(string teamId, string channelId, string content, string docType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var rows = await db.Database
            .SqlQueryRaw<BotInstallationRow>(
                "SELECT conversation_reference AS ConversationReferenceJson FROM firm_bot_installations WHERE team_id = {0} AND channel_id = {1}",
                teamId, channelId)
            .ToListAsync();

        if (!rows.Any())
            throw new InvalidOperationException($"Bot not installed in team {teamId} channel {channelId}");

        var conversationReference = JsonSerializer.Deserialize<ConversationReference>(
            rows[0].ConversationReferenceJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize conversation reference");

        var botAppId = _config["Firm:BotAppId"] ?? "";

        if (_adapter is CloudAdapter cloudAdapter)
        {
            await cloudAdapter.ContinueConversationAsync(
                botAppId,
                conversationReference,
                async (turnContext, ct) =>
                {
                    var message = Activity.CreateMessageActivity();
                    message.Text = $"**FIRM Meeting {docType}**\n\n{content}";
                    await turnContext.SendActivityAsync(message, ct);
                },
                CancellationToken.None);
        }
        else
        {
            _logger.LogError("Adapter is not a CloudAdapter — cannot send proactive message");
            throw new InvalidOperationException("Bot adapter does not support proactive messaging");
        }
    }

    public async Task<List<BotInstallationInfo>> GetInstallationsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Database
            .SqlQueryRaw<BotInstallationRaw>(
                "SELECT id AS Id, team_id AS TeamId, team_name AS TeamName, channel_id AS ChannelId, channel_name AS ChannelName FROM firm_bot_installations ORDER BY installed_at DESC")
            .ToListAsync();
        return rows.Select(r => new BotInstallationInfo(r.Id, r.TeamId, r.TeamName, r.ChannelId, r.ChannelName)).ToList();
    }

    public async Task<List<ChannelPostHistoryItem>> GetChannelPostHistoryAsync(long meetingId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var rows = await db.Database
            .SqlQueryRaw<ChannelPostHistoryRow2>(
                "SELECT team_name AS TeamName, channel_name AS ChannelName, doc_type AS DocType, posted_at AS PostedAt, success AS Success FROM firm_meeting_channel_posts WHERE meeting_id = {0} ORDER BY posted_at DESC",
                meetingId)
            .ToListAsync();
        return rows.Select(r => new ChannelPostHistoryItem(r.TeamName, r.ChannelName, r.DocType, r.PostedAt, r.Success)).ToList();
    }

    public async Task PostMeetingToChannelAsync(long meetingId, Guid initiatedByUserId, string teamId, string teamName, string channelId, string channelName, string docType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var meeting = await db.Meetings.FirstOrDefaultAsync(m => m.Id == meetingId);
        if (meeting == null) throw new InvalidOperationException($"Meeting {meetingId} not found");

        string content;
        if (docType == "transcript")
        {
            var transcriptKey = meeting.TranscriptS3Key ?? "";
            content = string.IsNullOrEmpty(transcriptKey) ? "" : await _s3Service.GetTranscriptTextAsync(transcriptKey);
        }
        else
        {
            var summary = await db.Summaries.OrderByDescending(s => s.CreatedAt).FirstOrDefaultAsync(s => s.MeetingId == meetingId);
            content = summary?.SummaryText ?? "";
        }

        await PostToChannelAsync(teamId, channelId, content, docType);

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO firm_meeting_channel_posts (meeting_id, initiated_by, team_id, team_name, channel_id, channel_name, doc_type, success) VALUES (@p0, @p1, @p2, @p3, @p4, @p5, @p6, 1)",
            new MySqlParameter("@p0", meetingId),
            new MySqlParameter("@p1", initiatedByUserId.ToString()),
            new MySqlParameter("@p2", teamId),
            new MySqlParameter("@p3", teamName),
            new MySqlParameter("@p4", channelId),
            new MySqlParameter("@p5", channelName),
            new MySqlParameter("@p6", docType));
    }
}

// Internal projection types for raw SQL queries
internal class ChannelPostHistoryRow2
{
    public string TeamName { get; set; } = "";
    public string ChannelName { get; set; } = "";
    public string DocType { get; set; } = "";
    public DateTime PostedAt { get; set; }
    public bool Success { get; set; }
}

internal class BotInstallationRow
{
    public string ConversationReferenceJson { get; set; } = "";
}

internal class BotInstallationRaw
{
    public long Id { get; set; }
    public string TeamId { get; set; } = "";
    public string TeamName { get; set; } = "";
    public string ChannelId { get; set; } = "";
    public string ChannelName { get; set; } = "";
}
