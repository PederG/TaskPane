namespace TaskPane.Core.Placement;

/// <summary>
/// Pure geometry helper that keeps a window rectangle visible when monitors change.
/// Coordinates are in the same logical units the caller uses (WPF DIPs or screen pixels).
/// </summary>
public static class WindowPlacementCalculator
{
    public readonly record struct Rect(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;

        public bool Intersects(Rect other)
            => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
    }

    /// <summary>
    /// Clamps size to min/max, then ensures at least <paramref name="edgeMargin"/> of the window
    /// intersects a work area. Prefers the monitor whose device name matches when provided.
    /// </summary>
    public static Rect EnsureVisible(
        Rect window,
        IReadOnlyList<Rect> workAreas,
        double minWidth,
        double minHeight,
        double maxWidth,
        double maxHeight,
        double edgeMargin = 40,
        int preferredWorkAreaIndex = -1)
    {
        if (workAreas.Count == 0)
        {
            return ClampSize(window, minWidth, minHeight, maxWidth, maxHeight);
        }

        var sized = ClampSize(window, minWidth, minHeight, maxWidth, maxHeight);
        var preferred = preferredWorkAreaIndex >= 0 && preferredWorkAreaIndex < workAreas.Count
            ? workAreas[preferredWorkAreaIndex]
            : FindBestWorkArea(sized, workAreas);

        var left = sized.Left;
        var top = sized.Top;
        var width = sized.Width;
        var height = sized.Height;

        if (left + width < preferred.Left + edgeMargin)
        {
            left = preferred.Left + edgeMargin;
        }

        if (top + height < preferred.Top + edgeMargin)
        {
            top = preferred.Top + edgeMargin;
        }

        if (left > preferred.Right - edgeMargin)
        {
            left = preferred.Right - width - edgeMargin;
        }

        if (top > preferred.Bottom - edgeMargin)
        {
            top = preferred.Bottom - height - edgeMargin;
        }

        // Final clamp inside preferred work area when the window fits.
        if (width <= preferred.Width)
        {
            left = Math.Clamp(left, preferred.Left, preferred.Right - width);
        }

        if (height <= preferred.Height)
        {
            top = Math.Clamp(top, preferred.Top, preferred.Bottom - height);
        }

        return new Rect(left, top, width, height);
    }

    public static int FindPreferredWorkAreaIndex(
        Rect window,
        IReadOnlyList<Rect> workAreas,
        string? preferredDeviceName,
        IReadOnlyList<string?> deviceNames)
    {
        if (!string.IsNullOrWhiteSpace(preferredDeviceName) && deviceNames.Count == workAreas.Count)
        {
            for (var i = 0; i < deviceNames.Count; i++)
            {
                if (string.Equals(deviceNames[i], preferredDeviceName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        var best = 0;
        var bestArea = 0.0;
        for (var i = 0; i < workAreas.Count; i++)
        {
            var overlap = IntersectionArea(window, workAreas[i]);
            if (overlap > bestArea)
            {
                bestArea = overlap;
                best = i;
            }
        }

        return best;
    }

    private static Rect FindBestWorkArea(Rect window, IReadOnlyList<Rect> workAreas)
    {
        var index = FindPreferredWorkAreaIndex(window, workAreas, preferredDeviceName: null, deviceNames: []);
        return workAreas[index];
    }

    private static Rect ClampSize(Rect window, double minWidth, double minHeight, double maxWidth, double maxHeight)
    {
        var width = Math.Clamp(window.Width, minWidth, maxWidth);
        var height = Math.Clamp(window.Height, minHeight, maxHeight);
        return new Rect(window.Left, window.Top, width, height);
    }

    private static double IntersectionArea(Rect a, Rect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        return (right - left) * (bottom - top);
    }
}
