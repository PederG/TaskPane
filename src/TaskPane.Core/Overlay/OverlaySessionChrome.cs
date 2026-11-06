namespace TaskPane.Core.Overlay;

/// <summary>
/// Policy for the always-on-top overlay chrome while a time session is running.
/// Mini form is not tray minimize — it is a smaller on-top strip that expands on focus.
/// </summary>
public static class OverlaySessionChrome
{
    /// <summary>Minimum window height (DIP) for the full compact overlay.</summary>
    public const double FullMinHeightDip = 200;

    /// <summary>Minimum window height (DIP) for the running-session mini strip.</summary>
    public const double MiniMinHeightDip = 72;

    /// <summary>
    /// Show the mini strip when a timer is active and the overlay is not focused.
    /// When no timer is active, always use full chrome (expand even if unfocused) —
    /// less surprising than leaving a blank mini shell after stop.
    /// </summary>
    public static bool ShouldShowMini(bool hasActiveSession, bool isOverlayFocused)
        => hasActiveSession && !isOverlayFocused;

    /// <summary>MinHeight for the current chrome mode.</summary>
    public static double MinHeightDip(bool isMini)
        => isMini ? MiniMinHeightDip : FullMinHeightDip;
}
