namespace TaskPane.Application.Services;

/// <summary>
/// Broadcasts task-list mutations so the overlay and manager stay in sync when one
/// surface changes data the other is still displaying (e.g. History undo in manager).
/// </summary>
public sealed class TaskWorkspaceNotifier
{
    public event Action? TasksChanged;

    public void NotifyTasksChanged() => TasksChanged?.Invoke();
}
