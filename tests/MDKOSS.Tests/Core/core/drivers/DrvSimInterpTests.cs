using System.Diagnostics;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core.core.drivers;

public sealed class DrvSimInterpTests
{
    [Fact]
    public void Line_stays_on_path_and_arrives_together()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.EnableAxis(1));
        Assert.True(drv.MoveLine([0, 1], [300, 400], velocity: 10_000, acceleration: 1_000_000, deceleration: 1_000_000));

        var onLine = false;
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(0, out var x);
            drv.TryGetAxisPrfPosition(1, out var y);
            if (x > 20)
            {
                onLine = Math.Abs((y / x) - (400.0 / 300.0)) < 0.05;
            }

            drv.TryGetAxisState(0, out var a);
            drv.TryGetAxisState(1, out var b);
            return !a.Moving && !b.Moving
                && Math.Abs(a.PrfPosition - 300) < 1
                && Math.Abs(b.PrfPosition - 400) < 1;
        }));
        Assert.True(onLine, "axes should stay on the 3:4 line while moving");
        Assert.True(drv.TryGetInterpState(out var moving, out var progress));
        Assert.False(moving);
        Assert.InRange(progress, 0, 1);
    }

    [Fact]
    public void Arc_stays_near_radius()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.EnableAxis(1));
        Assert.True(drv.SetAxisPosition(0, 100));
        Assert.True(drv.SetAxisPosition(1, 0));
        Assert.True(drv.MoveArc(
            [0, 1],
            [0, 100],
            [0, 0],
            clockwise: false,
            velocity: 8_000,
            acceleration: 800_000,
            deceleration: 800_000));

        var radiusOk = false;
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(0, out var x);
            drv.TryGetAxisPrfPosition(1, out var y);
            var r = Math.Sqrt((x * x) + (y * y));
            if (y > 10 && x < 95)
            {
                radiusOk = Math.Abs(r - 100) < 2;
            }

            drv.TryGetAxisState(0, out var a);
            drv.TryGetAxisState(1, out var b);
            return !a.Moving && !b.Moving
                && Math.Abs(a.PrfPosition) < 1
                && Math.Abs(b.PrfPosition - 100) < 1;
        }));
        Assert.True(radiusOk, "interpolated points should stay on the circle");
    }

    [Fact]
    public void Disabled_axis_rejects_interpolation()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.False(drv.MoveLine([0, 1], [10, 10], 1000, 1000, 1000));
        Assert.False(drv.MoveArc([0, 1], [0, 10], [0, 0], false, 1000, 1000, 1000));
    }

    [Fact]
    public void Stop_freezes_all_interp_axes()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.EnableAxis(1));
        Assert.True(drv.MoveLine([0, 1], [5_000, 5_000], velocity: 8_000, acceleration: 400_000, deceleration: 400_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisPrfPosition(0, out var x);
            return x > 30;
        }));

        Assert.True(drv.Stop(1 << 0, option: 1));
        Assert.True(drv.TryGetAxisPrfPosition(0, out var x0));
        Assert.True(drv.TryGetAxisPrfPosition(1, out var y0));
        Thread.Sleep(40);
        Assert.True(drv.TryGetAxisPrfPosition(0, out var x1));
        Assert.True(drv.TryGetAxisPrfPosition(1, out var y1));
        Assert.Equal(x0, x1, 2);
        Assert.Equal(y0, y1, 2);
        Assert.True(drv.TryGetAxisState(0, out var a));
        Assert.True(drv.TryGetAxisState(1, out var b));
        Assert.False(a.Moving);
        Assert.False(b.Moving);
        Assert.True(drv.TryGetInterpState(out var moving, out _));
        Assert.False(moving);
    }

    [Fact]
    public void Single_axis_move_cancels_interpolation()
    {
        using var drv = Create();
        Assert.True(drv.EnableAxis(0));
        Assert.True(drv.EnableAxis(1));
        Assert.True(drv.MoveLine([0, 1], [8_000, 8_000], velocity: 4_000, acceleration: 200_000, deceleration: 200_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetInterpState(out var moving, out _);
            return moving;
        }));
        Assert.True(drv.MoveAxisTrap(0, 50, velocity: 50_000, acceleration: 2_000_000, deceleration: 2_000_000));
        Assert.True(WaitUntil(() =>
        {
            drv.TryGetAxisState(0, out var a);
            drv.TryGetAxisState(1, out var b);
            return !a.Moving && !b.Moving && Math.Abs(a.PrfPosition - 50) < 1;
        }));
        Assert.True(drv.TryGetInterpState(out var interpMoving, out _));
        Assert.False(interpMoving);
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
