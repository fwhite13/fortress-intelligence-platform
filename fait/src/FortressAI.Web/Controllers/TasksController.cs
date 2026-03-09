using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FortressAI.Web.Services;

namespace FortressAI.Web.Controllers;

[Authorize]
[ApiController]
[Route("api")]
public class TasksController : ControllerBase
{
    private readonly GraphTaskService _graphTaskService;
    private readonly GraphCalendarService _graphCalendarService;
    private readonly ILogger<TasksController> _logger;

    public TasksController(
        GraphTaskService graphTaskService,
        GraphCalendarService graphCalendarService,
        ILogger<TasksController> logger)
    {
        _graphTaskService = graphTaskService;
        _graphCalendarService = graphCalendarService;
        _logger = logger;
    }

    /// <summary>
    /// Test endpoint: fetch Planner tasks for a user.
    /// </summary>
    [HttpGet("tasks/test")]
    public async Task<IActionResult> TestTaskFetch([FromQuery] string? userId)
    {
        var userGuid = ResolveUserId(userId);
        if (userGuid == null)
            return BadRequest(new { error = "Invalid or missing userId" });

        _logger.LogInformation("Test task fetch for user {UserId}", userGuid);
        var tasks = await _graphTaskService.GetUserTasksAsync(userGuid.Value);

        return Ok(new
        {
            userId = userGuid,
            count = tasks.Count,
            fetchedAt = DateTime.UtcNow,
            tasks = tasks.Select(t => new
            {
                t.TaskId,
                t.Title,
                t.DueDate,
                t.PercentComplete,
                t.Priority,
                t.PlanTitle,
                t.BucketName,
                isOverdue = t.DueDate.HasValue && t.DueDate.Value < DateTime.UtcNow,
                isDueToday = t.DueDate.HasValue && t.DueDate.Value.Date == DateTime.UtcNow.Date
            })
        });
    }

    /// <summary>
    /// Test endpoint: fetch calendar events for a user.
    /// </summary>
    [HttpGet("calendar/test")]
    public async Task<IActionResult> TestCalendarFetch([FromQuery] string? userId, [FromQuery] int? days)
    {
        var userGuid = ResolveUserId(userId);
        if (userGuid == null)
            return BadRequest(new { error = "Invalid or missing userId" });

        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(days ?? 7);

        _logger.LogInformation("Test calendar fetch for user {UserId} ({Start} to {End})", userGuid, startDate, endDate);
        var events = await _graphCalendarService.GetUserCalendarEventsAsync(userGuid.Value, startDate, endDate);

        return Ok(new
        {
            userId = userGuid,
            count = events.Count,
            startDate,
            endDate,
            fetchedAt = DateTime.UtcNow,
            events = events.Select(e => new
            {
                e.EventId,
                e.Subject,
                e.StartTime,
                e.EndTime,
                e.Location,
                e.OnlineMeetingUrl,
                e.AttendeesJson,
                e.Category,
                isTeamsMeeting = !string.IsNullOrEmpty(e.OnlineMeetingUrl)
            })
        });
    }

    private Guid? ResolveUserId(string? userId)
    {
        // Get authenticated user's ID from claims
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(claim) || !Guid.TryParse(claim, out var authenticatedUserId))
            return null;

        // If explicit userId provided, it must match the authenticated user (prevent reading other users' data)
        if (!string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var parsed))
        {
            if (parsed != authenticatedUserId)
                return null; // Deny cross-user access
            return parsed;
        }

        // Default to authenticated user's own data
        return authenticatedUserId;
    }
}
