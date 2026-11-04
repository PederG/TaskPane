namespace TaskPane.Core.Interfaces;

/// <summary>
/// Tracks unclean process exits so startup can prompt recovery without charging phantom work.
/// </summary>
public interface ICrashMarkerStore
{
    /// <summary>
    /// Marks a new session. Returns true when a previous session did not clear its marker (unclean exit).
    /// </summary>
    bool BeginSession();

    /// <summary>Clears the running marker after a clean shutdown.</summary>
    void EndSessionCleanly();

    bool HasUncleanMarker { get; }
}
