using Amazon.ECS;
using Amazon.ECS.Model;
using FortressIntelligenceRM.Web.Data;
using FortressIntelligenceRM.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FortressIntelligenceRM.Web.Services;

public class VpBotService
{
    private readonly IAmazonECS _ecs;
    private readonly IConfiguration _config;
    private readonly ILogger<VpBotService> _logger;
    private readonly MeetingService _meetingService;
    private readonly BrandingConfig _branding;
    private readonly IDbContextFactory<FirmDbContext> _dbFactory;

    // NOTE: inject the concrete BrandingConfig singleton (registered in Program.cs via
    // builder.Services.AddSingleton(branding) after binding config section "Branding"),
    // NOT IOptions<BrandingConfig>. Nothing ever calls .Configure<BrandingConfig>() /
    // AddOptions<BrandingConfig>(), so IOptions<BrandingConfig>.Value silently resolves
    // to bare class defaults (OrgName="Fortress") regardless of any env var/config —
    // this previously made every bot join as "Fortress Notetaker" on every deployment,
    // including RN, no matter what Branding__* env vars were set.
    public VpBotService(IAmazonECS ecs, IConfiguration config, ILogger<VpBotService> logger, MeetingService meetingService, BrandingConfig branding, IDbContextFactory<FirmDbContext> dbFactory)
    {
        _ecs = ecs;
        _config = config;
        _logger = logger;
        _meetingService = meetingService;
        _branding = branding;
        _dbFactory = dbFactory;
    }

    public async Task<string?> TriggerBotAsync(long meetingId, string meetingUrl, string platform = "teams")
    {
        var taskDef = _config["Firm:VpBotTaskDefinition"];
        var cluster = _config["Firm:EcsCluster"];
        var subnetId = _config["Firm:VpBotSubnetId"];
        var securityGroupId = _config["Firm:VpBotSecurityGroupId"];
        var botSecret = _config["Firm:BotCallbackSecret"] ?? "";
        var containerName = _config["Firm:VpBotContainerName"] ?? "firm-vpbot";
        var botDisplayName = _branding.NotetakerName;

        // WI #7009: BOT_JOIN_NAME is a separate, per-deployment-configurable name used ONLY
        // for the join-form display name (Zoom's name-based bot detection swaps the Join
        // button for "Sign in to join" when it sees names like "Refuge Notetaker"). It falls
        // back to BOT_DISPLAY_NAME when Firm:BotJoinName isn't set, so behavior is unchanged
        // until someone sets the new config key — no code deploy required to change it later.
        var botJoinNameBase = _config["Firm:BotJoinName"] ?? botDisplayName;
        var userFirstName = await GetUserFirstNameAsync(meetingId);
        var botJoinName = string.IsNullOrWhiteSpace(userFirstName)
            ? botJoinNameBase
            : $"{botJoinNameBase} - {userFirstName}";

        // WI #7033: TriggerBotAsync is only ever called for a primary recorder (subscribers never
        // launch a bot — see MeetingsApiController.AutoJoinTrigger's guard), so meetingId here is
        // always the primary's id. Resolve every user (primary + subscribers) attributed to this
        // meeting for the Phase 2 join-notification chat message.
        var botNamesCsv = await GetBotNamesCsvAsync(meetingId);

        if (string.IsNullOrEmpty(taskDef) || string.IsNullOrEmpty(cluster))
        {
            _logger.LogWarning("FIRM: VpBotTaskDefinition or EcsCluster not configured. Skipping ECS RunTask.");
            return null;
        }

        try
        {
            var request = new RunTaskRequest
            {
                Cluster = cluster,
                TaskDefinition = taskDef,
                LaunchType = LaunchType.FARGATE,
                Count = 1,
                NetworkConfiguration = new NetworkConfiguration
                {
                    AwsvpcConfiguration = new AwsVpcConfiguration
                    {
                        Subnets = new List<string> { subnetId ?? "subnet-08e1d4f1b5530f39e" },
                        SecurityGroups = new List<string> { securityGroupId ?? "sg-0fb53615b1eb4a175" },
                        AssignPublicIp = AssignPublicIp.ENABLED
                    }
                },
                Overrides = new TaskOverride
                {
                    ContainerOverrides = new List<ContainerOverride>
                    {
                        new ContainerOverride
                        {
                            Name = containerName,
                            // NOTE (ADO#6815): do NOT override FIRM_API_URL here. The firm-vpbot task
                            // definition already sets it to the internal URL (http://firm.fip.internal:8080),
                            // bypassing Cloudflare. Overriding it at runtime with Firm:ApiUrl (the public
                            // https://meetings.dev.fortressam.ai domain) sent the bot's callback through
                            // Cloudflare's managed challenge, which returned HTTP 403 and left the callback
                            // never reaching the API — meetings got stuck at Pending forever.
                            Environment = new List<Amazon.ECS.Model.KeyValuePair>
                            {
                                new() { Name = "MEETING_ID", Value = meetingId.ToString() },
                                new() { Name = "MEETING_URL", Value = meetingUrl },
                                new() { Name = "BOT_DISPLAY_NAME", Value = botDisplayName },
                                new() { Name = "BOT_JOIN_NAME", Value = botJoinName },
                                new() { Name = "BOT_NAMES_CSV", Value = botNamesCsv },
                                new() { Name = "BOT_CHAT_ANNOUNCE_NAME", Value = await GetUserFullNameAsync(meetingId) ?? botDisplayName },
                                new() { Name = "BOT_CALLBACK_SECRET", Value = botSecret },
                                new() { Name = "MEETING_PLATFORM", Value = platform },
                                new() { Name = "S3_BUCKET", Value = _config["Firm:S3Bucket"] ?? "firm-recordings-dev" },
                                new() { Name = "AWS_REGION", Value = "us-east-1" }
                            }
                        }
                    }
                }
            };

            var response = await _ecs.RunTaskAsync(request);
            var taskArn = response.Tasks.FirstOrDefault()?.TaskArn;
            _logger.LogInformation("FIRM: Bot ECS task launched: {Arn}", taskArn);
            if (taskArn != null)
                await _meetingService.UpdateBotTaskArnAsync(meetingId, taskArn);
            return taskArn;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to launch VP bot ECS task for meeting {Id}", meetingId);
            return null;
        }
    }

    /// <summary>
    /// Looks up the first name of the user who created the meeting, for join-name
    /// disambiguation when multiple users' bots are in the same meeting (WI #7009).
    /// Best-effort — returns null on any lookup failure so bot launch is never blocked.
    /// </summary>
    private async Task<string?> GetUserFirstNameAsync(long meetingId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var displayName = await db.Meetings
                .Where(m => m.Id == meetingId)
                .Select(m => m.CreatedByUser!.DisplayName)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(displayName))
                return null;

            return displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: Failed to resolve user first name for meeting {Id} — proceeding without it", meetingId);
            return null;
        }
    }

    /// <summary>
    /// WI #7258/#7259: Looks up the full display name of the user who created the meeting,
    /// for chat announcements ("I'm here to take notes for {FullName}").
    /// Best-effort — returns null on any lookup failure so bot launch is never blocked.
    /// </summary>
    private async Task<string?> GetUserFullNameAsync(long meetingId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var displayName = await db.Meetings
                .Where(m => m.Id == meetingId)
                .Select(m => m.CreatedByUser!.DisplayName)
                .FirstOrDefaultAsync();

            return string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: Failed to resolve user full name for meeting {Id} — proceeding without it", meetingId);
            return null;
        }
    }

    /// <summary>
    /// WI #7033: resolves the comma-separated list of user full names (primary + subscribers,
    /// ordered by created_at ASC) for the Phase 2 join-notification chat message
    /// ("...on behalf of Fred White, Rob Smith"). Best-effort — returns just the primary's name
    /// (or empty string) on any lookup failure so bot launch is never blocked.
    /// </summary>
    private async Task<string> GetBotNamesCsvAsync(long primaryMeetingId)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var names = await db.Meetings
                .Where(m => m.Id == primaryMeetingId || m.PrimaryMeetingId == primaryMeetingId)
                .OrderBy(m => m.CreatedAt)
                .Select(m => m.CreatedByUser!.DisplayName)
                .ToListAsync();

            return string.Join(",", names.Where(n => !string.IsNullOrWhiteSpace(n)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FIRM: Failed to resolve BOT_NAMES_CSV for meeting {Id} — proceeding without it", primaryMeetingId);
            return "";
        }
    }

    public async System.Threading.Tasks.Task StopBotAsync(string taskArn)
    {
        var cluster = _config["Firm:EcsCluster"];
        if (string.IsNullOrEmpty(cluster) || string.IsNullOrEmpty(taskArn))
        {
            _logger.LogWarning("FIRM: StopBotAsync called with empty cluster or taskArn");
            throw new InvalidOperationException("ECS cluster or taskArn not configured — cannot stop bot task");
        }

        try
        {
            var request = new StopTaskRequest
            {
                Cluster = cluster,
                Task = taskArn,
                Reason = "User requested stop recording"
            };
            await _ecs.StopTaskAsync(request);
            _logger.LogInformation("FIRM: ECS StopTask sent for task {TaskArn}", taskArn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FIRM: Failed to stop ECS task {TaskArn}", taskArn);
            throw; // Re-throw so controller can handle as bot_unreachable
        }
    }
}
