using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions.ModServer;

namespace MDKOSS.Tests.Core;

public sealed class DrvModbusTests
{
    [Fact]
    public void Simulate_mode_connects_without_host()
    {
        using var drv = Create();
        Assert.True(drv.IsConnected);
        Assert.Equal("MODBUS", drv.Name);
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
    public void Native_coil_and_holding_roundtrip_in_simulate_mode()
    {
        using var drv = Create();
        Assert.True(drv.Write("coil.7", true));
        Assert.True(drv.TryRead("coil.7", out var coil));
        Assert.True(Convert.ToBoolean(coil));

        Assert.True(drv.Write("holding.3", 0x1234));
        Assert.True(drv.TryRead("holding.3", out var hr));
        Assert.Equal((ushort)0x1234, Convert.ToUInt16(hr));
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
    public void Factory_registers_modbus_types()
    {
        TestPluginBootstrap.EnsureRegistered();
        Assert.True(DriverFactory.IsSupported("modbus"));
        Assert.True(DriverFactory.IsSupported("modbus-tcp"));
        Assert.Contains("modserver", MDKOSS.Extensions.MdkExtensionHost.RegisteredIds);

        using var d = DriverFactory.Create("modbus-tcp");
        d.Initialize(new MdkSetting.DriverConfig
        {
            Id = "modx",
            Type = "modbus-tcp",
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "true",
            },
        });
        Assert.True(d.IsConnected);
    }

    private static DrvModbus Create()
    {
        var drv = new DrvModbus();
        drv.Initialize(new MdkSetting.DriverConfig
        {
            Id = "modbus",
            Type = "modbus-tcp",
            Enabled = true,
            Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulate"] = "true",
            },
        });
        return drv;
    }
}
