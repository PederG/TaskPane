namespace TaskPane.Core.Interop;

/// <summary>
/// Pure alpha math for overlay surface translucency. Prefer scaling brush alpha over
/// <c>UIElement.Opacity</c> / <c>Window.Opacity</c> on <c>AllowsTransparency</c> hosts —
/// element opacity often composites against white instead of the desktop.
/// </summary>
public static class OverlaySurfaceOpacity
{
    /// <summary>
    /// Scales a base channel alpha by a user opacity factor in [0, 1].
    /// </summary>
    public static byte ScaleAlpha(byte baseAlpha, double opacity)
    {
        if (opacity <= 0)
        {
            return 0;
        }

        if (opacity >= 1)
        {
            return baseAlpha;
        }

        return (byte)Math.Round(baseAlpha * opacity);
    }
}
