using TaskPane.Core.Interfaces;

namespace TaskPane.Infrastructure.Autostart;

/// <summary>No-op autostart used in tests and non-Windows hosts.</summary>
public sealed class NullAutostartService : IAutostartService
{
    private bool _enabled;

    public bool IsSupported => false;

    public bool IsEnabled() => _enabled;

    public void SetEnabled(bool enabled) => _enabled = enabled;
}
