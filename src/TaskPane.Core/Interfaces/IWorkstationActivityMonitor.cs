namespace TaskPane.Core.Interfaces;

/// <summary>
/// Windows lock/suspend/idle signals. Implementations may be no-ops on non-Windows hosts.
/// </summary>
public interface IWorkstationActivityMonitor : IDisposable
{
    event EventHandler? SessionLocked;
    event EventHandler? SessionUnlocked;
    event EventHandler? SystemSuspended;
    event EventHandler? SystemResumed;

    /// <summary>Duration since last user input, or null when unavailable.</summary>
    TimeSpan? GetIdleDuration();

    void Start();
}
