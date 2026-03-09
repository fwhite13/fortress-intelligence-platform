using Microsoft.AspNetCore.Mvc;
using Amazon.SQS;
using Amazon.SQS.Model;
using FortressAI.Web.Services;
using System.Text.Json;

namespace FortressAI.Web.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly GraphWebhookService _webhookService;
    private readonly IConfiguration _config;
    private readonly ILogger<WebhooksController> _logger;
    private readonly IAmazonSQS? _sqsClient;

    public WebhooksController(
        GraphWebhookService webhookService,
        IConfiguration config,
        ILogger<WebhooksController> logger,
        IAmazonSQS? sqsClient = null)
    {
        _webhookService = webhookService;
        _config = config;
        _logger = logger;
        _sqsClient = sqsClient;
    }

    /// <summary>
    /// Receives Graph webhook notifications for email events.
    /// Handles validation handshake and forwards notifications to SQS.
    /// </summary>
    [HttpPost("graph")]
    public async Task<IActionResult> ReceiveGraphWebhook(
        [FromQuery] string? validationToken)
    {
        // Validation handshake (Graph sends this during subscription creation)
        if (!string.IsNullOrEmpty(validationToken))
        {
            _logger.LogInformation("Graph webhook validation handshake received");
            return Content(validationToken, "text/plain");
        }

        // Read the notification body
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        _logger.LogInformation("Received Graph webhook notification");

        try
        {
            var notification = JsonSerializer.Deserialize<GraphNotificationPayload>(body, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (notification?.Value == null)
            {
                _logger.LogWarning("Empty or invalid notification payload");
                return Accepted();
            }

            foreach (var item in notification.Value)
            {
                // Validate client state
                var userId = await _webhookService.ValidateClientStateAsync(item.ClientState ?? "");
                if (userId == null)
                {
                    _logger.LogWarning("Invalid client state in notification: {ClientState}", item.ClientState);
                    continue;
                }

                // Send to SQS if available, otherwise process inline
                var queueUrl = _config["AWS:SQS:EmailEventsQueue"];
                if (_sqsClient != null && !string.IsNullOrEmpty(queueUrl))
                {
                    var message = JsonSerializer.Serialize(new
                    {
                        UserId = userId,
                        ResourceId = item.ResourceData?.Id,
                        item.Resource,
                        item.ChangeType,
                        item.ClientState
                    });

                    await _sqsClient.SendMessageAsync(new SendMessageRequest
                    {
                        QueueUrl = queueUrl,
                        MessageBody = message
                    });
                    _logger.LogInformation("Queued email notification for user {UserId}, resource {ResourceId}",
                        userId, item.ResourceData?.Id);
                }
                else
                {
                    _logger.LogInformation("SQS not configured. Notification logged for user {UserId}, resource {ResourceId}",
                        userId, item.ResourceData?.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Graph webhook notification");
        }

        // Must return 2xx quickly to Graph
        return Accepted();
    }
}

// DTOs for Graph notification payload
public class GraphNotificationPayload
{
    public List<GraphNotificationItem>? Value { get; set; }
}

public class GraphNotificationItem
{
    public string? ChangeType { get; set; }
    public string? ClientState { get; set; }
    public string? Resource { get; set; }
    public string? SubscriptionId { get; set; }
    public GraphResourceData? ResourceData { get; set; }
}

public class GraphResourceData
{
    public string? Id { get; set; }
}
