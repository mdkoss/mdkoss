using System.Diagnostics;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core.core.drivers;

public sealed class DrvSimMotionTests
{
    [Fact]
    public void Trap_does_not_jump_until_timer_ticks()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.MoveAxisTrap(0, 10_000, velocity: 1_000, acceleration: 100_000, deceleration: 100_000));
        Assert.True(drv.TryGetAxisPrfPosition(0, out var pos));
        Assert.True(drv.TryGetAxisState(0, out var status));
        Assert.Equal(0, pos, 3);
        Assert.True(status.Moving, "should be moving before first ticks settle");
    }

    [Fact]
    public void Trap_reaches_target_and_clears_moving()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.MoveAxisTrap(0, 200, velocity: 20_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(0, out var pos);
            drv.TryGetAxisState(0, out var status);
            return Math.Abs(pos - 200) < 0.5 && !status.Moving && status.InPosition;
        }));
        Assert.True(drv.TryGetAxisEncPosition(0, out var enc));
        Assert.Equal(200, enc, 1);
    }

    [Fact]
    public void Jog_advances_position_on_timer()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(1));
        Assert.True(drv.MoveAxisJog(1, velocity: 5_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(1, out var pos);
            return pos > 20;
        }));
        Assert.True(drv.Stop(1 << 1, option: 1));
        Assert.True(drv.TryGetAxisState(1, out var status));
        Assert.False(status.Moving);
    }

    [Fact]
    public void Home_moves_to_zero_and_sets_homed()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(2));
        Assert.True(drv.SetAxisPosition(2, 150));
        Assert.True(drv.MoveAxisHome(2, homeMode: 0, velocity: 20_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(2, out var pos);
            drv.TryGetAxisState(2, out var status);
            return Math.Abs(pos) < 0.5 && status.Home && !status.Moving;
        }));
    }

    [Fact]
    public void Trap_position_increases_each_sample_until_target()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.MoveAxisTrap(0, 800, velocity: 4_000, acceleration: 200_000, deceleration: 200_000));

        var samples = SamplePositions(drv, 0, count: 8, intervalMs: 20);
        Assert.True(samples.Count >= 3, "timer should publish several positions");
        for (var i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i] >= samples[i - 1] - 1e-6, $"position should not go backwards: {samples[i - 1]} -> {samples[i]}");
        }

        Assert.True(samples[^1] > samples[0], "position must advance over time");
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisState(0, out var s);
            return !s.Moving && Math.Abs(s.PrfPosition - 800) < 1;
        }));
        Assert.True(drv.TryGetAxisState(0, out var done));
        Assert.Equal(800, done.PrfPosition, 1);
        Assert.Equal(done.PrfPosition, done.EncPosition, 3);
        Assert.True(done.InPosition);
        Assert.Equal(0, done.Velocity, 2);
    }

    [Fact]
    public void Trap_negative_target_decreases_position()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.SetAxisPosition(0, 100));
        Assert.True(drv.MoveAxisTrap(0, -200, velocity: 20_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisState(0, out var s);
            return !s.Moving && Math.Abs(s.PrfPosition + 200) < 1;
        }));
        Assert.True(drv.TryGetAxisPrfPosition(0, out var pos));
        Assert.Equal(-200, pos, 1);
    }

    [Fact]
    public void Trap_prf_and_enc_stay_in_sync_while_moving()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.MoveAxisTrap(0, 500, velocity: 8_000, acceleration: 400_000, deceleration: 400_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(0, out var prf);
            return prf > 10;
        }));

        for (var i = 0; i < 5; i++)
        {
            Assert.True(drv.TryGetAxisPrfPosition(0, out var prf));
            Assert.True(drv.TryGetAxisEncPosition(0, out var enc));
            Assert.Equal(prf, enc, 6);
            Thread.Sleep(15);
        }
    }

    [Fact]
    public void Trap_velocity_is_nonzero_while_moving()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.MoveAxisTrap(0, 2_000, velocity: 5_000, acceleration: 500_000, deceleration: 500_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisVelocity(0, out var vel);
            drv.TryGetAxisState(0, out var s);
            return s.Moving && Math.Abs(vel) > 1;
        }));
    }

    [Fact]
    public void Disabled_axis_rejects_motion_commands()
    {
        using var drv = Create();
        Assert.False(drv.MoveAxisTrap(0, 100, 1000, 1000, 1000));
        Assert.False(drv.MoveAxisJog(0, 1000, 1000, 1000));
        Assert.False(drv.MoveAxisHome(0, 0, 1000, 1000, 1000));
        Assert.True(drv.TryGetAxisPrfPosition(0, out var pos));
        Assert.Equal(0, pos, 6);
    }

    [Fact]
    public void Address_axis_reads_follow_timer_updates()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(3));
        Assert.True(drv.MoveAxisTrap(3, 300, velocity: 15_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryRead("axis.3", out var raw);
            return raw is IConvertible c && c.ToDouble(null) > 20;
        }));

        Assert.True(drv.TryRead("axis.3", out var prf));
        Assert.True(drv.TryRead("axis.3.enc", out var enc));
        Assert.True(drv.TryRead("axis.3.vel", out var vel));
        Assert.True(drv.TryRead("axis.3.status", out var status));
        Assert.Equal(Convert.ToDouble(prf), Convert.ToDouble(enc), 3);
        Assert.True(Convert.ToDouble(vel) > 0 || Convert.ToInt32(status) != 0);
    }

    [Fact]
    public void Two_axes_move_independently()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.EnableAxis(1));
        Assert.True(drv.MoveAxisTrap(0, 400, velocity: 20_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(drv.MoveAxisTrap(1, -300, velocity: 20_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisState(0, out var a);
            drv.TryGetAxisState(1, out var b);
            return !a.Moving && !b.Moving
                && Math.Abs(a.PrfPosition - 400) < 1
                && Math.Abs(b.PrfPosition + 300) < 1;
        }));
    }

    [Fact]
    public void Jog_negative_decreases_position_then_stop_freezes_it()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.SetAxisPosition(0, 50));
        Assert.True(drv.MoveAxisJog(0, velocity: -8_000, acceleration: 1_000_000, deceleration: 1_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(0, out var pos);
            return pos < 20;
        }));
        Assert.True(drv.Stop(1 << 0, option: 1));
        Assert.True(drv.TryGetAxisPrfPosition(0, out var stopped));
        Thread.Sleep(40);
        Assert.True(drv.TryGetAxisPrfPosition(0, out var later));
        Assert.Equal(stopped, later, 2);
        Assert.True(drv.TryGetAxisState(0, out var s));
        Assert.False(s.Moving);
    }

    private static List<double> SamplePositions(IDriver drv, short axis, int count, int intervalMs)
    {
        var list = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            if (drv.TryGetAxisPrfPosition(axis, out var pos))
            {
                list.Add(pos);
            }

            Thread.Sleep(intervalMs);
        }

        return list;
    }

    private static DrvSim Create()
    {
        var drv = new DrvSim();
        drv.Initialize(new MdkSetting.DriverConfig { Id = "sim", Type = "sim", Enabled = true });
        return drv;
    }

    private static bool WaitUntil(Func<bool> pred, int timeoutMs = 2000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (pred())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return pred();
    }
}
