using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core.core.drivers;

public sealed class DriverInterpTests
{
    [Fact]
    public void Line_rejects_single_axis_and_bad_profile()
    {
        Assert.False(DriverInterp.TryValidateLine([0], [1], 100, 100, 100, out _));
        Assert.False(DriverInterp.TryValidateLine([0, 1], [1], 100, 100, 100, out _));
        Assert.False(DriverInterp.TryValidateLine([0, 0], [1, 2], 100, 100, 100, out _));
        Assert.False(DriverInterp.TryValidateLine([0, 1], [1, 2], 0, 100, 100, out _));
        Assert.True(DriverInterp.TryValidateLine([0, 1], [100, 200], 1000, 5000, 5000, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void Arc_quarter_ccw_has_positive_sweep()
    {
        Assert.True(DriverInterp.TryComputeArc(
            100, 0, 0, 100, 0, 0, clockwise: false,
            out var radius, out var start, out var sweep, out var error));
        Assert.Null(error);
        Assert.Equal(100, radius, 6);
        Assert.Equal(0, start, 6);
        Assert.Equal(Math.PI / 2, sweep, 6);
    }

    [Fact]
    public void Arc_quarter_cw_has_negative_sweep()
    {
        Assert.True(DriverInterp.TryComputeArc(
            100, 0, 0, -100, 0, 0, clockwise: true,
            out _, out _, out var sweep, out _));
        Assert.Equal(-Math.PI / 2, sweep, 6);
    }

    [Fact]
    public void Arc_rejects_point_not_on_circle()
    {
        Assert.False(DriverInterp.TryComputeArc(
            100, 0, 80, 80, 0, 0, clockwise: false,
            out _, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void OverlapsMask_uses_zero_based_bits()
    {
        Assert.True(DriverInterp.OverlapsMask([0, 2], 1 << 2));
        Assert.False(DriverInterp.OverlapsMask([0, 2], 1 << 1));
    }
}
