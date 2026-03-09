using Microsoft.AspNetCore.Mvc;
using FortressAI.Web.Services;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("api/email")]
public class EmailController : ControllerBase
{
    private readonly EmailAlertService _alertService;
    private readonly MicrosoftTokenService _tokenService;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<EmailController> _logger;

    public EmailController(
        EmailAlertService alertService,
        MicrosoftTokenService tokenService,
        IHttpClientFactory httpFactory,
        ILogger<EmailController> logger)
    {
        _alertService = alertService;
        _tokenService = tokenService;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets active (undismissed) email alerts for a user.
    /// </summary>
    [HttpGet("alerts/{userId}")]
    public async Task<IActionResult> GetAlerts(string userId)
    {
        if (!Guid.TryParse(userId, out var userGuid))
            return BadRequest("Invalid user ID");

        var alerts = await _alertService.GetActiveAlertsAsync(userGuid);
        return Ok(alerts);
    }

    /// <summary>
    /// Dismisses an email alert.
    /// </summary>
    [HttpPost("alerts/{alertId}/dismiss")]
    public async Task<IActionResult> DismissAlert(int alertId, [FromQuery] string userId)
    {
        if (!Guid.TryParse(userId, out var userGuid))
            return BadRequest("Invalid user ID");

        await _alertService.DismissAlertAsync(alertId, userGuid);
        return Ok(new { message = "Alert dismissed" });
    }

    /// <summary>
    /// Sends a reply to an email via Graph API.
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendReply([FromBody] SendReplyRequest request)
    {
        if (!Guid.TryParse(request.UserId, out var userGuid))
            return BadRequest("Invalid user ID");

        var accessToken = await _tokenService.GetValidAccessTokenAsync(userGuid);
        if (accessToken == null)
            return Unauthorized(new { error = "No valid Microsoft token. User must re-authenticate." });

        try
        {
            var http = _httpFactory.CreateClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var replyPayload = new
            {
                message = new
                {
                    body = new
                    {
                        contentType = "Text",
                        content = request.ReplyBody
                    }
                },
                comment = ""
            };

            var response = await http.PostAsJsonAsync(
                $"https://graph.microsoft.com/v1.0/me/messages/{request.MessageId}/reply",
                replyPayload);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to send reply: {StatusCode} {Error}", response.StatusCode, error);
                return StatusCode((int)response.StatusCode, new { error = "Failed to send reply" });
            }

            if (request.AlertId.HasValue)
            {
                await _alertService.DismissAlertAsync(request.AlertId.Value, userGuid);
            }

            _logger.LogInformation("Reply sent to message {MessageId} for user {UserId}", request.MessageId, userGuid);
            return Ok(new { message = "Reply sent successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email reply");
            return StatusCode(500, new { error = "Internal error sending reply" });
        }
    }

    /// <summary>
    /// Processes a simulated email notification (for dev/testing without real Graph webhooks).
    /// Triggers the full classification → summarization → draft pipeline.
    /// </summary>
    [HttpPost("process-test")]
    public async Task<IActionResult> ProcessTestEmail(
        [FromBody] TestEmailRequest request,
        [FromServices] EmailClassifierService classifier,
        [FromServices] AssistantConfigService configSvc,
        [FromServices] KnowledgeBaseService kbService)
    {
        if (!Guid.TryParse(request.UserId, out var userGuid))
            return BadRequest("Invalid user ID");

        try
        {
            var config = await configSvc.GetOrCreateConfigAsync(userGuid);

            // Step 1: Classify
            var importance = await classifier.ClassifyEmailAsync(
                request.SenderEmail, request.Subject, request.BodyPreview ?? request.Body,
                request.UserName ?? "User");

            if (importance == "HIGH")
            {
                // Step 2: Get KB context
                var kbChunks = new List<string>();
                try
                {
                    var query = $"Information about {request.SenderEmail} or {request.Subject}";
                    var chunks = await kbService.RetrieveAsync(query, useFortressKb: false, usePersonalKb: true);
                    kbChunks = chunks.Select(c => c.Content).ToList();
                }
                catch { /* KB not available */ }

                // Step 3: Summarize
                var summary = await classifier.SummarizeEmailAsync(
                    request.SenderEmail, request.Subject, request.Body,
                    request.UserName ?? "User", config.AssistantName, config.PersonalityPreset, kbChunks);

                // Step 4: Draft response
                var draft = await classifier.DraftResponseAsync(
                    request.SenderEmail, request.Subject, request.Body,
                    summary, request.UserName ?? "User", config.AssistantName, config.PersonalityPreset, kbChunks);

                // Step 5: Create alert (with SignalR push)
                var alert = await _alertService.CreateAlertAsync(
                    userGuid, request.MessageId ?? Guid.NewGuid().ToString(),
                    request.SenderEmail, request.Subject, importance, summary, draft);

                return Ok(new { importance, summary, draft, alertId = alert.Id });
            }
            else
            {
                await _alertService.LogEmailAsync(userGuid,
                    request.MessageId ?? Guid.NewGuid().ToString(),
                    request.SenderEmail, request.Subject, importance, DateTime.UtcNow);

                return Ok(new { importance, message = "Logged (not high priority)" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing test email");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}

public class SendReplyRequest
{
    public string UserId { get; set; } = "";
    public string MessageId { get; set; } = "";
    public string ReplyBody { get; set; } = "";
    public int? AlertId { get; set; }
}

public class TestEmailRequest
{
    public string UserId { get; set; } = "";
    public string SenderEmail { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string? BodyPreview { get; set; }
    public string? MessageId { get; set; }
    public string? UserName { get; set; }
}
