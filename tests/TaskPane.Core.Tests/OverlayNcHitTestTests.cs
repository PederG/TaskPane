using TaskPane.Core.Interop;

namespace TaskPane.Core.Tests;

public class OverlayNcHitTestTests
{
    private const double W = 340;
    private const double H = 400;
    private const double Border = 6;
    private const double Caption = 14;

    // Visible RootSurface flush with window for most cases.
    private static int Decide(
        double x,
        double y,
        bool locked = false,
        bool overChrome = false,
        double visL = 0,
        double visT = 0,
        double visR = W,
        double visB = H,
        double captionBand = Caption)
        => OverlayNcHitTest.Decide(
            x, y, W, H, Border, captionBand, locked, overChrome, visL, visT, visR, visB);

    [Fact]
    public void Top_left_corner_is_resize_not_caption()
    {
        Assert.Equal(OverlayNcHitTest.HtTopLeft, Decide(2, 2));
        Assert.Equal(OverlayNcHitTest.HtTopLeft, Decide(5, 5));
    }

    [Fact]
    public void Top_right_corner_is_resize_not_caption()
    {
        Assert.Equal(OverlayNcHitTest.HtTopRight, Decide(W - 2, 2));
        Assert.Equal(OverlayNcHitTest.HtTopRight, Decide(W - 5, 5));
    }

    [Fact]
    public void Top_center_padding_band_is_caption()
    {
        // Inside upper padding (≈14 DIP), away from side resize borders.
        Assert.Equal(OverlayNcHitTest.HtCaption, Decide(W / 2, 3));
        Assert.Equal(OverlayNcHitTest.HtCaption, Decide(W / 2, 10));
        Assert.Equal(OverlayNcHitTest.HtCaption, Decide(W / 2, 13));
    }

    [Fact]
    public void Below_caption_band_is_client_not_top_resize()
    {
        Assert.Equal(OverlayNcHitTest.HtClient, Decide(W / 2, 20));
    }

    [Fact]
    public void Position_locked_suppresses_caption()
    {
        Assert.Equal(OverlayNcHitTest.HtClient, Decide(W / 2, 4, locked: true));
        // Corners still resize when locked (move locked ≠ size locked).
        Assert.Equal(OverlayNcHitTest.HtTopLeft, Decide(2, 2, locked: true));
    }

    [Fact]
    public void Interactive_header_chrome_suppresses_caption()
    {
        Assert.Equal(
            OverlayNcHitTest.HtClient,
            Decide(W - 40, 8, overChrome: true));
    }

    [Fact]
    public void Side_and_bottom_edges_remain_resize()
    {
        Assert.Equal(OverlayNcHitTest.HtLeft, Decide(2, H / 2));
        Assert.Equal(OverlayNcHitTest.HtRight, Decide(W - 2, H / 2));
        Assert.Equal(OverlayNcHitTest.HtBottom, Decide(W / 2, H - 2));
        Assert.Equal(OverlayNcHitTest.HtBottomLeft, Decide(2, H - 2));
        Assert.Equal(OverlayNcHitTest.HtBottomRight, Decide(W - 2, H - 2));
    }

    [Fact]
    public void Caption_strip_limited_to_visible_region()
    {
        // Point in window top padding but outside inset visible RootSurface → client.
        Assert.Equal(
            OverlayNcHitTest.HtClient,
            Decide(5, 3, visL: 10, visT: 10, visR: W - 10, visB: H - 10));

        // Inside visible top padding band → caption.
        Assert.Equal(
            OverlayNcHitTest.HtCaption,
            Decide(W / 2, 12, visL: 10, visT: 10, visR: W - 10, visB: H - 10));
    }

    [Fact]
    public void Visible_inset_corners_use_visible_edges()
    {
        Assert.Equal(
            OverlayNcHitTest.HtTopLeft,
            Decide(12, 12, visL: 10, visT: 10, visR: W - 10, visB: H - 10));
    }

    [Fact]
    public void Thin_caption_band_allows_httop_below_it_inside_resize_border()
    {
        // Caption band 2 DIP; resize border 6 — y=4 is onTop but outside caption → HTTOP.
        Assert.Equal(
            OverlayNcHitTest.HtTop,
            Decide(W / 2, 4, captionBand: 2));
        Assert.Equal(
            OverlayNcHitTest.HtCaption,
            Decide(W / 2, 1, captionBand: 2));
    }
}
