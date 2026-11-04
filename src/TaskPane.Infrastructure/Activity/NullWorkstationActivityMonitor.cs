using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Activity;

/// <summary>No-op monitor for non-Windows hosts and automated tests.</summary>
public sealed class NullWorkstationActivityMonitor : IWorkstationActivityMonitor
{
    public event EventHandler? SessionLocked
    {
        add { }
        remove { }
    }

    public event EventHandler? SessionUnlocked
    {
        add { }
        remove { }
    }

    public event EventHandler? SystemSuspended
    {
        add { }
        remove { }
    }

    public event EventHandler? SystemResumed
    {
        add { }
        remove { }
    }

    public TimeSpan? GetIdleDuration() => null;

    public void Start()
    {
        // Intentionally empty — no OS hooks on this host.
    }

    public void Dispose()
    {
    }
}
