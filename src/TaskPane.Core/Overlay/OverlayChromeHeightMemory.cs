namespace TaskPane.Core.Overlay;

/// <summary>
/// Remembers full-chrome overlay height across mini-strip collapse so the HWND can snap
/// down to the strip and restore the prior expanded size on expand.
/// </summary>
/// <remarks>
/// After a user resize, WPF turns <c>SizeToContent</c> off and keeps an explicit Height.
/// Collapsing content alone therefore leaves a tall empty chrome; this helper owns the
/// remembered expanded height used by the window to force the snap / restore.
/// </remarks>
public sealed class OverlayChromeHeightMemory
{
    /// <summary>Last known full-chrome height (DIP), or 0 when none remembered yet.</summary>
    public double RememberedExpandedHeight { get; private set; }

    /// <summary>
    /// Record the current window height before collapsing and return the mini-strip target height.
    /// </summary>
    public double OnCollapse(double currentHeight)
    {
        if (IsUsableExpandedHeight(currentHeight))
        {
            RememberedExpandedHeight = currentHeight;
        }
        else if (!IsUsableExpandedHeight(RememberedExpandedHeight))
        {
            RememberedExpandedHeight = OverlaySessionChrome.FullMinHeightDip;
        }

        return OverlaySessionChrome.MiniMinHeightDip;
    }

    /// <summary>
    /// Height to apply when expanding back to full chrome.
    /// Falls back to <paramref name="placementHeightFallback"/> when nothing taller was remembered.
    /// </summary>
    public double OnExpand(double placementHeightFallback)
    {
        if (IsUsableExpandedHeight(RememberedExpandedHeight))
        {
            return RememberedExpandedHeight;
        }

        var fallback = Math.Max(placementHeightFallback, OverlaySessionChrome.FullMinHeightDip);
        RememberedExpandedHeight = fallback;
        return fallback;
    }

    /// <summary>
    /// Height to persist while chrome may be mini — never write the strip height as the restored full height.
    /// </summary>
    public double HeightForPersistence(bool isMini, double currentHeight, double placementHeight)
    {
        if (!isMini)
        {
            var live = currentHeight > 0 ? currentHeight : placementHeight;
            if (IsUsableExpandedHeight(live))
            {
                RememberedExpandedHeight = live;
            }

            return Math.Max(live, OverlaySessionChrome.FullMinHeightDip);
        }

        if (IsUsableExpandedHeight(RememberedExpandedHeight))
        {
            return RememberedExpandedHeight;
        }

        return Math.Max(placementHeight, OverlaySessionChrome.FullMinHeightDip);
    }

    private static bool IsUsableExpandedHeight(double height)
        => !double.IsNaN(height)
           && !double.IsInfinity(height)
           && height >= OverlaySessionChrome.FullMinHeightDip;
}
