namespace TaskPane.Core.Enums;

/// <summary>
/// Open vs completed. Sleeping, waiting, blocked and deleted are tracked separately.
/// </summary>
public enum TaskLifecycleStatus
{
    Open = 0,
    Completed = 1
}
