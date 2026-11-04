namespace TaskPane.Core.Enums;

/// <summary>How to resolve a dangling open session after restart/crash.</summary>
public enum SessionRecoveryChoice
{
    /// <summary>Close the session at its start (zero duration; no overnight charge).</summary>
    Discard = 0,

    /// <summary>Close at a conservative timestamp supplied by the caller (e.g. last write).</summary>
    CloseAtKnownTime = 1,

    /// <summary>Close at "now" — only when the user explicitly accepts charging the gap.</summary>
    CloseNow = 2
}
