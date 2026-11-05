namespace TaskPane.Core.Interop;

/// <summary>
/// Computes HWND-relative <c>CreateRoundRectRgn</c> parameters from physical window rect
/// and RootSurface screen bounds (DIPs from WPF <c>PointToScreen</c>).
/// Does not assume RootSurface originates at HWND (0,0).
/// </summary>
public static class RoundWindowRegionGeometry
{
    /// <summary>
    /// Builds inclusive-left/top exclusive-right/bottom region coords in HWND-relative
    /// physical pixels, plus ellipse diameters (= 2 × physical corner radius).
    /// </summary>
    /// <param name="windowLeft">GetWindowRect.Left (physical screen px).</param>
    /// <param name="windowTop">GetWindowRect.Top (physical screen px).</param>
    /// <param name="rootScreenLeftDip">RootSurface top-left from PointToScreen (DIP).</param>
    /// <param name="rootScreenTopDip">RootSurface top-left Y from PointToScreen (DIP).</param>
    /// <param name="rootScreenRightDip">RootSurface bottom-right X from PointToScreen (DIP).</param>
    /// <param name="rootScreenBottomDip">RootSurface bottom-right Y from PointToScreen (DIP).</param>
    /// <param name="dpiScaleX">Pixels-per-DIP X (e.g. VisualTreeHelper.GetDpi().DpiScaleX).</param>
    /// <param name="dpiScaleY">Pixels-per-DIP Y.</param>
    /// <param name="radiusDip">Corner radius in DIP (TpOverlayWindowRadius, typically 18).</param>
    public static RoundWindowRegionParams Compute(
        int windowLeft,
        int windowTop,
        double rootScreenLeftDip,
        double rootScreenTopDip,
        double rootScreenRightDip,
        double rootScreenBottomDip,
        double dpiScaleX,
        double dpiScaleY,
        double radiusDip)
    {
        var scaleX = dpiScaleX > 0 ? dpiScaleX : 1.0;
        var scaleY = dpiScaleY > 0 ? dpiScaleY : 1.0;

        var physLeft = (int)Math.Round(rootScreenLeftDip * scaleX);
        var physTop = (int)Math.Round(rootScreenTopDip * scaleY);
        var physRight = (int)Math.Round(rootScreenRightDip * scaleX);
        var physBottom = (int)Math.Round(rootScreenBottomDip * scaleY);

        var radiusPx = (int)Math.Round(radiusDip * Math.Min(scaleX, scaleY));
        if (radiusPx < 0)
        {
            radiusPx = 0;
        }

        // CreateRoundRectRgn ellipse width/height are the full ellipse diameters (= 2 × radius).
        var ellipse = radiusPx * 2;

        return new RoundWindowRegionParams(
            Left: physLeft - windowLeft,
            Top: physTop - windowTop,
            Right: physRight - windowLeft,
            Bottom: physBottom - windowTop,
            EllipseWidth: ellipse,
            EllipseHeight: ellipse);
    }
}

/// <summary>HWND-relative round-rect region for <c>CreateRoundRectRgn</c> / <c>SetWindowRgn</c>.</summary>
public readonly record struct RoundWindowRegionParams(
    int Left,
    int Top,
    int Right,
    int Bottom,
    int EllipseWidth,
    int EllipseHeight)
{
    public bool IsValid => Right > Left && Bottom > Top;
}
