using TaskPane.Core.Interop;

namespace TaskPane.Core.Tests;

public class RoundWindowRegionGeometryTests
{
    [Fact]
    public void Compute_maps_root_surface_to_hwnd_relative_at_96dpi()
    {
        // Window at (100, 200); RootSurface inset 1 DIP from HWND origin — not assumed (0,0).
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 100,
            windowTop: 200,
            rootScreenLeftDip: 101,
            rootScreenTopDip: 201,
            rootScreenRightDip: 101 + 340,
            rootScreenBottomDip: 201 + 400,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0,
            radiusDip: 18);

        Assert.Equal(1, region.Left);
        Assert.Equal(1, region.Top);
        Assert.Equal(341, region.Right);
        Assert.Equal(401, region.Bottom);
        Assert.Equal(36, region.EllipseWidth);
        Assert.Equal(36, region.EllipseHeight);
        Assert.True(region.IsValid);
    }

    [Fact]
    public void Compute_scales_bounds_and_radius_at_150dpi()
    {
        // Physical window origin (150, 300). RootSurface PointToScreen DIPs inset by 1 DIP.
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 150,
            windowTop: 300,
            rootScreenLeftDip: 101,
            rootScreenTopDip: 201,
            rootScreenRightDip: 101 + 200,
            rootScreenBottomDip: 201 + 100,
            dpiScaleX: 1.5,
            dpiScaleY: 1.5,
            radiusDip: 18);

        // phys root TL = round(101*1.5)=152, TRB similarly; relative = phys - window.
        Assert.Equal(2, region.Left);   // 152 - 150
        Assert.Equal(2, region.Top);    // 302 - 300
        Assert.Equal(302, region.Right);  // round(301*1.5)=452 → 452-150=302
        Assert.Equal(152, region.Bottom); // round(301*1.5)=452 → wait bottom: round(301*1.5)
        // rootScreenBottomDip = 301 → 301*1.5 = 451.5 → 452; 452-300 = 152 ✓
        Assert.Equal(54, region.EllipseWidth);  // 2 * round(18*1.5) = 2*27
        Assert.Equal(54, region.EllipseHeight);
    }

    [Fact]
    public void Compute_ellipse_is_twice_physical_radius()
    {
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 0,
            windowTop: 0,
            rootScreenLeftDip: 0,
            rootScreenTopDip: 0,
            rootScreenRightDip: 100,
            rootScreenBottomDip: 100,
            dpiScaleX: 2.0,
            dpiScaleY: 2.0,
            radiusDip: 18);

        Assert.Equal(72, region.EllipseWidth);
        Assert.Equal(72, region.EllipseHeight);
    }

    [Fact]
    public void Compute_invalid_when_degenerate()
    {
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 0,
            windowTop: 0,
            rootScreenLeftDip: 10,
            rootScreenTopDip: 10,
            rootScreenRightDip: 10,
            rootScreenBottomDip: 10,
            dpiScaleX: 1.0,
            dpiScaleY: 1.0,
            radiusDip: 18);

        Assert.False(region.IsValid);
    }

    [Fact]
    public void Compute_treats_non_positive_dpi_as_identity()
    {
        var region = RoundWindowRegionGeometry.Compute(
            windowLeft: 50,
            windowTop: 60,
            rootScreenLeftDip: 52,
            rootScreenTopDip: 63,
            rootScreenRightDip: 152,
            rootScreenBottomDip: 163,
            dpiScaleX: 0,
            dpiScaleY: -1,
            radiusDip: 18);

        Assert.Equal(2, region.Left);
        Assert.Equal(3, region.Top);
        Assert.Equal(102, region.Right);
        Assert.Equal(103, region.Bottom);
        Assert.Equal(36, region.EllipseWidth);
    }
}
