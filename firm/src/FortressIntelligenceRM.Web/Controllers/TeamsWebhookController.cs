using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FortressIntelligenceRM.Web.Services;

namespace FortressIntelligenceRM.Web.Controllers;

[ApiController]
[Route("api/teams")]
public class TeamsWebhookController : ControllerBase
{
    private readonly TeamsGraphService _graphService;
    private readonly IConfiguration _config;
    private readonly ILogger<TeamsWebhookController> _logger;

    public TeamsWebhookController(
        TeamsGraphService graphService,
        IConfiguration config,
        ILogger<TeamsWebhookController> logger)
    {
        _graphService = graphService;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Graph webhook validation — must return plain text, NOT JSON
    /// </summary>
    [HttpGet("webhook")]
    [AllowAnonymous]
    public IActionResult ValidateWebhook([FromQuery] string? validationToken)
    {
        if (string.IsNullOrEmpty(validationToken))
            return BadRequest("validationToken required");

        _logger.LogInformation("[TeamsWebhook] Validation request received.");
        return Content(validationToken, "text/plain");
    }

    /// <summary>
    /// Graph webhook notification — validate clientState, fire-and-forget, return 202 within 3 seconds.
    /// Gated by Firm__EnableModeA feature flag (mothballed by default).
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public IActionResult HandleNotification([FromBody] GraphNotificationEnvelope? envelope)
    {
        // Mode A mothball guard — silently accept but do not process when disabled
        var modeAEnabled = _config.GetValue<bool>("Firm:EnableModeA", false);
        if (!modeAEnabled)
        {
            _logger.LogDebug("[TeamsWebhook] Mode A disabled (Firm__EnableModeA=false) — ignoring notification");
            return Accepted(new { accepted = true });
        }

        if (envelope?.Value == null || envelope.Value.Count == 0)
            return Accepted(new { accepted = true });

        var expectedState = _config["Firm:WebhookClientState"] ?? "";

        foreach (var notification in envelope.Value)
        {
            if (!string.IsNullOrEmpty(expectedState) && notification.ClientState != expectedState)
            {
                _logger.LogWarning("[TeamsWebhook] Invalid clientState received — rejecting notification");
                return Unauthorized();
            }

            var meetingRef = notification.Resource ?? "";
            if (!string.IsNullOrEmpty(meetingRef))
            {
                // Fire and forget — must return within 3 seconds
                _ = Task.Run(() => _graphService.FetchAndProcessTranscriptAsync(meetingRef, CancellationToken.None));
                _logger.LogInformation("[TeamsWebhook] Queued FetchAndProcessTranscriptAsync for resource: {Resource}", meetingRef);
            }
        }

        return Accepted(new { accepted = true });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public class GraphNotificationEnvelope
{
    public List<GraphNotification> Value { get; set; } = new();
}

public class GraphNotification
{
    public string Resource { get; set; } = "";
    public string? ClientState { get; set; }
    public string? SubscriptionId { get; set; }
    public string? ChangeType { get; set; }
}
