namespace TaskPane.Core.Interop;

/// <summary>
/// Computes HWND-relative <c>CreateRoundRectRgn</c> parameters from physical window rect
/// and RootSurface screen bounds from WPF <c>PointToScreen</c>.
/// Does not assume RootSurface originates at HWND (0,0).
/// </summary>
/// <remarks>
/// <para>
/// On modern DPI-aware WPF, <c>PointToScreen</c> returns screen coordinates already consistent
/// with Win32 <c>GetWindowRect</c> (physical pixels). Do <b>not</b> multiply those by DpiScale
/// again — only the DIP corner radius is converted to physical pixels.
/// </para>
/// <para>
/// <c>CreateRoundRectRgn</c> is binary (no GDI antialiasing). A region that matches RootSurface
/// clips WPF's CornerRadius AA fringe against the desktop → jagged outer silhouette. Expanding
/// the region in physical pixels would let more of that fringe show, but also re-exposes the
/// white HWND host in the corner wedges SetWindowRgn is meant to hide. Shipping keeps the
/// white-host-safe inset (no expand trial constants). Prefer layered per-pixel alpha for a
/// smooth outer edge; keep this region as fallback until that path is Windows-verified.
/// </para>
/// </remarks>
public static class RoundWindowRegionGeometry
{
    /// <summary>
    /// Default inset (physical px) applied to the region LEFT and TOP only. Clips the ~1px
    /// #FFFFFF host hairline observed on those edges after SetWindowRgn without shrinking
    /// right/bottom (avoids biting into RootSurface content on the trailing edges).
    /// </summary>
    public const int DefaultLeftTopInsetPhysicalPx = 1;

    /// <summary>
    /// Builds inclusive-left/top exclusive-right/bottom region coords in HWND-relative
    /// physical pixels, plus ellipse diameters (= 2 × physical corner radius).
    /// </summary>
    /// <param name="windowLeft">GetWindowRect.Left (physical screen px).</param>
    /// <param name="windowTop">GetWindowRect.Top (physical screen px).</param>
    /// <param name="rootScreenLeft">RootSurface top-left X from PointToScreen (screen px).</param>
    /// <param name="rootScreenTop">RootSurface top-left Y from PointToScreen (screen px).</param>
    /// <param name="rootScreenRight">RootSurface bottom-right X from PointToScreen (screen px).</param>
    /// <param name="rootScreenBottom">RootSurface bottom-right Y from PointToScreen (screen px).</param>
    /// <param name="dpiScaleX">Pixels-per-DIP X — used only for radius conversion.</param>
    /// <param name="dpiScaleY">Pixels-per-DIP Y — used only for radius conversion.</param>
    /// <param name="radiusDip">Corner radius in DIP (TpOverlayWindowRadius, typically 18).</param>
    /// <param name="leftTopInsetPhysicalPx">
    /// Extra inset on LEFT and TOP only (default 1). Pass 0 to disable for diagnostics.
    /// </param>
    public static RoundWindowRegionParams Compute(
        int windowLeft,
        int windowTop,
        double rootScreenLeft,
        double rootScreenTop,
        double rootScreenRight,
        double rootScreenBottom,
        double dpiScaleX,
        double dpiScaleY,
        double radiusDip,
        int leftTopInsetPhysicalPx = DefaultLeftTopInsetPhysicalPx)
    {
        // PointToScreen values are already in the same screen space as GetWindowRect.
        var physLeft = (int)Math.Round(rootScreenLeft);
        var physTop = (int)Math.Round(rootScreenTop);
        var physRight = (int)Math.Round(rootScreenRight);
        var physBottom = (int)Math.Round(rootScreenBottom);

        var scale = Math.Min(
            dpiScaleX > 0 ? dpiScaleX : 1.0,
            dpiScaleY > 0 ? dpiScaleY : 1.0);
        var radiusPx = (int)Math.Round(radiusDip * scale);
        if (radiusPx < 0)
        {
            radiusPx = 0;
        }

        // CreateRoundRectRgn ellipse width/height are the full ellipse diameters (= 2 × radius).
        var ellipse = radiusPx * 2;

        var inset = leftTopInsetPhysicalPx < 0 ? 0 : leftTopInsetPhysicalPx;

        return new RoundWindowRegionParams(
            Left: physLeft - windowLeft + inset,
            Top: physTop - windowTop + inset,
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
