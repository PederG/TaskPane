using TaskPane.Core.Placement;

namespace TaskPane.Core.Tests;

public sealed class WindowPlacementCalculatorTests
{
    [Fact]
    public void Moves_window_onto_remaining_work_area_when_monitor_missing()
    {
        var offscreen = new WindowPlacementCalculator.Rect(3000, 100, 360, 480);
        var primary = new WindowPlacementCalculator.Rect(0, 0, 1920, 1040);
        var restored = WindowPlacementCalculator.EnsureVisible(
            offscreen,
            [primary],
            minWidth: 280,
            minHeight: 220,
            maxWidth: 900,
            maxHeight: 1200);

        Assert.True(restored.Left >= primary.Left);
        Assert.True(restored.Top >= primary.Top);
        Assert.True(restored.Right <= primary.Right);
        Assert.True(restored.Bottom <= primary.Bottom);
        Assert.Equal(360, restored.Width);
        Assert.Equal(480, restored.Height);
    }

    [Fact]
    public void Prefers_named_monitor_when_still_present()
    {
        var left = new WindowPlacementCalculator.Rect(0, 0, 1920, 1080);
        var right = new WindowPlacementCalculator.Rect(1920, 0, 1920, 1080);
        var window = new WindowPlacementCalculator.Rect(2000, 40, 400, 500);
        var index = WindowPlacementCalculator.FindPreferredWorkAreaIndex(
            window,
            [left, right],
            preferredDeviceName: @"\\.\DISPLAY2",
            deviceNames: [@"\\.\DISPLAY1", @"\\.\DISPLAY2"]);

        Assert.Equal(1, index);
        var restored = WindowPlacementCalculator.EnsureVisible(
            window,
            [left, right],
            280,
            220,
            900,
            1200,
            preferredWorkAreaIndex: index);
        Assert.True(restored.Left >= right.Left);
    }

    [Fact]
    public void Clamps_oversized_window()
    {
        var area = new WindowPlacementCalculator.Rect(0, 0, 800, 600);
        var restored = WindowPlacementCalculator.EnsureVisible(
            new WindowPlacementCalculator.Rect(10, 10, 2000, 2000),
            [area],
            280,
            220,
            900,
            1200);
        Assert.Equal(900, restored.Width);
        Assert.Equal(1200, restored.Height);
    }
}
