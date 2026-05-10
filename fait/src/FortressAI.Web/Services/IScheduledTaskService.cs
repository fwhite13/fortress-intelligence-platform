using FortressAI.Shared.Models;

namespace FortressAI.Web.Services;

public interface IScheduledTaskService
{
    Task<List<ScheduledTask>> GetTasksAsync(Guid userId);
    Task<ScheduledTask?> GetTaskAsync(Guid taskId, Guid userId);
    Task<ScheduledTask> CreateTaskAsync(Guid userId, CreateScheduledTaskDto dto);
    Task<ScheduledTask?> UpdateTaskAsync(Guid taskId, Guid userId, UpdateScheduledTaskDto dto);
    Task<bool> DeleteTaskAsync(Guid taskId, Guid userId);
    Task<bool> PauseAsync(Guid taskId, Guid userId);
    Task<bool> ResumeAsync(Guid taskId, Guid userId);
    Task<List<ScheduledTaskRun>> GetRunHistoryAsync(Guid taskId, Guid userId, int limit = 20);
}
