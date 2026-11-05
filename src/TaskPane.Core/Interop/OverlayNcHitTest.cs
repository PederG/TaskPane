namespace TaskPane.Core.Interop;

/// <summary>
/// Pure WM_NCHITTEST decision for the compact overlay: edge/corner resize plus a top
/// caption band for dragging empty RootSurface padding without stealing header buttons.
/// </summary>
public static class OverlayNcHitTest
{
    public const int HtClient = 1;
    public const int HtCaption = 2;
    public const int HtLeft = 10;
    public const int HtRight = 11;
    public const int HtTop = 12;
    public const int HtTopLeft = 13;
    public const int HtTopRight = 14;
    public const int HtBottom = 15;
    public const int HtBottomLeft = 16;
    public const int HtBottomRight = 17;

    /// <summary>Default invisible resize edge thickness (DIP).</summary>
    public const double DefaultResizeBorderDip = 6;

    /// <summary>Default top drag band (DIP) — matches TpOverlayPad / RootSurface upper padding.</summary>
    public const double DefaultTopCaptionBandDip = 14;

    /// <summary>
    /// Resolves a Win32 hit-test code for a window-local DIP point.
    /// </summary>
    /// <param name="localX">X in window DIP.</param>
    /// <param name="localY">Y in window DIP.</param>
    /// <param name="windowWidth">Window ActualWidth (DIP).</param>
    /// <param name="windowHeight">Window ActualHeight (DIP).</param>
    /// <param name="resizeBorderDip">Edge thickness for HTLEFT/RIGHT/TOP/BOTTOM and corners.</param>
    /// <param name="topCaptionBandDip">
    /// Height of the top-center drag strip (≈ RootSurface upper padding). Top-left / top-right
    /// resize corners still win inside this band.
    /// </param>
    /// <param name="positionLocked">When true, never return <see cref="HtCaption"/>.</param>
    /// <param name="overInteractiveChrome">
    /// True when the point is over Search / Expand / More (or other header action buttons).
    /// Forces <see cref="HtClient"/> so those controls keep mouse input.
    /// </param>
    /// <param name="visibleLeft">Inclusive left of the visible RootSurface (window DIP).</param>
    /// <param name="visibleTop">Inclusive top of the visible RootSurface (window DIP).</param>
    /// <param name="visibleRight">Exclusive right of the visible RootSurface (window DIP).</param>
    /// <param name="visibleBottom">Exclusive bottom of the visible RootSurface (window DIP).</param>
    public static int Decide(
        double localX,
        double localY,
        double windowWidth,
        double windowHeight,
        double resizeBorderDip,
        double topCaptionBandDip,
        bool positionLocked,
        bool overInteractiveChrome,
        double visibleLeft,
        double visibleTop,
        double visibleRight,
        double visibleBottom)
    {
        if (windowWidth <= 0 || windowHeight <= 0)
        {
            return HtClient;
        }

        var border = resizeBorderDip < 0 ? 0 : resizeBorderDip;
        var captionBand = topCaptionBandDip < 0 ? 0 : topCaptionBandDip;

        // Limit caption / edge decisions to the visible RootSurface silhouette.
        var insideVisible = localX >= visibleLeft
            && localY >= visibleTop
            && localX < visibleRight
            && localY < visibleBottom;

        if (!insideVisible)
        {
            return HtClient;
        }

        // Resize borders relative to the visible surface (not the raw HWND), so inset chrome
        // still gets correct corner grip after SetWindowRgn.
        var onLeft = localX <= visibleLeft + border;
        var onRight = localX >= visibleRight - border;
        var onTop = localY <= visibleTop + border;
        var onBottom = localY >= visibleBottom - border;

        // Corners always win (including over the caption band).
        if (onLeft && onTop)
        {
            return HtTopLeft;
        }

        if (onRight && onTop)
        {
            return HtTopRight;
        }

        if (onLeft && onBottom)
        {
            return HtBottomLeft;
        }

        if (onRight && onBottom)
        {
            return HtBottomRight;
        }

        if (onLeft)
        {
            return HtLeft;
        }

        if (onRight)
        {
            return HtRight;
        }

        if (onBottom)
        {
            return HtBottom;
        }

        // Top-center band ≈ upper RootSurface padding → native caption drag.
        // Prefer HTCAPTION over HTTOP so empty padding is movable; corners already handled.
        var inTopCaptionBand = localY < visibleTop + captionBand;
        if (inTopCaptionBand)
        {
            if (positionLocked || overInteractiveChrome)
            {
                return HtClient;
            }

            return HtCaption;
        }

        // Remaining top resize strip only if caption band is thinner than the resize border.
        if (onTop)
        {
            return HtTop;
        }

        return HtClient;
    }
}
