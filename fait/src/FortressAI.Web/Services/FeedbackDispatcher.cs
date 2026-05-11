using FortressAI.Web.Data.Models;
using Microsoft.Extensions.Logging;

namespace FortressAI.Web.Services;

public class FeedbackDispatcher
{
    private readonly ILogger<FeedbackDispatcher> _logger;

    public FeedbackDispatcher(ILogger<FeedbackDispatcher> logger)
    {
        _logger = logger;
    }

    public Task DispatchToJarvisAsync(FeedbackSubmission submission)
    {
        _logger.LogInformation("[feedback] Webhook dispatch removed — Jarvis polls directly");
        return Task.CompletedTask;
    }
}
