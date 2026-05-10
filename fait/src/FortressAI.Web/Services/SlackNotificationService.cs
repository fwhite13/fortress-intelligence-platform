using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FortressAI.Web.Services;

public class SlackNotificationService : ISlackNotificationService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string? _botToken;
    private readonly ILogger<SlackNotificationService> _logger;

    public SlackNotificationService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<SlackNotificationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _botToken = config["Slack__BotToken"];
        _logger = logger;
    }

    public async Task SendDmAsync(string userEmail, string message)
    {
        try
        {
            if (string.IsNullOrEmpty(_botToken))
            {
                _logger.LogWarning("Slack__BotToken not configured — skipping Slack DM to {Email}", userEmail);
                return;
            }

            var client = _httpClientFactory.CreateClient("slack");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _botToken);

            // Step 1: Look up Slack user by email
            var lookupResponse = await client.GetAsync(
                $"https://slack.com/api/users.lookupByEmail?email={Uri.EscapeDataString(userEmail)}");
            var lookupBody = await lookupResponse.Content.ReadAsStringAsync();
            var lookupJson = JsonDocument.Parse(lookupBody);

            if (!lookupJson.RootElement.TryGetProperty("ok", out var okEl) || !okEl.GetBoolean())
            {
                _logger.LogWarning("Slack users.lookupByEmail failed for {Email}: {Body}", userEmail, lookupBody);
                return;
            }

            var slackUserId = lookupJson.RootElement.GetProperty("user").GetProperty("id").GetString();
            if (string.IsNullOrEmpty(slackUserId))
            {
                _logger.LogWarning("Slack returned empty user id for {Email}", userEmail);
                return;
            }

            // Step 2: Post DM to user
            var payload = JsonSerializer.Serialize(new { channel = slackUserId, text = message });
            var postResponse = await client.PostAsync(
                "https://slack.com/api/chat.postMessage",
                new StringContent(payload, Encoding.UTF8, "application/json"));
            var postBody = await postResponse.Content.ReadAsStringAsync();
            var postJson = JsonDocument.Parse(postBody);

            if (!postJson.RootElement.TryGetProperty("ok", out var postOk) || !postOk.GetBoolean())
            {
                _logger.LogWarning("Slack chat.postMessage failed for user {UserId}: {Body}", slackUserId, postBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Slack DM to {Email} failed — best-effort, ignoring", userEmail);
        }
    }
}
