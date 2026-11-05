using TaskPane.Core.Interop;

namespace TaskPane.Core.Tests;

public class RoundWindowRegionGeometryTests
{
    [Fact]
    public void Compute_maps_root_surface_to_hwnd_relative_at_100_percent_dpi()
    {
        // PointToScreen already physical; 1px left/top inset; radius unscaled at 100%.
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 100,
            windowTop: 200,
            rootScreenLeft: 101,
            rootScreenTop: 201,
            rootScreenRight: 101 + 340,
            rootScreenBottom: 201 + 400,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0,
            radiusDip: 18);

        Assert.Equal(2, region.Left);   // (101-100) + 1 inset
        Assert.Equal(2, region.Top);    // (201-200) + 1 inset
        Assert.Equal(341, region.Right);
        Assert.Equal(401, region.Bottom);
        Assert.Equal(36, region.EllipseWidth);
        Assert.Equal(36, region.EllipseHeight);
        Assert.True(region.IsValid);
    }

    [Theory]
    [InlineData(1.25, 44)] // 125%: 2 * round(18*1.25)=2*22 (22.5 → ToEven)
    [InlineData(1.5, 54)]  // 150%: 2 * round(18*1.5)=2*27
    [InlineData(2.0, 72)]  // 200%: 2 * round(18*2)=2*36
    public void Compute_scales_radius_only_at_high_dpi(double dpiScale, int expectedEllipse)
    {
        // Bounds stay 1:1 with PointToScreen/GetWindowRect; only radius uses DpiScale.
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 150,
            windowTop: 300,
            rootScreenLeft: 152,
            rootScreenTop: 302,
            rootScreenRight: 452,
            rootScreenBottom: 452,
            dpiScaleX: dpiScale,
            dpiScaleY: dpiScale,
            radiusDip: 18);

        Assert.Equal(3, region.Left);   // (152-150) + 1
        Assert.Equal(3, region.Top);    // (302-300) + 1
        Assert.Equal(302, region.Right);
        Assert.Equal(152, region.Bottom);
        Assert.Equal(expectedEllipse, region.EllipseWidth);
        Assert.Equal(expectedEllipse, region.EllipseHeight);
    }

    [Fact]
    public void Compute_125_percent_dpi_radius_uses_bankers_round()
    {
        // 18 * 1.25 = 22.5 → Math.Round default (ToEven) → 22; ellipse = 44.
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 0,
            windowTop: 0,
            rootScreenLeft: 0,
            rootScreenTop: 0,
            rootScreenRight: 200,
            rootScreenBottom: 200,
            dpiScaleX: 1.25,
            dpiScaleY: 1.25,
            radiusDip: 18,
            leftTopInsetPhysicalPx: 0);

        Assert.Equal(0, region.Left);
        Assert.Equal(0, region.Top);
        Assert.Equal(200, region.Right);
        Assert.Equal(200, region.Bottom);
        Assert.Equal(44, region.EllipseWidth);
        Assert.Equal(44, region.EllipseHeight);
    }

    [Fact]
    public void Compute_150_percent_dpi_does_not_rescale_point_to_screen_bounds()
    {
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 150,
            windowTop: 300,
            rootScreenLeft: 152,
            rootScreenTop: 302,
            rootScreenRight: 452,
            rootScreenBottom: 402,
            dpiScaleX: 1.5,
            dpiScaleY: 1.5,
            radiusDip: 18,
            leftTopInsetPhysicalPx: 0);

        // If bounds were wrongly ×1.5: left would be round(152*1.5)-150 = 78 — not 2.
        Assert.Equal(2, region.Left);
        Assert.Equal(2, region.Top);
        Assert.Equal(302, region.Right);
        Assert.Equal(102, region.Bottom);
        Assert.Equal(54, region.EllipseWidth);
    }

    [Fact]
    public void Compute_200_percent_dpi_ellipse_is_twice_physical_radius()
    {
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 0,
            windowTop: 0,
            rootScreenLeft: 0,
            rootScreenTop: 0,
            rootScreenRight: 100,
            rootScreenBottom: 100,
            dpiScaleX: 2.0,
            dpiScaleY: 2.0,
            radiusDip: 18,
            leftTopInsetPhysicalPx: 0);

        Assert.Equal(72, region.EllipseWidth);
        Assert.Equal(72, region.EllipseHeight);
    }

    [Fact]
    public void Compute_left_top_inset_only_does_not_move_right_bottom()
    {
        var noInset = RoundWindowRegionGeometry.Compute(
            windowLeft: 10,
            windowTop: 20,
            rootScreenLeft: 10,
            rootScreenTop: 20,
            rootScreenRight: 110,
            rootScreenBottom: 120,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0,
            radiusDip: 18,
            leftTopInsetPhysicalPx: 0);

        var inset = RoundWindowRegionGeometry.Compute(
            windowLeft: 10,
            windowTop: 20,
            rootScreenLeft: 10,
            rootScreenTop: 20,
            rootScreenRight: 110,
            rootScreenBottom: 120,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0,
            radiusDip: 18,
            leftTopInsetPhysicalPx: 1);

        Assert.Equal(0, noInset.Left);
        Assert.Equal(0, noInset.Top);
        Assert.Equal(1, inset.Left);
        Assert.Equal(1, inset.Top);
        Assert.Equal(noInset.Right, inset.Right);
        Assert.Equal(noInset.Bottom, inset.Bottom);
    }

    [Fact]
    public void Compute_invalid_when_degenerate()
    {
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 0,
            windowTop: 0,
            rootScreenLeft: 10,
            rootScreenTop: 10,
            rootScreenRight: 10,
            rootScreenBottom: 10,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0,
            radiusDip: 18,
            leftTopInsetPhysicalPx: 0);

        Assert.False(region.IsValid);
    }

    [Fact]
    public void Compute_treats_non_positive_dpi_as_identity_for_radius()
    {
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 50,
            windowTop: 60,
            rootScreenLeft: 52,
            rootScreenTop: 63,
            rootScreenRight: 152,
            rootScreenBottom: 163,
            dpiScaleX: 0,
            dpiScaleY: -1,
            radiusDip: 18,
            leftTopInsetPhysicalPx: 0);

        Assert.Equal(2, region.Left);
        Assert.Equal(3, region.Top);
        Assert.Equal(102, region.Right);
        Assert.Equal(103, region.Bottom);
        Assert.Equal(36, region.EllipseWidth);
    }
}
