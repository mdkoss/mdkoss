using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Tasks;

namespace MDKOSS.Tests.Core.Flow;

/// <summary>One test per motion.* Flow node (unit + sim runtime).</summary>
public sealed class FlowMotionNodeTests
{
    private sealed class RecordingFlowHost : IFlowRuntimeHost
    {
        public List<string> Log { get; } = [];
        public bool Fail { get; set; }
        public bool GpioReadValue { get; set; } = true;
        public bool DriverOk { get; set; } = true;

        private bool Gate(string tag, out string? error)
        {
            Log.Add(tag);
            if (Fail)
            {
                error = "host_fail";
                return false;
            }

            error = null;
            return true;
        }

        public bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error) =>
            Gate($"write:{deviceId}:{alias}:{value}", out error);

        public DeviceActionResult ExecuteDeviceAction(
            string deviceId,
            string action,
            Dictionary<string, JsonElement>? parameters)
        {
            Log.Add($"action:{deviceId}:{action}");
            return Fail ? DeviceActionResult.Fail("host_fail") : DeviceActionResult.Ok();
        }

        public bool TryAxisMoveTo(string axisDeviceId, double position, out string? error) =>
            Gate($"axisMove:{axisDeviceId}:{position}", out error);

        public bool TryAxisSetMotionEnabled(string axisDeviceId, bool enabled, out string? error) =>
            Gate($"axisEnable:{axisDeviceId}:{enabled}", out error);

        public bool TryAxisJog(string axisDeviceId, double direction, double velocity, out string? error) =>
            Gate($"axisJog:{axisDeviceId}:{direction}:{velocity}", out error);

        public bool TryAxisStopMotion(string axisDeviceId, out string? error) =>
            Gate($"axisStop:{axisDeviceId}", out error);

        public bool TryPlatformSetMotion(string platformDeviceId, bool enabled, out string? error) =>
            Gate($"platSet:{platformDeviceId}:{enabled}", out error);

        public bool TryPlatformAxisMoveTo(
            string platformDeviceId,
            string axisLetter,
            double position,
            out string? error) =>
            Gate($"platAxis:{platformDeviceId}:{axisLetter}:{position}", out error);

        public bool TryPlatformAxisJog(
            string platformDeviceId,
            string axisLetter,
            double direction,
            double velocity,
            out string? error) =>
            Gate($"platAxisJog:{platformDeviceId}:{axisLetter}:{direction}:{velocity}", out error);

        public bool TryPlatformAxisStopMotion(
            string platformDeviceId,
            string axisLetter,
            out string? error) =>
            Gate($"platAxisStop:{platformDeviceId}:{axisLetter}", out error);

        public bool TryGpioWriteOutput(string gpioDeviceId, string alias, bool value, out string? error) =>
            TryWriteDigitalOutput(gpioDeviceId, alias, value, out error);

        public bool TryGpioReadInput(string gpioDeviceId, string alias, out bool value, out string? error)
        {
            value = GpioReadValue;
            return Gate($"gpioRead:{gpioDeviceId}:{alias}", out error);
        }

        public bool TryGetDeviceSnapshot(
            string deviceId,
            out string? deviceType,
            out string? state,
            out bool driverConnected,
            out string? error)
        {
            deviceType = "axis";
            state = "running";
            driverConnected = DriverOk;
            return Gate($"snap:{deviceId}", out error);
        }

        public bool TryEnsureDriverConnected(string deviceId, out string? error)
        {
            Log.Add($"ensure:{deviceId}");
            if (!DriverOk || Fail)
            {
                error = "driver_not_connected";
                return false;
            }

            error = null;
            return true;
        }
    }

    /// <summary>Host backed by live devices from <see cref="MdkRuntime.TryGetDevice"/>.</summary>
    private sealed class RuntimeDeviceFlowHost(MdkRuntime runtime) : IFlowRuntimeHost
    {
        public bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error) =>
            runtime.TryWriteDigitalOutput(deviceId, alias, value, out error);

        public DeviceActionResult ExecuteDeviceAction(
            string deviceId,
            string action,
            Dictionary<string, JsonElement>? parameters) =>
            runtime.ExecuteDeviceAction(deviceId, action, parameters);

        public bool TryAxisMoveTo(string axisDeviceId, double position, out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            return axis.MoveTo(position) || Fail("axis_move_failed", out error);
        }

        public bool TryAxisSetMotionEnabled(string axisDeviceId, bool enabled, out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            return axis.SetMotionEnabled(enabled) || Fail("axis_enable_failed", out error);
        }

        public bool TryAxisJog(string axisDeviceId, double direction, double velocity, out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            return axis.Jog(direction, velocity) || Fail("axis_jog_failed", out error);
        }

        public bool TryAxisStopMotion(string axisDeviceId, out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(axisDeviceId, out var raw) || raw is not AxisDevice axis)
            {
                error = "axis_not_found";
                return false;
            }

            return axis.StopMotion() || Fail("axis_stop_failed", out error);
        }

        public bool TryPlatformSetMotion(string platformDeviceId, bool enabled, out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            return platform.SetMotion(enabled) || Fail("platform_set_motion_failed", out error);
        }

        public bool TryPlatformAxisMoveTo(
            string platformDeviceId,
            string axisLetter,
            double position,
            out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            var entry = platform.Axes.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = "platform_axis_not_found";
                return false;
            }

            return entry.Axis.MoveTo(position) || Fail("platform_axis_move_failed", out error);
        }

        public bool TryPlatformAxisJog(
            string platformDeviceId,
            string axisLetter,
            double direction,
            double velocity,
            out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            var entry = platform.Axes.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = "platform_axis_not_found";
                return false;
            }

            return entry.Axis.Jog(direction, velocity) || Fail("platform_axis_jog_failed", out error);
        }

        public bool TryPlatformAxisStopMotion(
            string platformDeviceId,
            string axisLetter,
            out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(platformDeviceId, out var raw) || raw is not PlatformDevice platform)
            {
                error = "platform_not_found";
                return false;
            }

            var entry = platform.Axes.FirstOrDefault(a =>
                string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                error = "platform_axis_not_found";
                return false;
            }

            return entry.Axis.StopMotion() || Fail("platform_axis_stop_failed", out error);
        }

        public bool TryGpioWriteOutput(string gpioDeviceId, string alias, bool value, out string? error) =>
            runtime.TryWriteDigitalOutput(gpioDeviceId, alias, value, out error);

        public bool TryGpioReadInput(string gpioDeviceId, string alias, out bool value, out string? error)
        {
            value = false;
            error = null;
            if (!runtime.TryGetDevice(gpioDeviceId, out var raw))
            {
                error = "device_not_found";
                return false;
            }

            switch (raw)
            {
                case GpioDevice gpio:
                    value = gpio.ReadInput(alias);
                    return true;
                case VioDevice vio:
                    value = vio.ReadInput(alias);
                    return true;
                default:
                    error = "device_not_gpio_or_vio";
                    return false;
            }
        }

        public bool TryGetDeviceSnapshot(
            string deviceId,
            out string? deviceType,
            out string? state,
            out bool driverConnected,
            out string? error)
        {
            deviceType = null;
            state = null;
            driverConnected = false;
            error = null;
            if (!runtime.TryGetDevice(deviceId, out var device))
            {
                error = "device_not_found";
                return false;
            }

            var snap = device.GetSnapshot();
            deviceType = snap.Type;
            state = snap.State;
            driverConnected = snap.DriverConnected;
            return true;
        }

        public bool TryEnsureDriverConnected(string deviceId, out string? error)
        {
            error = null;
            if (!runtime.TryGetDevice(deviceId, out var device))
            {
                error = "device_not_found";
                return false;
            }

            if (device.LinkedDriver.IsConnected)
            {
                return true;
            }

            error = $"driver_not_connected:{device.LinkedDriver.Name}";
            return false;
        }

        private static bool Fail(string code, out string? error)
        {
            error = code;
            return false;
        }
    }

    private static Dictionary<string, string> Props(params (string K, string V)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs)
        {
            d[k] = v;
        }

        return d;
    }

    private static (FlowInterpreter Interp, MVarStore Vars, RecordingFlowHost Host) RunNode(
        string kind,
        Dictionary<string, string> props,
        RecordingFlowHost? host = null)
    {
        host ??= new RecordingFlowHost();
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode { Id = "m", Kind = kind, Props = props },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "m", Port = FlowPorts.Next },
                new FlowEdge { From = "m", To = "e", Port = FlowPorts.Next },
            ],
        };

        Assert.Empty(doc.Validate());
        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "mtest", vars, host);
        interp.Reset();
        interp.Pump(64);
        return (interp, vars, host);
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

    // ---- unit: each motion kind ----

    [Fact]
    public void Motion_axisMoveTo()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionAxisMoveTo,
            Props(("deviceId", "axis-x"), ("position", "10.5")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("axisMove:axis-x:10.5", host.Log);
    }

    [Fact]
    public void Motion_axisEnable()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionAxisEnable,
            Props(("deviceId", "axis-x"), ("enabled", "false")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("axisEnable:axis-x:False", host.Log);
    }

    [Fact]
    public void Motion_axisJog()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionAxisJog,
            Props(("deviceId", "axis-x"), ("direction", "-1"), ("velocity", "2.5")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("axisJog:axis-x:-1:2.5", host.Log);
    }

    [Fact]
    public void Motion_axisStop()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionAxisStop,
            Props(("deviceId", "axis-x")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("axisStop:axis-x", host.Log);
    }

    [Fact]
    public void Motion_platformSetMotion()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionPlatformSetMotion,
            Props(("deviceId", "plat"), ("enabled", "true")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("platSet:plat:True", host.Log);
    }

    [Fact]
    public void Motion_platformStart()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionPlatformStart,
            Props(("deviceId", "plat")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("platSet:plat:True", host.Log);
    }

    [Fact]
    public void Motion_platformStop()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionPlatformStop,
            Props(("deviceId", "plat")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("platSet:plat:False", host.Log);
    }

    [Fact]
    public void Motion_platformAxisMoveTo()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionPlatformAxisMoveTo,
            Props(("deviceId", "plat"), ("axis", "Y"), ("position", "7")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("platAxis:plat:Y:7", host.Log);
    }

    [Fact]
    public void Motion_platformAxisJog()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionPlatformAxisJog,
            Props(("deviceId", "plat"), ("axis", "X"), ("direction", "1"), ("velocity", "0.5")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("platAxisJog:plat:X:1:0.5", host.Log);
    }

    [Fact]
    public void Motion_platformAxisStop()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionPlatformAxisStop,
            Props(("deviceId", "plat"), ("axis", "Z")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("platAxisStop:plat:Z", host.Log);
    }

    [Fact]
    public void Motion_gpioWrite()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionGpioWrite,
            Props(("deviceId", "gpio"), ("alias", "Y0"), ("value", "true")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("write:gpio:Y0:True", host.Log);
    }

    [Fact]
    public void Motion_gpioRead()
    {
        var host = new RecordingFlowHost { GpioReadValue = false };
        var (interp, vars, _) = RunNode(
            FlowNodeKinds.MotionGpioRead,
            Props(("deviceId", "gpio"), ("alias", "X0"), ("name", "di")),
            host);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("gpioRead:gpio:X0", host.Log);
        Assert.False(vars.Get<bool>("task.mtest.flow.var.di"));
    }

    [Fact]
    public void Motion_deviceSnapshot()
    {
        var (interp, vars, host) = RunNode(
            FlowNodeKinds.MotionDeviceSnapshot,
            Props(("deviceId", "axis-x"), ("prefix", "s")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("snap:axis-x", host.Log);
        Assert.Equal("axis", vars.Get<string>("task.mtest.flow.var.s.type"));
        Assert.Equal("running", vars.Get<string>("task.mtest.flow.var.s.state"));
        Assert.True(vars.Get<bool>("task.mtest.flow.var.s.driverConnected"));
    }

    [Fact]
    public void Motion_ensureDriver()
    {
        var (interp, _, host) = RunNode(
            FlowNodeKinds.MotionEnsureDriver,
            Props(("deviceId", "axis-x")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("ensure:axis-x", host.Log);
    }

    [Fact]
    public void Motion_ensureDriver_faults_when_disconnected()
    {
        var host = new RecordingFlowHost { DriverOk = false };
        var (interp, _, _) = RunNode(
            FlowNodeKinds.MotionEnsureDriver,
            Props(("deviceId", "axis-x")),
            host);
        Assert.Equal(FlowRunState.Fault, interp.State);
        Assert.Contains("driver_not_connected", interp.LastError ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void Motion_setParam_and_getParam()
    {
        var host = new RecordingFlowHost();
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "sp",
                    Kind = FlowNodeKinds.MotionSetParam,
                    Props = Props(("key", "t"), ("expr", "9")),
                },
                new FlowNode
                {
                    Id = "gp",
                    Kind = FlowNodeKinds.MotionGetParam,
                    Props = Props(("key", "t"), ("name", "v")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "sp", Port = FlowPorts.Next },
                new FlowEdge { From = "sp", To = "gp", Port = FlowPorts.Next },
                new FlowEdge { From = "gp", To = "e", Port = FlowPorts.Next },
            ],
        };

        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "mtest", vars, host);
        interp.Reset();
        interp.Pump(64);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(9.0, vars.Get<double>("task.mtest.flow.var.v"));
    }

    [Fact]
    public void Motion_setTaskVar()
    {
        var (interp, vars, _) = RunNode(
            FlowNodeKinds.MotionSetTaskVar,
            Props(("key", "alive"), ("expr", "true")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.True(vars.Get<bool>("task.mtest.alive"));
    }

    [Fact]
    public void Motion_setGlobalVar()
    {
        var (interp, vars, _) = RunNode(
            FlowNodeKinds.MotionSetGlobalVar,
            Props(("key", "machine.mode"), ("expr", "\"AUTO\"")));
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal("AUTO", vars.Get<string>("machine.mode"));
    }

    [Fact]
    public void Motion_host_failure_faults_axisMoveTo()
    {
        var host = new RecordingFlowHost { Fail = true };
        var (interp, _, _) = RunNode(
            FlowNodeKinds.MotionAxisMoveTo,
            Props(("deviceId", "axis-x"), ("position", "1")),
            host);
        Assert.Equal(FlowRunState.Fault, interp.State);
        Assert.Contains("host_fail", interp.LastError ?? "", StringComparison.Ordinal);
    }

    // ---- sim runtime: real devices ----

    [Fact]
    public async Task Sim_runtime_each_motion_block()
    {
        var db = Path.Combine(Path.GetTempPath(), $"mdk-flow-motion-{Guid.NewGuid():N}.db");
        var setting = new MdkSetting
        {
            ProjectName = "flow-motion-sim",
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
                new MdkSetting.DeviceConfig
                {
                    Id = "gpio",
                    Name = "Gpio",
                    Type = "gpio",
                    Enabled = true,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["in.start"] = "d1:X0",
                        ["out.lamp"] = "d1:Y0",
                    },
                },
            ],
        };

        try
        {
            using var rt = new MdkRuntime(setting);
            rt.Initialize();
            var host = new RuntimeDeviceFlowHost(rt);
            var vars = rt.Vars;

            // ensureDriver
            {
                var doc = Linear(
                    FlowNodeKinds.MotionEnsureDriver,
                    Props(("deviceId", "axis-x")));
                Pump(doc, "ens", vars, host);
            }

            // axisEnable + axisMoveTo + axisJog + axisStop
            {
                var doc = new FlowDocument
                {
                    Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
                    Nodes =
                    [
                        new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                        new FlowNode
                        {
                            Id = "en",
                            Kind = FlowNodeKinds.MotionAxisEnable,
                            Props = Props(("deviceId", "axis-x"), ("enabled", "true")),
                        },
                        new FlowNode
                        {
                            Id = "mv",
                            Kind = FlowNodeKinds.MotionAxisMoveTo,
                            Props = Props(("deviceId", "axis-x"), ("position", "123")),
                        },
                        new FlowNode
                        {
                            Id = "jog",
                            Kind = FlowNodeKinds.MotionAxisJog,
                            Props = Props(("deviceId", "axis-x"), ("direction", "1"), ("velocity", "2")),
                        },
                        new FlowNode
                        {
                            Id = "stp",
                            Kind = FlowNodeKinds.MotionAxisStop,
                            Props = Props(("deviceId", "axis-x")),
                        },
                        new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
                    ],
                    Edges =
                    [
                        new FlowEdge { From = "s", To = "en", Port = FlowPorts.Next },
                        new FlowEdge { From = "en", To = "mv", Port = FlowPorts.Next },
                        new FlowEdge { From = "mv", To = "jog", Port = FlowPorts.Next },
                        new FlowEdge { From = "jog", To = "stp", Port = FlowPorts.Next },
                        new FlowEdge { From = "stp", To = "e", Port = FlowPorts.Next },
                    ],
                };
                Pump(doc, "axis", vars, host);
                Assert.Equal(123.0, vars.Get<double>("device.AxisX.axis-x.position"));
                Assert.Equal(0.0, vars.Get<double>("device.AxisX.axis-x.jogCommand"));
                Assert.False(vars.Get<bool>("device.AxisX.axis-x.motionEnabled"));
            }

            // platformStart / platformAxisMoveTo / platformAxisJog / platformAxisStop / platformStop / platformSetMotion
            {
                var doc = new FlowDocument
                {
                    Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
                    Nodes =
                    [
                        new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                        new FlowNode
                        {
                            Id = "ps",
                            Kind = FlowNodeKinds.MotionPlatformStart,
                            Props = Props(("deviceId", "plat")),
                        },
                        new FlowNode
                        {
                            Id = "pam",
                            Kind = FlowNodeKinds.MotionPlatformAxisMoveTo,
                            Props = Props(("deviceId", "plat"), ("axis", "X"), ("position", "45")),
                        },
                        new FlowNode
                        {
                            Id = "paj",
                            Kind = FlowNodeKinds.MotionPlatformAxisJog,
                            Props = Props(("deviceId", "plat"), ("axis", "Y"), ("direction", "-1"), ("velocity", "1.5")),
                        },
                        new FlowNode
                        {
                            Id = "pas",
                            Kind = FlowNodeKinds.MotionPlatformAxisStop,
                            Props = Props(("deviceId", "plat"), ("axis", "Y")),
                        },
                        new FlowNode
                        {
                            Id = "pst",
                            Kind = FlowNodeKinds.MotionPlatformStop,
                            Props = Props(("deviceId", "plat")),
                        },
                        new FlowNode
                        {
                            Id = "pset",
                            Kind = FlowNodeKinds.MotionPlatformSetMotion,
                            Props = Props(("deviceId", "plat"), ("enabled", "true")),
                        },
                        new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
                    ],
                    Edges =
                    [
                        new FlowEdge { From = "s", To = "ps", Port = FlowPorts.Next },
                        new FlowEdge { From = "ps", To = "pam", Port = FlowPorts.Next },
                        new FlowEdge { From = "pam", To = "paj", Port = FlowPorts.Next },
                        new FlowEdge { From = "paj", To = "pas", Port = FlowPorts.Next },
                        new FlowEdge { From = "pas", To = "pst", Port = FlowPorts.Next },
                        new FlowEdge { From = "pst", To = "pset", Port = FlowPorts.Next },
                        new FlowEdge { From = "pset", To = "e", Port = FlowPorts.Next },
                    ],
                };
                Pump(doc, "plat", vars, host);
                Assert.Equal(45.0, vars.Get<double>("device.Table X.plat.X.position"));
                Assert.Equal(0.0, vars.Get<double>("device.Table Y.plat.Y.jogCommand"));
                Assert.True(vars.Get<bool>("device.Table.plat.motionEnabled"));
            }

            // gpioWrite + gpioRead (write then read via vio-like — gpio input may be false until driver sets)
            {
                var doc = new FlowDocument
                {
                    Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
                    Nodes =
                    [
                        new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                        new FlowNode
                        {
                            Id = "gw",
                            Kind = FlowNodeKinds.MotionGpioWrite,
                            Props = Props(("deviceId", "gpio"), ("alias", "lamp"), ("value", "true")),
                        },
                        new FlowNode
                        {
                            Id = "gr",
                            Kind = FlowNodeKinds.MotionGpioRead,
                            Props = Props(("deviceId", "gpio"), ("alias", "start"), ("name", "di")),
                        },
                        new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
                    ],
                    Edges =
                    [
                        new FlowEdge { From = "s", To = "gw", Port = FlowPorts.Next },
                        new FlowEdge { From = "gw", To = "gr", Port = FlowPorts.Next },
                        new FlowEdge { From = "gr", To = "e", Port = FlowPorts.Next },
                    ],
                };
                Pump(doc, "gpio", vars, host);
                Assert.Equal("lamp", vars.Get<string>("device.Gpio.gpio.lastOutputAlias"));
                Assert.True(vars.Get<bool>("device.Gpio.gpio.lastOutputValue"));
                Assert.True(vars.TryGet<object>("task.gpio.flow.var.di", out _));
            }

            // deviceSnapshot
            {
                var doc = Linear(
                    FlowNodeKinds.MotionDeviceSnapshot,
                    Props(("deviceId", "axis-x"), ("prefix", "snap")));
                Pump(doc, "snap", vars, host);
                Assert.False(string.IsNullOrWhiteSpace(vars.Get<string>("task.snap.flow.var.snap.type")));
                Assert.True(vars.Get<bool>("task.snap.flow.var.snap.driverConnected"));
            }

            // setTaskVar / setGlobalVar via FlowTask.Create (same interpreter path)
            {
                var doc = new FlowDocument
                {
                    Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
                    Nodes =
                    [
                        new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                        new FlowNode
                        {
                            Id = "st",
                            Kind = FlowNodeKinds.MotionSetTaskVar,
                            Props = Props(("key", "alive"), ("expr", "true")),
                        },
                        new FlowNode
                        {
                            Id = "sg",
                            Kind = FlowNodeKinds.MotionSetGlobalVar,
                            Props = Props(("key", "machine.mode"), ("expr", "\"AUTO\"")),
                        },
                        new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
                    ],
                    Edges =
                    [
                        new FlowEdge { From = "s", To = "st", Port = FlowPorts.Next },
                        new FlowEdge { From = "st", To = "sg", Port = FlowPorts.Next },
                        new FlowEdge { From = "sg", To = "e", Port = FlowPorts.Next },
                    ],
                };
                var cfg = new MdkSetting.TaskConfig
                {
                    Name = "task-vars",
                    Type = "flow",
                    IntervalMs = 20,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["flowJson"] = doc.ToJson(),
                        ["loop"] = "false",
                    },
                };
                var task = FlowTask.Create(cfg, vars, host);
                await task.ExecuteOnceAsync(CancellationToken.None);
                Assert.Equal(FlowRunState.Completed, task.FlowState);
                Assert.True(vars.Get<bool>("task.task-vars.alive"));
                Assert.Equal("AUTO", vars.Get<string>("machine.mode"));
            }

            await Task.CompletedTask;
        }
        finally
        {
            try
            {
                if (File.Exists(db))
                {
                    File.Delete(db);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    private static FlowDocument Linear(string kind, Dictionary<string, string> props) =>
        new()
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode { Id = "m", Kind = kind, Props = props },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "m", Port = FlowPorts.Next },
                new FlowEdge { From = "m", To = "e", Port = FlowPorts.Next },
            ],
        };

    private static void Pump(FlowDocument doc, string taskName, MVarStore vars, IFlowRuntimeHost host)
    {
        Assert.Empty(doc.Validate());
        var interp = new FlowInterpreter(doc, taskName, vars, host);
        interp.Reset();
        interp.Pump(128);
        Assert.Equal(FlowRunState.Completed, interp.State);
    }
}
