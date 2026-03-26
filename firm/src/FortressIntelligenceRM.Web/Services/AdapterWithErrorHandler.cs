using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Bot.Builder.TraceExtensions;
using Microsoft.Bot.Schema;

namespace FortressIntelligenceRM.Web.Services;

public class AdapterWithErrorHandler : CloudAdapter
{
    public AdapterWithErrorHandler(IConfiguration configuration, ILogger<IBotFrameworkHttpAdapter> logger)
        : base(configuration, logger: logger)
    {
        OnTurnError = async (turnContext, exception) =>
        {
            logger.LogError(exception, "[BotAdapter] Unhandled error in bot turn");
            await turnContext.SendActivityAsync("An error occurred in the bot.");
            await turnContext.TraceActivityAsync("OnTurnError Trace", exception.Message,
                "https://www.botframework.com/schemas/error", "TurnError");
        };
    }
}
