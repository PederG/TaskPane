namespace TaskPane.Core.Overlay;

/// <summary>
/// Policy for the always-on-top overlay chrome modes.
/// Mini form is not tray minimize — it is a smaller on-top strip that expands on focus.
/// </summary>
public static class OverlaySessionChrome
{
    /// <summary>Minimum window height (DIP) for the full compact overlay.</summary>
    public const double FullMinHeightDip = 200;

    /// <summary>Minimum window height (DIP) for the mini strip.</summary>
    public const double MiniMinHeightDip = 72;

    /// <summary>
    /// Show the mini strip whenever the overlay is not focused.
    /// Active sessions render title + elapsed + pause; idle renders brand + expand.
    /// </summary>
    public static bool ShouldShowMini(bool isOverlayFocused)
        => !isOverlayFocused;

    /// <summary>MinHeight for the current chrome mode.</summary>
    public static double MinHeightDip(bool isMini)
        => isMini ? MiniMinHeightDip : FullMinHeightDip;
}
