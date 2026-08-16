using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tests.Core;

public sealed class GpioAndSnapshotPerfTests
{
    [Fact]
    public void Gpio_snapshot_reads_many_bits_on_same_port()
    {
        using var drv = new DrvSim();
        drv.Initialize(new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true });
        Assert.True(drv.Write("di.gpi", 0b101));

        var vars = new MVarStore();
        var gpio = new GpioDevice(
            "g1",
            "GPIO",
            new Dictionary<string, IDriver>(StringComparer.OrdinalIgnoreCase) { ["d1"] = drv },
            vars);
        for (short i = 0; i < 32; i++)
        {
            gpio.RegisterInput($"in{i}", "d1", $"di.gpi.bit.{i}");
        }

        gpio.Initialize();
        gpio.Start();
        var snap = gpio.GetSnapshot();
        Assert.Equal(32, snap.GpioIoPoints!.Count);
        Assert.Equal("true", snap.GpioIoPoints.First(p => p.Alias == "in0").Value);
        Assert.Equal("false", snap.GpioIoPoints.First(p => p.Alias == "in1").Value);
        Assert.Equal("true", snap.GpioIoPoints.First(p => p.Alias == "in2").Value);
    }

    [Fact]
    public void Sim_bit_write_does_not_leave_neighbor_bits_stale_in_port_cache()
    {
        using var drv = new DrvSim();
        drv.Initialize(new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true });
        Assert.True(drv.Write("do.gpo.bit.0", true));
        Assert.True(drv.TryRead("do.gpo.bit.0", out var b0));
        Assert.True(Assert.IsType<bool>(b0));
        Assert.True(drv.Write("do.gpo.bit.1", true));
        Assert.True(drv.Write("do.gpo.bit.0", false));
        Assert.True(drv.TryRead("do.gpo.bit.1", out var b1));
        Assert.True(Assert.IsType<bool>(b1));
        Assert.True(drv.TryRead("do.gpo.bit.0", out var b0b));
        Assert.False(Assert.IsType<bool>(b0b));
    }

    [Fact]
    public void TryGetAxisStates_fills_platform_axes()
    {
        using var drv = new DrvSim();
        drv.Initialize(new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true });
        Assert.True(drv.EnableAxis(1));
        Assert.True(drv.EnableAxis(2));
        IDriver drvIface = drv;
        var axes = new short[] { 1, 2, 3 };
        var statuses = new AxisStatus[3];
        Assert.True(drvIface.TryGetAxisStates(axes, statuses));
        Assert.True(statuses[0].ServoOn);
        Assert.True(statuses[1].ServoOn);
        Assert.False(statuses[2].ServoOn);
    }

    [Fact]
    public void Runtime_parallel_GetSnapshot_does_not_throw()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mdk-snap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var setting = new MdkSetting
        {
            ProjectName = "snap-coalesce",
            DatabasePath = Path.Combine(dir, "mdk.db"),
            MonitoringPrefix = "http://127.0.0.1:0/",
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "g1",
                    Name = "GPIO",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = Enumerable.Range(0, 24).ToDictionary(
                        i => $"in.b{i}",
                        i => $"d1|di.gpi.bit.{i}",
                        StringComparer.OrdinalIgnoreCase),
                },
            ],
            Platforms =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "p1",
                    Name = "XYZ",
                    Type = "xyz",
                    DriverId = "d1",
                    Enabled = true,
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        try
        {
            var snaps = new RuntimeSnapshot[8];
            Parallel.For(0, snaps.Length, i => snaps[i] = rt.GetSnapshot());
            Assert.All(snaps, s =>
            {
                Assert.Equal("snap-coalesce", s.ProjectName);
                Assert.True(s.Devices.ContainsKey("g1"));
                Assert.True(s.Devices.ContainsKey("p1"));
                Assert.Equal(3, s.Devices["p1"].PlatformAxes!.Count);
            });
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // temp dir
            }
        }
    }
}
