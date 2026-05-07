using FortressAI.V2.Web.Data.Models;

namespace FortressAI.V2.Web.Services;

public interface IScheduledTaskService
{
    Task<List<ScheduledTask>> GetUserTasksAsync(string userId, CancellationToken ct = default);
    Task<ScheduledTask> CreateTaskAsync(string userId, string name, string prompt,
        string scheduleType, string? cronExpression,
        bool alertOnCompletion = false, bool alertOnFailure = true,
        CancellationToken ct = default);
    Task<ScheduledTask> UpdateTaskAsync(string taskId, string userId, string name, string prompt,
        string? cronExpression, bool isActive,
        bool alertOnCompletion = false, bool alertOnFailure = true,
        CancellationToken ct = default);
    Task DeleteTaskAsync(string taskId, string userId, CancellationToken ct = default);
    Task TriggerNowAsync(string taskId, string userId, CancellationToken ct = default);
    Task<List<ScheduledTaskRun>> GetRunHistoryAsync(string taskId, string userId,
        int limit = 20, CancellationToken ct = default);
    Task<List<ScheduledTaskRun>> GetAllRunHistoryAsync(string userId,
        int limit = 50, CancellationToken ct = default);
}
