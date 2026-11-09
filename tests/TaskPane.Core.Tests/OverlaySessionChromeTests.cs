using TaskPane.Core.Overlay;

namespace TaskPane.Core.Tests;

public sealed class OverlaySessionChromeTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ShouldShowMini_follows_focus_only(bool isOverlayFocused, bool expectedMini)
    {
        Assert.Equal(expectedMini, OverlaySessionChrome.ShouldShowMini(isOverlayFocused));
    }

    [Fact]
    public void Idle_unfocused_overlay_stays_in_mini_strip()
    {
        // No active timer, unfocused → still mini (brand + expand; not full chrome).
        Assert.True(OverlaySessionChrome.ShouldShowMini(isOverlayFocused: false));
    }

    [Fact]
    public void Stopping_timer_while_unfocused_keeps_mini_strip()
    {
        // Active + unfocused → mini
        Assert.True(OverlaySessionChrome.ShouldShowMini(isOverlayFocused: false));
        // Timer stopped, still unfocused → remain mini (idle brand strip)
        Assert.True(OverlaySessionChrome.ShouldShowMini(isOverlayFocused: false));
    }

    [Fact]
    public void Focus_expands_to_full_chrome_whether_or_not_timer_runs()
    {
        Assert.True(OverlaySessionChrome.ShouldShowMini(isOverlayFocused: false));
        Assert.False(OverlaySessionChrome.ShouldShowMini(isOverlayFocused: true));
    }

    [Fact]
    public void MinHeightDip_shrinks_for_mini_mode()
    {
        Assert.Equal(OverlaySessionChrome.FullMinHeightDip, OverlaySessionChrome.MinHeightDip(isMini: false));
        Assert.Equal(OverlaySessionChrome.MiniMinHeightDip, OverlaySessionChrome.MinHeightDip(isMini: true));
        Assert.True(OverlaySessionChrome.MiniMinHeightDip < OverlaySessionChrome.FullMinHeightDip);
    }
}
