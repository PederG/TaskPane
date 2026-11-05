using TaskPane.Core.Interop;

namespace TaskPane.Core.Tests;

public class OverlayNcHitTestTests
{
    private const double W = 340;
    private const double H = 400;
    private const double Border = 6;
    private const double Corner = 16;
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
        double captionBand = Caption,
        double cornerHit = Corner)
        => OverlayNcHitTest.Decide(
            x, y, W, H, Border, cornerHit, captionBand, locked, overChrome, visL, visT, visR, visB);

    [Fact]
    public void Top_left_corner_is_resize_not_caption()
    {
        Assert.Equal(OverlayNcHitTest.HtTopLeft, Decide(2, 2));
        Assert.Equal(OverlayNcHitTest.HtTopLeft, Decide(5, 5));
        // Enlarged corner zone beyond the 6 DIP edge strip.
        Assert.Equal(OverlayNcHitTest.HtTopLeft, Decide(12, 12));
    }

    [Fact]
    public void Top_right_corner_is_resize_not_caption()
    {
        Assert.Equal(OverlayNcHitTest.HtTopRight, Decide(W - 2, 2));
        Assert.Equal(OverlayNcHitTest.HtTopRight, Decide(W - 5, 5));
        Assert.Equal(OverlayNcHitTest.HtTopRight, Decide(W - 12, 12));
    }

    [Fact]
    public void Top_center_padding_band_is_caption()
    {
        // Inside upper padding (≈14 DIP), away from side resize / corner zones.
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
    public void Interactive_header_chrome_suppresses_caption_and_corners()
    {
        Assert.Equal(
            OverlayNcHitTest.HtClient,
            Decide(W - 40, 8, overChrome: true));
        // Even inside the enlarged top-right corner square, buttons stay HTCLIENT.
        Assert.Equal(
            OverlayNcHitTest.HtClient,
            Decide(W - 4, 4, overChrome: true));
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
    public void Bottom_right_corner_wins_over_bottom_and_right_edge_strips()
    {
        // Outside the 6 DIP edge strip but inside the 16 DIP corner square → diagonal, not edge.
        Assert.Equal(OverlayNcHitTest.HtBottomRight, Decide(W - 10, H - 10));
        Assert.Equal(OverlayNcHitTest.HtBottomRight, Decide(W - 14, H - 3));
        Assert.Equal(OverlayNcHitTest.HtBottomRight, Decide(W - 3, H - 14));

        // Past the corner square on the bottom edge → HTBOTTOM (not right).
        Assert.Equal(OverlayNcHitTest.HtBottom, Decide(W / 2, H - 3));
        // Past the corner square on the right edge → HTRIGHT (not bottom).
        Assert.Equal(OverlayNcHitTest.HtRight, Decide(W - 3, H / 2));
    }

    [Fact]
    public void Bottom_left_corner_wins_over_edge_strips()
    {
        Assert.Equal(OverlayNcHitTest.HtBottomLeft, Decide(10, H - 10));
        Assert.Equal(OverlayNcHitTest.HtBottomLeft, Decide(14, H - 3));
        Assert.Equal(OverlayNcHitTest.HtLeft, Decide(3, H / 2));
    }

    [Fact]
    public void Corner_hit_never_smaller_than_edge_border()
    {
        // cornerHit < border → effectively uses border; (5, H-5) still diagonal.
        Assert.Equal(
            OverlayNcHitTest.HtBottomRight,
            Decide(W - 5, H - 5, cornerHit: 3));
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
        Assert.Equal(
            OverlayNcHitTest.HtBottomRight,
            Decide(W - 20, H - 20, visL: 10, visT: 10, visR: W - 10, visB: H - 10));
    }

    [Fact]
    public void Thin_caption_band_allows_httop_below_it_inside_resize_border()
    {
        // Caption band 2 DIP; resize border 6 — y=4 is onTop but outside caption → HTTOP.
        // Stay away from enlarged corner zones (center X).
        Assert.Equal(
            OverlayNcHitTest.HtTop,
            Decide(W / 2, 4, captionBand: 2));
        Assert.Equal(
            OverlayNcHitTest.HtCaption,
            Decide(W / 2, 1, captionBand: 2));
    }
}
