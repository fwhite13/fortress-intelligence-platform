using FortressAI.Web.Data.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;

namespace FortressAI.Web.Services;

public class FeedbackDispatcher
{
    private readonly ILogger<FeedbackDispatcher> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public FeedbackDispatcher(
        ILogger<FeedbackDispatcher> logger,
        IHttpClientFactory httpClientFactory,
        IConfiguration config)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public async Task DispatchToJarvisAsync(FeedbackSubmission submission)
    {
        var webhookUrl = _config["Feedback:JarvisWebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            _logger.LogWarning("[feedback] Feedback:JarvisWebhookUrl not configured — skipping dispatch");
            return;
        }

        // Fix I2: use config for base URL, not hardcoded domain
        var baseUrl = _config["FIP:FaitBaseUrl"]?.TrimEnd('/') ?? "https://fait.fortressam.ai";

        var screenshotLine = submission.ScreenshotS3Key != null
            ? $"**Screenshot:** s3://{submission.ScreenshotS3Key}"
            : "";

        // Fix I3: InternalToken NOT included in message body — Jarvis has it configured separately
        var payload = new
        {
            message = $$"""
            ## FEEDBACK: {{submission.Type.ToUpper()}} from FAIT

            **Submission ID:** {{submission.Id}}
            **User ID:** {{submission.UserId}}
            **Page:** {{submission.PageUrl ?? "unknown"}}
            **Type:** {{submission.Type}}

            **Description:**
            {{submission.Description}}

            {{screenshotLine}}

            **Triage instructions:**
            - Auto-dispatch if this is a clear UI bug, broken element, wrong data, or regression
            - Escalate to Fred if this involves auth/permissions, data integrity, scope-expanding features, or active WI duplicates
            - After triage, call back: POST {{baseUrl}}/api/feedback/{{submission.Id}}/status
              with headers: Authorization: Bearer <configured-internal-token>
              with body: { "status": "dispatched"|"escalated", "adoWiId": 1234 (if dispatched), "message": "..." }
            """,
        };

        try
        {
            var http = _httpClientFactory.CreateClient("feedback");
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config["OpenClaw:ApiToken"] ?? "");
            await http.PostAsJsonAsync(webhookUrl, payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[feedback] Failed to dispatch to Jarvis");
        }
    }
}
