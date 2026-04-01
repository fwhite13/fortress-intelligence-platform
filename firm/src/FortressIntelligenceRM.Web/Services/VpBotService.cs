using Amazon.ECS;
using Amazon.ECS.Model;
using FortressIntelligenceRM.Web.Services;
using Microsoft.Extensions.Options;

namespace FortressIntelligenceRM.Web.Services;

public class VpBotService
{
    private readonly IAmazonECS _ecs;
    private readonly IConfiguration _config;
    private readonly ILogger<VpBotService> _logger;
    private readonly MeetingService _meetingService;

    public VpBotService(IAmazonECS ecs, IConfiguration config, ILogger<VpBotService> logger, MeetingService meetingService)
    {
        _ecs = ecs;
        _config = config;
        _logger = logger;
        _meetingService = meetingService;
    }

    public async Task<string?> TriggerBotAsync(long meetingId, string meetingUrl, string platform = "teams")
    {
        var taskDef = _config["Firm:VpBotTaskDefinition"];
        var cluster = _config["Firm:EcsCluster"];
        var subnetId = _config["Firm:VpBotSubnetId"];
        var securityGroupId = _config["Firm:VpBotSecurityGroupId"];
        var firmApiUrl = _config["Firm:ApiUrl"] ?? "";
        var botSecret = _config["Firm:BotCallbackSecret"] ?? "";

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
                            Name = "firm-vpbot",
                            Environment = new List<Amazon.ECS.Model.KeyValuePair>
                            {
                                new() { Name = "FIRM_API_URL", Value = firmApiUrl },
                                new() { Name = "MEETING_ID", Value = meetingId.ToString() },
                                new() { Name = "MEETING_URL", Value = meetingUrl },
                                new() { Name = "BOT_DISPLAY_NAME", Value = "Fortress Notetaker" },
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

    public async System.Threading.Tasks.Task StopBotAsync(string taskArn)
    {
        var cluster = _config["Firm:EcsCluster"];
        if (string.IsNullOrEmpty(cluster) || string.IsNullOrEmpty(taskArn))
        {
            _logger.LogWarning("FIRM: StopBotAsync called with empty cluster or taskArn");
            return;
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
