using TaskPane.Core.Overlay;

namespace TaskPane.Core.Tests;

public sealed class OverlaySessionChromeTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void ShouldShowMini_matches_active_session_and_focus(
        bool hasActiveSession,
        bool isOverlayFocused,
        bool expectedMini)
    {
        Assert.Equal(
            expectedMini,
            OverlaySessionChrome.ShouldShowMini(hasActiveSession, isOverlayFocused));
    }

    [Fact]
    public void Stopping_timer_while_unfocused_expands_to_full_chrome()
    {
        // Active + unfocused → mini
        Assert.True(OverlaySessionChrome.ShouldShowMini(hasActiveSession: true, isOverlayFocused: false));
        // Timer stopped, still unfocused → full (not stay mini)
        Assert.False(OverlaySessionChrome.ShouldShowMini(hasActiveSession: false, isOverlayFocused: false));
    }

    [Fact]
    public void Focus_while_timer_running_expands_to_full_chrome()
    {
        Assert.True(OverlaySessionChrome.ShouldShowMini(hasActiveSession: true, isOverlayFocused: false));
        Assert.False(OverlaySessionChrome.ShouldShowMini(hasActiveSession: true, isOverlayFocused: true));
    }

    [Fact]
    public void MinHeightDip_shrinks_for_mini_mode()
    {
        Assert.Equal(OverlaySessionChrome.FullMinHeightDip, OverlaySessionChrome.MinHeightDip(isMini: false));
        Assert.Equal(OverlaySessionChrome.MiniMinHeightDip, OverlaySessionChrome.MinHeightDip(isMini: true));
        Assert.True(OverlaySessionChrome.MiniMinHeightDip < OverlaySessionChrome.FullMinHeightDip);
    }
}
