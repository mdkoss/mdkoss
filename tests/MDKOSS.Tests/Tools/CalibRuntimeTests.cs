using System.Net;
using System.Net.Sockets;
using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Tasks;
using MDKOSS.Tools.Calib.Calib;

namespace MDKOSS.Tests.Tools;

public sealed class CalibRuntimeTests
{
    public CalibRuntimeTests()
    {
        TestPluginBootstrap.EnsureRegistered();
        CalibExtensionBootstrap.Register();
    }

    [Fact]
    public void Sample_flow_file_validates()
    {
        var path = CalibTestFiles.FindRepoFile(Path.Combine("src", "MDKOSS.Tools.Calib", "configs", "flows", "platform-xy.flow.json"));
        Assert.True(File.Exists(path), path);
        Assert.True(FlowDocument.TryParse(File.ReadAllText(path), out var doc, out var error), error);
        Assert.Empty(doc.Validate());
    }

    [Fact]
    public void Sample_setting_lists_four_calib_items()
    {
        var path = CalibTestFiles.FindRepoFile(Path.Combine("src", "MDKOSS.Tools.Calib", "configs", "sample.setting.json"));
        Assert.True(File.Exists(path), path);
        var setting = MdkSetting.Load(path);
        var items = CalibCatalog.List(setting);
        Assert.Equal(4, items.Count);
        Assert.Contains(items, t => t.Type == "calib.axisoffset");
        Assert.Contains(items, t => t.Type == "calib.ninepoint");
        Assert.Contains(items, t => t.Type == "calib.platformoffset");
        Assert.Contains(items, t => CalibCatalog.IsFlowKind(t.Type));
    }

    [Fact]
    public async Task Runtime_registers_motion_and_on_demand_flow()
    {
        var flow = new FlowDocument
        {
            Version = 1,
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "n-start" }],
            Nodes =
            [
                new FlowNode { Id = "n-start", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "n-ok",
                    Kind = FlowNodeKinds.MotionSetTaskVar,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["key"] = "calib.ok",
                        ["expr"] = "true",
                    },
                },
                new FlowNode { Id = "n-end", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "n-start", To = "n-ok", Port = FlowPorts.Next },
                new FlowEdge { From = "n-ok", To = "n-end", Port = FlowPorts.Next },
            ],
        };
        Assert.Empty(flow.Validate());

        var dbPath = Path.Combine(Path.GetTempPath(), $"mdkoss-calib-{Guid.NewGuid():N}.db");
        var setting = new MdkSetting
        {
            ProjectName = "calib-rt",
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
            DatabasePath = dbPath,
            Drivers = [new MdkSetting.DriverConfig { Id = "sim1", Type = "sim", Enabled = true }],
            Axes =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "axis-x",
                    Name = "X",
                    Type = "linear",
                    DriverId = "sim1",
                    Enabled = true,
                    Parameters = { ["axis"] = "0" },
                },
            ],
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "calib-axis-offset",
                    Type = "calib.axisoffset",
                    DriverId = "sim1",
                    IntervalMs = 30,
                    Parameters = { ["calib"] = "true", ["axisDeviceId"] = "axis-x", ["expectedPos"] = "5", ["settleTicks"] = "1" },
                },
                new MdkSetting.TaskConfig
                {
                    Name = "calib-flow",
                    Type = "flow",
                    IntervalMs = 20,
                    Parameters =
                    {
                        ["calib"] = "true",
                        ["loop"] = "false",
                        ["autoStart"] = "false",
                        ["flowJson"] = flow.ToJson(),
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        Assert.True(rt.TryGetTask("calib-axis-offset", out var motion));
        Assert.IsType<AxisOffsetCalibTask>(motion);
        Assert.True(rt.TryGetTask("calib-flow", out var raw));
        var flowTask = Assert.IsType<FlowTask>(raw);
        Assert.Equal(FlowRunState.Idle, flowTask.FlowState);

        rt.Start();
        try
        {
            flowTask.Reset();
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline && flowTask.FlowState is not FlowRunState.Completed and not FlowRunState.Fault)
            {
                await Task.Delay(30);
            }

            Assert.Equal(FlowRunState.Completed, flowTask.FlowState);
            Assert.True(rt.Vars.Get<bool>("task.calib-flow.calib.ok"));

            rt.Vars.Set("task.calib-axis-offset.command", "start");
            deadline = DateTime.UtcNow.AddSeconds(4);
            string? phase = null;
            while (DateTime.UtcNow < deadline)
            {
                phase = rt.Vars.Get<string>("task.calib-axis-offset.phase");
                if (string.Equals(phase, "Done", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(phase, "Fault", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(40);
            }

            Assert.Equal("Done", phase);
            Assert.True(rt.Vars.Get<bool>("task.calib-axis-offset.calib.ok"));

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["expectedPos"] = "5",
            };
            var results = CalibStore.CollectResults(rt.Vars.Snapshot(), "calib-axis-offset");
            Assert.True(CalibStore.TrySaveParams(rt.DataStore, setting.ProjectName, "calib-axis-offset", parameters, out var persistError), persistError);
            Assert.True(CalibStore.TrySaveResult(
                rt.DataStore,
                setting.ProjectName,
                "calib-axis-offset",
                parameters,
                results,
                ok: true,
                "标定完成",
                out persistError), persistError);
            Assert.True(rt.DataStore.TryGetCalibParams(setting.ProjectName, "calib-axis-offset", out var savedParams));
            Assert.Equal("5", savedParams!.Params["expectedPos"]);
            Assert.True(rt.DataStore.TryGetLatestCalibResult(setting.ProjectName, "calib-axis-offset", out var savedResult));
            Assert.True(savedResult!.Ok);
            Assert.True(savedResult.Results.ContainsKey("ok"));
        }
        finally
        {
            await rt.StopAsync();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // ignore lock
            }
        }
    }

    [Fact]
    public async Task Runtime_registers_platform_offset()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"mdkoss-calib-plat-{Guid.NewGuid():N}.db");
        var setting = new MdkSetting
        {
            ProjectName = "calib-plat",
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
            DatabasePath = dbPath,
            Drivers = [new MdkSetting.DriverConfig { Id = "sim1", Type = "sim", Enabled = true }],
            Axes =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "axis-x",
                    Name = "X",
                    Type = "linear",
                    DriverId = "sim1",
                    Enabled = true,
                    Parameters = { ["axis"] = "0" },
                },
                new MdkSetting.DeviceConfig
                {
                    Id = "axis-y",
                    Name = "Y",
                    Type = "linear",
                    DriverId = "sim1",
                    Enabled = true,
                    Parameters = { ["axis"] = "1" },
                },
            ],
            Platforms =
            [
                new MdkSetting.DeviceConfig
                {
                    Id = "platform-xy",
                    Name = "XY",
                    Type = "xy",
                    Enabled = true,
                    Parameters = { ["axis.X"] = "axis-x", ["axis.Y"] = "axis-y" },
                },
            ],
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "calib-platform-offset",
                    Type = "calib.platformoffset",
                    DriverId = "sim1",
                    IntervalMs = 30,
                    Parameters =
                    {
                        ["calib"] = "true",
                        ["platformDeviceId"] = "platform-xy",
                        ["axisLetter"] = "X",
                        ["expectedPos"] = "4",
                        ["settleTicks"] = "1",
                    },
                },
            ],
        };

        using var rt = new MdkRuntime(setting);
        rt.Initialize();
        Assert.True(rt.TryGetTask("calib-platform-offset", out var raw));
        Assert.IsType<PlatformOffsetCalibTask>(raw);
        rt.Start();
        try
        {
            rt.Vars.Set("task.calib-platform-offset.command", "start");
            var deadline = DateTime.UtcNow.AddSeconds(4);
            string? phase = null;
            while (DateTime.UtcNow < deadline)
            {
                phase = rt.Vars.Get<string>("task.calib-platform-offset.phase");
                if (string.Equals(phase, "Done", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(phase, "Fault", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                await Task.Delay(40);
            }

            Assert.Equal("Done", phase);
            Assert.True(rt.Vars.Get<bool>("task.calib-platform-offset.calib.ok"));
        }
        finally
        {
            await rt.StopAsync();
            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // ignore lock
            }
        }
    }

    private static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
