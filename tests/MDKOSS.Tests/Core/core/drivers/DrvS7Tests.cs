using System.Globalization;
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
    public void Native_merker_and_db_roundtrip_in_simulate_mode()
    {
        using var drv = Create();
        Assert.True(drv.Write("MW10", (ushort)0x1234));
        Assert.True(drv.TryRead("MW10", out var mw));
        Assert.Equal((ushort)0x1234, Convert.ToUInt16(mw, CultureInfo.InvariantCulture));

        Assert.True(drv.Write("M0.2", true));
        Assert.True(drv.TryRead("M0.2", out var mBit));
        Assert.True(Convert.ToBoolean(mBit));

        Assert.True(drv.Write("DB1.DBX0.1", true));
        Assert.True(drv.TryRead("DB1.DBX0.1", out var dbx));
        Assert.True(Convert.ToBoolean(dbx));

        Assert.True(drv.Write("DB1.DBW2", (ushort)99));
        Assert.True(drv.TryRead("DB1.DBW2", out var dbw));
        Assert.Equal((ushort)99, Convert.ToUInt16(dbw, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Driver_memory_keys_are_not_stolen_as_s7_addresses()
    {
        using var drv = Create();
        Assert.True(drv.TryRead("driver.id", out var id));
        Assert.Equal("s7", Convert.ToString(id, CultureInfo.InvariantCulture));
        Assert.True(drv.TryRead("driver.cpu", out var cpu));
        Assert.Equal("S71200", Convert.ToString(cpu, CultureInfo.InvariantCulture));
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
