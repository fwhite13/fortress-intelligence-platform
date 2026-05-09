namespace FortressAI.V2.Web.Services;

public interface ITaskListNotifier
{
    event Action? OnTaskListChanged;
    void NotifyChanged();
}

public class TaskListNotifier : ITaskListNotifier
{
    public event Action? OnTaskListChanged;
    public void NotifyChanged() => OnTaskListChanged?.Invoke();
}
