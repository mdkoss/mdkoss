using System.Net;
using System.Net.Sockets;
using MDKOSS.Core;
using MDKOSS.Core.Flow;
using MDKOSS.Tasks;

namespace MDKOSS.Tests.Core.Flow;

/// <summary>End-to-end: create flow task → save setting → load → bootstrap → tick run.</summary>
public sealed class FlowTaskLifecycleTests
{
    private static int GetFreeLoopbackPort()
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

    private static FlowDocument BuildCounterFlow()
    {
        return new FlowDocument
        {
            Version = 1,
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "n-start" }],
            Nodes =
            [
                new FlowNode { Id = "n-start", Kind = FlowNodeKinds.Start, X = 40, Y = 80 },
                new FlowNode
                {
                    Id = "n-set",
                    Kind = FlowNodeKinds.SetVar,
                    X = 200,
                    Y = 80,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "x",
                        ["expr"] = "x + 1",
                    },
                },
                new FlowNode
                {
                    Id = "n-log",
                    Kind = FlowNodeKinds.OpLog,
                    X = 360,
                    Y = 80,
                    Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["message"] = "\"x=\" + x",
                    },
                },
                new FlowNode { Id = "n-end", Kind = FlowNodeKinds.End, X = 520, Y = 80 },
            ],
            Edges =
            [
                new FlowEdge { From = "n-start", To = "n-set", Port = FlowPorts.Next },
                new FlowEdge { From = "n-set", To = "n-log", Port = FlowPorts.Next },
                new FlowEdge { From = "n-log", To = "n-end", Port = FlowPorts.Next },
            ],
        };
    }

    [Fact]
    public async Task Create_save_load_run_via_factory()
    {
        var doc = BuildCounterFlow();
        Assert.Empty(doc.Validate());

        var setting = new MdkSetting
        {
            ProjectName = "flow-lifecycle",
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "task-flow-e2e",
                    Type = "flow",
                    IntervalMs = 20,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["loop"] = "false",
                        ["flowJson"] = doc.ToJson(),
                    },
                },
            ],
        };

        var path = Path.Combine(Path.GetTempPath(), $"mdkoss-flow-{Guid.NewGuid():N}.setting.json");
        try
        {
            setting.Save(path);
            Assert.True(File.Exists(path));

            var loaded = MdkSetting.Load(path);
            Assert.Equal("flow-lifecycle", loaded.ProjectName);
            var cfg = Assert.Single(loaded.Tasks);
            Assert.Equal("flow", cfg.Type, ignoreCase: true);
            Assert.True(cfg.Parameters.TryGetValue("flowJson", out var json));
            Assert.False(string.IsNullOrWhiteSpace(json));

            Assert.True(FlowDocument.TryParse(json!, out var parsed, out var err), err);
            Assert.Empty(parsed.Validate());

            var vars = new MVarStore();
            var emptyDrivers = new Dictionary<string, MDKOSS.Core.Drivers.IDriver>(StringComparer.OrdinalIgnoreCase);
            var emptyDevices = new Dictionary<string, MDeviceBase>(StringComparer.OrdinalIgnoreCase);
            var ctx = new TaskBootstrapContext(
                emptyDrivers,
                emptyDevices,
                vars,
                () => new RuntimeSnapshot(
                    loaded.ProjectName,
                    false,
                    new Dictionary<string, DriverSnapshot>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, DeviceSnapshot>(StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)),
                () => []);

            var created = RuntimeTaskFactory.Create("flow", ctx, cfg);
            var task = Assert.IsType<FlowTask>(created);

            await task.ExecuteOnceAsync(CancellationToken.None);

            Assert.Equal(MTaskState.Running, task.State);
            Assert.Equal(FlowRunState.Completed, task.FlowState);
            Assert.Equal(1.0, vars.Get<double>("task.task-flow-e2e.flow.var.x"));
            Assert.Equal("completed", vars.Get<string>("task.task-flow-e2e.flow.state"));
            Assert.Contains("x=1", vars.Get<string>("task.task-flow-e2e.flow.lastLog") ?? "", StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Runtime_bootstrap_registers_and_scheduler_runs_flow()
    {
        var doc = BuildCounterFlow();
        var dbPath = Path.Combine(Path.GetTempPath(), $"mdkoss-flow-db-{Guid.NewGuid():N}.db");
        var setting = new MdkSetting
        {
            ProjectName = "flow-runtime",
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
            DatabasePath = dbPath,
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = "task-flow-rt",
                    Type = "flow",
                    IntervalMs = 30,
                    Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["loop"] = "true",
                        ["flowJson"] = doc.ToJson(),
                    },
                },
            ],
        };

        var path = Path.Combine(Path.GetTempPath(), $"mdkoss-flow-rt-{Guid.NewGuid():N}.setting.json");
        try
        {
            setting.Save(path);
            var loaded = MdkSetting.Load(path);

            using var rt = new MdkRuntime(loaded);
            rt.Initialize();

            var snap = Assert.Single(rt.GetTaskSnapshots());
            Assert.Equal("task-flow-rt", snap.Name);
            Assert.Equal(nameof(FlowTask), snap.Type);

            rt.Start();
            Assert.True(rt.IsRunning);

            var deadline = DateTime.UtcNow.AddSeconds(3);
            double x = 0;
            while (DateTime.UtcNow < deadline)
            {
                x = rt.Vars.Get<double>("task.task-flow-rt.flow.var.x");
                if (x >= 2)
                {
                    break;
                }

                await Task.Delay(40);
            }

            Assert.True(x >= 2, $"expected looped counter >= 2 (locals persist across reset), got {x}");
            Assert.False(string.IsNullOrWhiteSpace(rt.Vars.Get<string>("task.task-flow-rt.flow.lastLog")));

            await rt.StopAsync();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            try
            {
                if (File.Exists(dbPath))
                {
                    File.Delete(dbPath);
                }
            }
            catch
            {
                // ignore lock on Windows
            }
        }
    }

    [Fact]
    public async Task Sample_setting_task_flow_demo_loads_and_runs()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");
        Assert.True(File.Exists(path), $"Missing sample settings at {path}");

        var setting = MdkSetting.Load(path);
        var cfg = Assert.Single(setting.Tasks, t =>
            string.Equals(t.Name, "task-flow-demo", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("flow", cfg.Type, ignoreCase: true);
        Assert.True(cfg.Parameters.ContainsKey("flowJson"));

        // Isolate: only the flow task, unique DB/monitor to avoid colliding with other tests.
        var isolated = new MdkSetting
        {
            ProjectName = "sample-flow-only",
            MonitoringPrefix = $"http://127.0.0.1:{GetFreeLoopbackPort()}/",
            DatabasePath = Path.Combine(Path.GetTempPath(), $"mdkoss-sample-flow-{Guid.NewGuid():N}.db"),
            Tasks =
            [
                new MdkSetting.TaskConfig
                {
                    Name = cfg.Name,
                    Type = cfg.Type,
                    IntervalMs = cfg.IntervalMs,
                    Parameters = new Dictionary<string, string>(cfg.Parameters, StringComparer.OrdinalIgnoreCase)
                    {
                        ["loop"] = "false",
                    },
                },
            ],
        };

        var vars = new MVarStore();
        var task = FlowTask.Create(isolated.Tasks[0], vars);
        await task.ExecuteOnceAsync(CancellationToken.None);

        Assert.Equal(FlowRunState.Completed, task.FlowState);
        Assert.Equal(1.0, vars.Get<double>("task.task-flow-demo.flow.var.x"));
    }
}
