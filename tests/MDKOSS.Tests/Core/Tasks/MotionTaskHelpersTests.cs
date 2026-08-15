using System.Net;
using System.Net.Sockets;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;
using MDKOSS.Tasks;

namespace MDKOSS.Tests.Core.Tasks;

/// <summary>
/// Verifies <see cref="MotionTask"/> exposes the full AxisDevice motion surface
/// (move / enable / jog / stop) used by device actions and flow blocks.
/// </summary>
public sealed class MotionTaskHelpersTests
{
    private sealed class ProbeMotionTask : MotionTask
    {
        public ProbeMotionTask(
            string name,
            IDriver driver,
            MVarStore vars,
            IReadOnlyDictionary<string, MDeviceBase> devices)
            : base(name, 50, driver, vars, devices)
        {
        }

        public bool CallAxisMoveTo(string id, double pos) => AxisMoveTo(id, pos);

        public bool CallAxisEnable(string id, bool enabled) => AxisSetMotionEnabled(id, enabled);

        public bool CallAxisJog(string id, double direction, double velocity) =>
            AxisJog(id, direction, velocity);

        public bool CallAxisStop(string id) => AxisStopMotion(id);

        public bool CallPlatformAxisJog(string id, string letter, double direction, double velocity) =>
            PlatformAxisJog(id, letter, direction, velocity);

        public bool CallPlatformAxisStop(string id, string letter) =>
            PlatformAxisStopMotion(id, letter);

        protected override Task TickAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void Axis_and_platform_helpers_cover_jog_and_stop()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-motion-helpers-{Guid.NewGuid():N}.db");
        var setting = new MdkSetting
        {
            ProjectName = "motion-helpers",
            MonitoringPrefix = $"http://127.0.0.1:{FreePort()}/",
            DatabasePath = db,
            Drivers =
            [
                new MdkSetting.DriverConfig { Id = "d1", Type = "sim", Enabled = true },
            ],
            Devices =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "axis-x",
                    Name = "AxisX",
                    Type = "axis",
                    DriverId = "d1",
                    Enabled = true,
                },
                new MdkSetting.DeviceConfig
                {
                    Id = "plat",
                    Name = "Table",
                    Type = "xy",
                    DriverId = "d1",
                    Enabled = true,
                },
            ],
        };

        try
        {
            using var rt = new MdkRuntime(setting);
            rt.Initialize();
            Assert.True(rt.TryGetDevice("axis-x", out var axisDev));
            Assert.True(rt.TryGetDevice("plat", out _));

            var devices = new Dictionary<string, MDeviceBase>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in new[] { "axis-x", "plat" })
            {
                Assert.True(rt.TryGetDevice(id, out var d));
                devices[id] = d!;
            }

            var linked = ((AxisDevice)axisDev!).LinkedDriver;
            var task = new ProbeMotionTask("probe", linked, rt.Vars, devices);

            Assert.True(task.CallAxisEnable("axis-x", true));
            Assert.True(task.CallAxisMoveTo("axis-x", 10));
            Assert.True(task.CallAxisJog("axis-x", 1, 3));
            Assert.Equal(3.0, rt.Vars.Get<double>("device.AxisX.axis-x.jogCommand"));
            Assert.True(task.CallAxisStop("axis-x"));
            Assert.Equal(0.0, rt.Vars.Get<double>("device.AxisX.axis-x.jogCommand"));
            Assert.False(rt.Vars.Get<bool>("device.AxisX.axis-x.motionEnabled"));

            Assert.True(task.CallPlatformAxisJog("plat", "X", -1, 2));
            Assert.Equal(-2.0, rt.Vars.Get<double>("device.Table X.plat.X.jogCommand"));
            Assert.True(task.CallPlatformAxisStop("plat", "X"));
            Assert.Equal(0.0, rt.Vars.Get<double>("device.Table X.plat.X.jogCommand"));
        }
        finally
        {
            try
            {
                File.Delete(db);
            }
            catch
            {
                // ignore cleanup races
            }
        }
    }
}
