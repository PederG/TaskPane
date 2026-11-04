namespace TaskPane.Core.Interfaces;

/// <summary>
/// Per-user autostart registration. Implementations must never require admin elevation.
/// </summary>
public interface IAutostartService
{
    bool IsSupported { get; }

    bool IsEnabled();

    /// <summary>Enable or disable autostart for the current user profile.</summary>
    void SetEnabled(bool enabled);
}
