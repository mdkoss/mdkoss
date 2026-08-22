using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Extensions.ModServer;
using MDKOSS.Sample.Modbus.Machine;

namespace MDKOSS.Tests.Sample.Modbus;

public sealed class HoldingRegisterBankTests
{
    [Fact]
    public void Default_bank_covers_200_holding_registers()
    {
        using var drv = CreateSimDriver();
        Assert.Equal(200, HoldingRegisterBank.DefaultCount);

        var written = HoldingRegisterBank.FillPattern(drv);
        Assert.Equal(200, written);

        var snap = HoldingRegisterBank.Read(drv);
        Assert.Equal(0, snap.Start);
        Assert.Equal(200, snap.Values.Count);
        Assert.Equal(200, snap.OkCount);
        Assert.True(snap.Connected);
        Assert.Equal((ushort)0xA000, snap.Values[0]);
        Assert.Equal((ushort)0xA007, snap.Values[7]);
        Assert.Equal((ushort)0xA0C7, snap.Values[199]);
    }

    [Fact]
    public void WriteOne_and_WriteMany_roundtrip()
    {
        using var drv = CreateSimDriver();
        Assert.True(HoldingRegisterBank.WriteOne(drv, 42, 0xBEEF));
        Assert.True(drv.TryRead("holding.42", out var one));
        Assert.Equal((ushort)0xBEEF, Convert.ToUInt16(one));

        var block = new ushort[] { 1, 2, 3, 4, 5 };
        Assert.Equal(5, HoldingRegisterBank.WriteMany(drv, 10, block));
        var snap = HoldingRegisterBank.Read(drv, 10, 5);
        Assert.Equal(block, snap.Values.ToArray());
    }

    private static DrvModbus CreateSimDriver()
    {
        var drv = new DrvModbus();
        drv.Initialize(new MdkSetting.DriverConfig
        {
            Id = "drv-modbus",
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
