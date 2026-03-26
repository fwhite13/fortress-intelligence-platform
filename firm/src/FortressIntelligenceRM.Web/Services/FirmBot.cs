using System.Text.Json;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;

namespace FortressIntelligenceRM.Web.Services;

public class FirmBot : ActivityHandler
{
    private readonly IFirmBotService _botService;
    private readonly ILogger<FirmBot> _logger;

    public FirmBot(IFirmBotService botService, ILogger<FirmBot> logger)
    {
        _botService = botService;
        _logger = logger;
    }

    protected override async Task OnMembersAddedAsync(
        IList<ChannelAccount> membersAdded,
        ITurnContext<IConversationUpdateActivity> turnContext,
        CancellationToken cancellationToken)
    {
        foreach (var member in membersAdded)
        {
            if (member.Id == turnContext.Activity.Recipient.Id)
            {
                var reference = turnContext.Activity.GetConversationReference();
                var channelData = turnContext.Activity.ChannelData as Newtonsoft.Json.Linq.JObject;
                var teamId = channelData?["team"]?["id"]?.ToString();
                var teamName = channelData?["team"]?["name"]?.ToString();
                var channelId = channelData?["channel"]?["id"]?.ToString()
                    ?? turnContext.Activity.Conversation.Id;
                var channelName = channelData?["channel"]?["name"]?.ToString() ?? "General";

                await _botService.StoreInstallationAsync(
                    teamId, teamName, channelId, channelName,
                    JsonSerializer.Serialize(reference),
                    reference.ServiceUrl ?? "",
                    turnContext.Activity.Conversation.TenantId);

                _logger.LogInformation("[FirmBot] Installed: team={TeamId} channel={ChannelId}", teamId, channelId);
            }
        }
    }

    protected override async Task OnMembersRemovedAsync(
        IList<ChannelAccount> membersRemoved,
        ITurnContext<IConversationUpdateActivity> turnContext,
        CancellationToken cancellationToken)
    {
        foreach (var member in membersRemoved)
        {
            if (member.Id == turnContext.Activity.Recipient.Id)
            {
                var channelData = turnContext.Activity.ChannelData as Newtonsoft.Json.Linq.JObject;
                var teamId = channelData?["team"]?["id"]?.ToString();
                var channelId = channelData?["channel"]?["id"]?.ToString()
                    ?? turnContext.Activity.Conversation.Id;

                await _botService.RemoveInstallationAsync(teamId, channelId);
                _logger.LogInformation("[FirmBot] Removed: team={TeamId} channel={ChannelId}", teamId, channelId);
            }
        }
    }
}
