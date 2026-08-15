using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core.core.drivers;

public sealed class AxisStatusTests
{
    [Fact]
    public void FromGts_decodes_documented_sts_bits()
    {
        var raw = AxisStatusBits.Alarm
            | AxisStatusBits.FollowError
            | AxisStatusBits.PositiveLimit
            | AxisStatusBits.NegativeLimit
            | AxisStatusBits.SmoothStop
            | AxisStatusBits.AbruptStop
            | AxisStatusBits.ServoOn
            | AxisStatusBits.Moving
            | AxisStatusBits.InPosition;

        var status = AxisStatus.FromGts(raw, home: true, prfPosition: 12.5, encPosition: 12.4, velocity: 100);

        Assert.Equal(raw, status.Raw);
        Assert.True(status.Alarm);
        Assert.True(status.FollowError);
        Assert.True(status.PositiveLimit);
        Assert.True(status.NegativeLimit);
        Assert.True(status.SmoothStop);
        Assert.True(status.AbruptStop);
        Assert.True(status.ServoOn);
        Assert.True(status.Moving);
        Assert.True(status.InPosition);
        Assert.True(status.Home);
        Assert.Equal(12.5, status.PrfPosition);
        Assert.Equal(12.4, status.EncPosition);
        Assert.Equal(100, status.Velocity);
        Assert.Contains("ALM", status.FormatFlags(), StringComparison.Ordinal);
        Assert.Contains("ORG", status.FormatFlags(), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_packs_gts_layout_raw_word()
    {
        var status = AxisStatus.Create(alarm: true, servoOn: true, inPosition: true, home: true);
        Assert.Equal(AxisStatusBits.Alarm | AxisStatusBits.ServoOn | AxisStatusBits.InPosition, status.Raw);
        Assert.True(status.Home);
        Assert.False(status.Moving);
    }

    [Fact]
    public void Sim_TryGetAxisState_fills_gts_aligned_flags()
    {
        using var drv = new DrvSim();
        drv.Initialize(new MdkSetting.DriverConfig { Id = "sim", Type = "sim", Enabled = true });

        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.TryGetAxisState(0, out var idle));
        Assert.True(idle.ServoOn);
        Assert.False(idle.Moving);
        Assert.True(idle.InPosition);
        Assert.True(AxisStatusBits.Test(idle.Raw, AxisStatusBits.ServoOn));
        Assert.True(AxisStatusBits.Test(idle.Raw, AxisStatusBits.InPosition));

        Assert.True(drv.TryGetAxisStatus(0, out var raw));
        Assert.Equal(idle.Raw, raw);
    }
}
