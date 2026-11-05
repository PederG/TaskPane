using TaskPane.Core.Interop;

namespace TaskPane.Core.Tests;

public class PackedLParamPointTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(100, 200, 0x00C80064)]
    [InlineData(-1, -1, unchecked((int)0xFFFFFFFF))]
    [InlineData(-10, 40, unchecked((int)0x0028FFF6))]
    [InlineData(10, -40, unchecked((int)0xFFD8000A))]
    public void Unpack_signed_screen_coordinates(int expectedX, int expectedY, int packed32)
    {
        // Sign-extend a 32-bit LPARAM the way Win64 delivers it in a 64-bit register.
        long lParam = packed32;

        PackedLParamPoint.Unpack(lParam, out var x, out var y);

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void Unpack_does_not_require_ToInt32_for_high_bits()
    {
        // High bits set in a 64-bit value — ToInt32() would throw; ToInt64 path must work.
        long lParam = unchecked((long)(uint)unchecked((int)0xFFD8000A));

        PackedLParamPoint.Unpack(lParam, out var x, out var y);

        Assert.Equal(10, x);
        Assert.Equal(-40, y);
    }
}
