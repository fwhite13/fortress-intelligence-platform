using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FortressAI.V2.Web.Data;
using FortressAI.V2.Web.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace FortressAI.V2.Web.Services;

public interface IScheduledTaskNotificationService
{
    Task SendCompletionEmailAsync(
        ScheduledTask task,
        ScheduledTaskRun run,
        string userId,
        CancellationToken ct = default);
}

public class ScheduledTaskNotificationService : IScheduledTaskNotificationService
{
    private readonly IDbContextFactory<FaitV2DbContext> _dbFactory;
    private readonly IMicrosoftTokenService _tokenService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScheduledTaskNotificationService> _logger;

    public ScheduledTaskNotificationService(
        IDbContextFactory<FaitV2DbContext> dbFactory,
        IMicrosoftTokenService tokenService,
        IHttpClientFactory httpClientFactory,
        ILogger<ScheduledTaskNotificationService> logger)
    {
        _dbFactory = dbFactory;
        _tokenService = tokenService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task SendCompletionEmailAsync(
        ScheduledTask task,
        ScheduledTaskRun run,
        string userId,
        CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(CancellationToken.None);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, CancellationToken.None);
        if (user == null)
        {
            _logger.LogWarning("User not found for userId={UserId} — skipping scheduled task notification", userId);
            return;
        }

        bool isSuccess = run.Status == "success";

        if (isSuccess && !task.AlertOnCompletion)
            return;
        if (!isSuccess && !task.AlertOnFailure)
            return;

        var token = await _tokenService.GetValidAccessTokenAsync(user.EntraOid);
        if (token == null)
        {
            _logger.LogWarning("MS Graph not configured or token unavailable for userId={UserId} — skipping notification", userId);
            return;
        }

        var statusLabel = isSuccess ? "completed successfully" : "failed";
        var subject = $"[FAIT] Task '{task.Name}' {statusLabel}";

        var bodyBuilder = new StringBuilder();
        bodyBuilder.Append("<h3>[FAIT] Scheduled Task Notification</h3>");
        bodyBuilder.Append($"<p><strong>Task:</strong> {HtmlEncode(task.Name)}</p>");
        bodyBuilder.Append($"<p><strong>Run ID:</strong> {HtmlEncode(run.Id)}</p>");
        bodyBuilder.Append($"<p><strong>Status:</strong> {HtmlEncode(statusLabel)}</p>");
        bodyBuilder.Append($"<p><strong>Completed:</strong> {run.CompletedAt:u}</p>");

        if (run.OutputText != null)
        {
            var preview = run.OutputText.Length > 500 ? run.OutputText[..500] : run.OutputText;
            bodyBuilder.Append($"<p><strong>Output Preview:</strong></p><pre>{HtmlEncode(preview)}</pre>");
        }

        if (run.ErrorMessage != null && !isSuccess)
        {
            bodyBuilder.Append($"<p><strong>Error:</strong> {HtmlEncode(run.ErrorMessage)}</p>");
        }

        var payload = new
        {
            message = new
            {
                subject,
                body = new { contentType = "HTML", content = bodyBuilder.ToString() },
                toRecipients = new[]
                {
                    new { emailAddress = new { address = user.Email } }
                }
            }
        };

        try
        {
            var httpClient = _httpClientFactory.CreateClient("MicrosoftGraphClient");
            var request = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var response = await httpClient.SendAsync(request, CancellationToken.None);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Graph sendMail returned {StatusCode} for task={TaskId} run={RunId}",
                    response.StatusCode, task.Id, run.Id);
                return;
            }

            _logger.LogInformation("Scheduled task notification sent for task={TaskId} run={RunId} status={Status}",
                task.Id, run.Id, run.Status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception sending Graph notification for task={TaskId} run={RunId}", task.Id, run.Id);
        }
    }

    private static string HtmlEncode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);
}
