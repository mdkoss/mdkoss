using MDKOSS.Core.Drivers;
using MDKOSS.Drivers.Dmc;

namespace MDKOSS.Tests.Core.Drivers;

public sealed class DmcIoMapTests
{
    [Fact]
    public void Native_bit_is_zero_based_passthrough()
    {
        Assert.True(DmcIoMap.IsGeneral(GtsIoType.Gpi));
        Assert.True(DmcIoMap.IsGeneral(GtsIoType.Gpo));
        Assert.False(DmcIoMap.IsGeneral(GtsIoType.Home));

        Assert.True(DmcIoMap.TryNativeBit(0, out var bit0));
        Assert.Equal((ushort)0, bit0);
        Assert.True(DmcIoMap.TryNativeBit(15, out var bit15));
        Assert.Equal((ushort)15, bit15);
        Assert.False(DmcIoMap.TryNativeBit(-1, out _));
    }

    [Fact]
    public void Address_do_gpo_bit_0_is_first_dmc_output()
    {
        Assert.True(DriverIoAddress.TryParse("do.gpo.bit.0", out var io));
        Assert.True(io.IsOutput);
        Assert.True(DmcIoMap.IsGeneral(io.Type));
        Assert.True(DmcIoMap.TryNativeBit(io.BitIndex!.Value, out var bitno));
        Assert.Equal((ushort)0, bitno);
    }

    [Fact]
    public void Axis_status_masks()
    {
        Assert.True(DmcIoMap.TryAxisStatusMask(GtsIoType.Home, out var home));
        Assert.Equal(DmcIoMap.AxisOrg, home);
        Assert.True(DmcIoMap.TryAxisStatusMask(GtsIoType.Alarm, out var alm));
        Assert.Equal(DmcIoMap.AxisAlm, alm);
        Assert.False(DmcIoMap.TryAxisStatusMask(GtsIoType.Gpi, out _));
        Assert.True(DmcIoMap.IsServoEnable(GtsIoType.Enable));
        Assert.True(DmcIoMap.IsAlarmClear(GtsIoType.Clear));
    }
}
