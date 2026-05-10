using System.Net.Http.Headers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using FortressAI.Web.Data;
using FortressAI.Web.Hubs;

namespace FortressAI.Web.Services;

public class TaskNotificationService : ITaskNotificationService
{
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly MicrosoftTokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TaskNotificationService> _logger;

    public TaskNotificationService(
        IHubContext<DashboardHub> hubContext,
        IDbContextFactory<AppDbContext> dbFactory,
        MicrosoftTokenService tokenService,
        IHttpClientFactory httpClientFactory,
        ILogger<TaskNotificationService> logger)
    {
        _hubContext = hubContext;
        _dbFactory = dbFactory;
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task NotifyTaskCompletedAsync(Guid userId, string taskName, string? resultSummary, CancellationToken ct = default)
    {
        // Channel 1: SignalR toast
        try
        {
            await _hubContext.Clients.Group($"user-{userId}").SendAsync(
                "ReceiveTaskNotification",
                new { taskName, status = "success", message = $"Task '{taskName}' completed successfully.", tasksUrl = "/tasks" },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR notification failed for task completion (userId={UserId}, task={TaskName}) — notification unaffected", userId, taskName);
        }

        // Channel 2: MS365 email
        try
        {
            var userEmail = await GetUserEmailAsync(userId, ct);
            if (userEmail == null)
            {
                _logger.LogDebug("No user email found for userId={UserId} — skipping email notification", userId);
                return;
            }

            var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
            if (accessToken == null)
            {
                _logger.LogDebug("No MS365 token for user {UserId} — skipping email notification", userId);
                return;
            }

            var body = string.IsNullOrEmpty(resultSummary)
                ? $"Task '{taskName}' completed successfully.\n\nView task history: https://fait.fortressam.ai/tasks"
                : $"Task '{taskName}' completed successfully.\n\nResult summary:\n{(resultSummary.Length > 500 ? resultSummary[..500] : resultSummary)}\n\nView task history: https://fait.fortressam.ai/tasks";

            await SendGraphEmailAsync(userId, userEmail, $"Task completed: {taskName}", body, accessToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email notification failed for task completion (userId={UserId}, task={TaskName}) — task status unaffected", userId, taskName);
        }
    }

    public async Task NotifyTaskPermanentlyFailedAsync(Guid userId, string taskName, string errorMessage, CancellationToken ct = default)
    {
        // Channel 1: SignalR toast
        try
        {
            await _hubContext.Clients.Group($"user-{userId}").SendAsync(
                "ReceiveTaskNotification",
                new { taskName, status = "failed", message = $"Task '{taskName}' has stopped retrying after repeated failures.", tasksUrl = "/tasks" },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR notification failed for task failure (userId={UserId}, task={TaskName}) — notification unaffected", userId, taskName);
        }

        // Channel 2: MS365 email
        try
        {
            var userEmail = await GetUserEmailAsync(userId, ct);
            if (userEmail == null)
            {
                _logger.LogDebug("No user email found for userId={UserId} — skipping email notification", userId);
                return;
            }

            var accessToken = await _tokenService.GetValidAccessTokenAsync(userId);
            if (accessToken == null)
            {
                _logger.LogDebug("No MS365 token for user {UserId} — skipping email notification", userId);
                return;
            }

            var body = $"Task '{taskName}' has stopped retrying after repeated failures and requires your attention.\n\nError: {errorMessage}\n\nThis task has stopped retrying. View at: https://fait.fortressam.ai/tasks";

            await SendGraphEmailAsync(userId, userEmail, $"Task failed: {taskName}", body, accessToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Email notification failed for task permanent failure (userId={UserId}, task={TaskName}) — task status unaffected", userId, taskName);
        }
    }

    private async Task<string?> GetUserEmailAsync(Guid userId, CancellationToken ct)
    {
        try
        {
            await using var ctx = await _dbFactory.CreateDbContextAsync(ct);
            var user = await ctx.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            return user?.Email;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load email for user {UserId}", userId);
            return null;
        }
    }

    private async Task SendGraphEmailAsync(Guid userId, string userEmail, string subject, string body, string accessToken)
    {
        var http = _httpClientFactory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var payload = new
        {
            message = new
            {
                subject,
                body = new { contentType = "Text", content = body },
                toRecipients = new[] { new { emailAddress = new { address = userEmail } } }
            },
            saveToSentItems = false
        };

        var response = await http.PostAsJsonAsync("https://graph.microsoft.com/v1.0/me/sendMail", payload);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Graph sendMail failed for user {UserId}: {Status} — {Error}", userId, response.StatusCode, error);
        }
    }
}
