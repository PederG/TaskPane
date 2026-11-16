using TaskPane.Core.Overlay;

namespace TaskPane.Core.Tests;

public sealed class OverlayChromeHeightMemoryTests
{
    [Fact]
    public void Collapse_after_tall_resize_snaps_to_mini_and_remembers_expanded_height()
    {
        var memory = new OverlayChromeHeightMemory();
        const double resized = 640;

        var mini = memory.OnCollapse(resized);

        Assert.Equal(OverlaySessionChrome.MiniMinHeightDip, mini);
        Assert.Equal(resized, memory.RememberedExpandedHeight);
    }

    [Fact]
    public void Expand_restores_remembered_height_not_mini_strip()
    {
        var memory = new OverlayChromeHeightMemory();
        memory.OnCollapse(520);

        var restored = memory.OnExpand(placementHeightFallback: OverlaySessionChrome.FullMinHeightDip);

        Assert.Equal(520, restored);
        Assert.True(restored > OverlaySessionChrome.MiniMinHeightDip);
    }

    [Fact]
    public void Expand_without_prior_collapse_uses_placement_fallback()
    {
        var memory = new OverlayChromeHeightMemory();

        var restored = memory.OnExpand(placementHeightFallback: 480);

        Assert.Equal(480, restored);
        Assert.Equal(480, memory.RememberedExpandedHeight);
    }

    [Fact]
    public void Persistence_while_mini_keeps_remembered_expanded_height()
    {
        var memory = new OverlayChromeHeightMemory();
        memory.OnCollapse(700);

        var persisted = memory.HeightForPersistence(
            isMini: true,
            currentHeight: OverlaySessionChrome.MiniMinHeightDip,
            placementHeight: 300);

        Assert.Equal(700, persisted);
    }

    [Fact]
    public void Persistence_while_full_updates_remembered_from_live_height()
    {
        var memory = new OverlayChromeHeightMemory();
        memory.OnCollapse(400);

        var persisted = memory.HeightForPersistence(
            isMini: false,
            currentHeight: 560,
            placementHeight: 400);

        Assert.Equal(560, persisted);
        Assert.Equal(560, memory.RememberedExpandedHeight);
    }

    [Fact]
    public void Collapse_ignores_already_mini_height_when_remembering()
    {
        var memory = new OverlayChromeHeightMemory();
        memory.OnCollapse(610);

        memory.OnCollapse(OverlaySessionChrome.MiniMinHeightDip);

        Assert.Equal(610, memory.RememberedExpandedHeight);
        Assert.Equal(OverlaySessionChrome.MiniMinHeightDip, memory.OnCollapse(50));
    }
}
