using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core.core.drivers;

public sealed class DrvSimIoBitBaseTests
{
    [Fact]
    public void Default_is_0base_so_bit0_is_first_port_bit()
    {
        var drv = Create();
        Assert.True(drv.Write("do.gpo.bit.0", true));
        Assert.True(drv.TryRead("do.gpo.bit.0", out var bit));
        Assert.True(Assert.IsType<bool>(bit));
        Assert.True(drv.TryRead("do.gpo", out var word));
        Assert.Equal(1, Convert.ToInt32(word, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Default_rejects_negative_shift()
    {
        var drv = Create();
        Assert.False(drv.Write("do.gpo.bit.-1", true));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1base")]
    [InlineData("gts")]
    public void IoBitBase_1_maps_bit1_to_first_port_bit(string ioBitBase)
    {
        var drv = Create(ioBitBase);
        Assert.False(drv.Write("do.gpo.bit.0", true));
        Assert.True(drv.Write("do.gpo.bit.1", true));
        Assert.True(drv.TryRead("do.gpo.bit.1", out var bit));
        Assert.True(Assert.IsType<bool>(bit));
        Assert.True(drv.TryRead("do.gpo", out var word));
        Assert.Equal(1, Convert.ToInt32(word, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void IoBitBase_0_and_1_do_not_clobber_neighbor_bits()
    {
        var zero = Create("0");
        Assert.True(zero.Write("do.gpo.bit.0", true));
        Assert.True(zero.Write("do.gpo.bit.1", true));
        Assert.True(zero.Write("do.gpo.bit.0", false));
        Assert.True(zero.TryRead("do.gpo.bit.0", out var b0));
        Assert.True(zero.TryRead("do.gpo.bit.1", out var b1));
        Assert.False(Assert.IsType<bool>(b0));
        Assert.True(Assert.IsType<bool>(b1));

        var one = Create("1");
        Assert.True(one.Write("do.gpo.bit.1", true));
        Assert.True(one.Write("do.gpo.bit.2", true));
        Assert.True(one.Write("do.gpo.bit.1", false));
        Assert.True(one.TryRead("do.gpo.bit.1", out var gts0));
        Assert.True(one.TryRead("do.gpo.bit.2", out var gts1));
        Assert.False(Assert.IsType<bool>(gts0));
        Assert.True(Assert.IsType<bool>(gts1));
    }

    private static DrvSim Create(string? ioBitBase = null)
    {
        var drv = new DrvSim();
        var config = new MdkSetting.DriverConfig { Id = "sim", Type = "sim", Enabled = true };
        if (ioBitBase is not null)
        {
            config.Parameters["ioBitBase"] = ioBitBase;
        }

        drv.Initialize(config);
        return drv;
    }
}
