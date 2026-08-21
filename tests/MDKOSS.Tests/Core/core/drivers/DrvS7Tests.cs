using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Drivers.S7;

namespace MDKOSS.Tests.Core;

public sealed class DrvS7Tests
{
    [Fact]
    public void Simulate_mode_connects_without_host()
    {
        using var drv = Create();
        Assert.True(drv.IsConnected);
        Assert.Equal("S7", drv.Name);
    }

    [Fact]
    public void Do_bit_roundtrip_in_simulate_mode()
    {
        using var drv = Create();
        Assert.True(drv.Write("do.gpo.bit.0", true));
        Assert.True(drv.TryRead("do.gpo.bit.0", out var bit));
        Assert.True(Convert.ToBoolean(bit));

        Assert.True(drv.WriteDoBit(GtsIoType.Gpo, 3, true));
        Assert.True(drv.TryReadDo(GtsIoType.Gpo, out var word));
        Assert.Equal(0b1001, word & 0b1111);
    }

    [Fact]
    public void Di_can_be_seeded_in_simulate_mode()
    {
        using var drv = Create();
        Assert.True(drv.Write("do.gpo", 0xA5));
        Assert.True(drv.TryReadDo(GtsIoType.Gpo, out var doWord));
        Assert.Equal(0xA5, doWord & 0xFF);

        Assert.True(drv.Write("di.gpi.bit.2", true));
        Assert.True(drv.TryRead("di.gpi.bit.2", out var diBit));
        Assert.True(Convert.ToBoolean(diBit));
        Assert.True(drv.TryReadDi(GtsIoType.Gpi, out var diWord));
        Assert.Equal(1 << 2, diWord & (1 << 2));
    }

    [Fact]
    public void Native_q_bit_roundtrip_in_simulate_mode()
    {
        using var drv = Create();
        Assert.True(drv.Write("Q0.1", true));
        Assert.True(drv.TryRead("Q0.1", out var bit));
        Assert.True(Convert.ToBoolean(bit));
        Assert.True(drv.TryRead("do.gpo.bit.1", out var viaGpio));
        Assert.True(Convert.ToBoolean(viaGpio));
    }

    [Fact]
    public void Axis_apis_return_false()
    {
        using var drv = Create();
        Assert.False(drv.EnableAxis(0));
        Assert.False(drv.MoveAxisJog(0, 10, 1, 1));
        Assert.False(drv.TryGetAxisPrfPosition(0, out _));
    }

    [Fact]
    public void Factory_registers_s7_types()
    {
        TestPluginBootstrap.EnsureRegistered();
        Assert.True(DriverFactory.IsSupported("s7"));
        Assert.True(DriverFactory.IsSupported("s7-1200"));
        Assert.Contains("driver-s7", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);

        using var d = DriverFactory.Create("s7-1200");
        d.Initialize(new MdkSetting.DriverConfig
        {
            Id = "s7x",
            Type = "s7-1200",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "true",
            },
        });
        Assert.True(d.IsConnected);
    }

    private static DrvS7 Create()
    {
        var drv = new DrvS7();
        drv.Initialize(new MdkSetting.DriverConfig
        {
            Id = "s7",
            Type = "s7-1200",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "true",
            },
        });
        return drv;
    }
}
