using TaskPane.Core.Placement;

namespace TaskPane.App.Services;

/// <summary>Reads WinForms screen work areas for placement restore after monitor changes.</summary>
public static class MonitorWorkAreaProbe
{
    public static IReadOnlyList<WindowPlacementCalculator.Rect> GetWorkAreas()
    {
        var list = new List<WindowPlacementCalculator.Rect>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var wa = screen.WorkingArea;
            list.Add(new WindowPlacementCalculator.Rect(wa.Left, wa.Top, wa.Width, wa.Height));
        }

        if (list.Count == 0)
        {
            var fallback = System.Windows.SystemParameters.WorkArea;
            list.Add(new WindowPlacementCalculator.Rect(fallback.Left, fallback.Top, fallback.Width, fallback.Height));
        }

        return list;
    }

    public static IReadOnlyList<string?> GetDeviceNames()
    {
        var list = new List<string?>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            list.Add(screen.DeviceName);
        }

        return list;
    }
}
