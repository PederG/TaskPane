using TaskPane.Core.Interop;

namespace TaskPane.Core.Tests;

public class OverlaySurfaceOpacityTests
{
    [Theory]
    [InlineData(230, 1.0, 230)]
    [InlineData(230, 0.0, 0)]
    [InlineData(230, 0.5, 115)]
    [InlineData(255, 0.45, 115)]
    [InlineData(0, 0.92, 0)]
    public void ScaleAlpha_multiplies_base_channel(byte baseAlpha, double opacity, byte expected)
    {
        Assert.Equal(expected, OverlaySurfaceOpacity.ScaleAlpha(baseAlpha, opacity));
    }

    [Fact]
    public void ScaleAlpha_clamps_out_of_range_opacity()
    {
        Assert.Equal(200, OverlaySurfaceOpacity.ScaleAlpha(200, 1.5));
        Assert.Equal(0, OverlaySurfaceOpacity.ScaleAlpha(200, -0.2));
    }
}
