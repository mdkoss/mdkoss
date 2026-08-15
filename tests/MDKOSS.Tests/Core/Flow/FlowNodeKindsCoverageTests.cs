using System.Text.Json;
using MDKOSS.Core;
using MDKOSS.Core.Flow;

namespace MDKOSS.Tests.Core.Flow;

/// <summary>Functional coverage for every <see cref="FlowNodeKinds"/> entry.</summary>
public sealed class FlowNodeKindsCoverageTests
{
    private sealed class RecordingFlowHost : IFlowRuntimeHost
    {
        public List<(string DeviceId, string Alias, bool Value)> Writes { get; } = [];
        public List<(string DeviceId, string Action, string? ParamsJson)> Actions { get; } = [];
        public List<string> AxisMoves { get; } = [];
        public List<string> AxisEnables { get; } = [];
        public List<string> AxisJogs { get; } = [];
        public List<string> AxisStops { get; } = [];
        public List<string> PlatformMotions { get; } = [];
        public List<string> PlatformAxisMoves { get; } = [];
        public List<string> PlatformAxisJogs { get; } = [];
        public List<string> PlatformAxisStops { get; } = [];
        public List<string> GpioReads { get; } = [];
        public List<string> Snapshots { get; } = [];
        public List<string> EnsureDrivers { get; } = [];
        public bool FailWrite { get; set; }
        public bool FailAction { get; set; }
        public bool GpioReadValue { get; set; } = true;
        public bool DriverConnected { get; set; } = true;

        public bool TryWriteDigitalOutput(string deviceId, string alias, bool value, out string? error)
        {
            if (FailWrite)
            {
                error = "write_denied";
                return false;
            }

            Writes.Add((deviceId, alias, value));
            error = null;
            return true;
        }

        public DeviceActionResult ExecuteDeviceAction(
            string deviceId,
            string action,
            Dictionary<string, JsonElement>? parameters)
        {
            if (FailAction)
            {
                return DeviceActionResult.Fail("action_denied");
            }

            Actions.Add((deviceId, action, parameters is null ? null : JsonSerializer.Serialize(parameters)));
            return DeviceActionResult.Ok(new { ok = true });
        }

        public bool TryAxisMoveTo(string axisDeviceId, double position, out string? error)
        {
            AxisMoves.Add($"{axisDeviceId}:{position}");
            error = null;
            return true;
        }

        public bool TryAxisSetMotionEnabled(string axisDeviceId, bool enabled, out string? error)
        {
            AxisEnables.Add($"{axisDeviceId}:{enabled}");
            error = null;
            return true;
        }

        public bool TryAxisJog(string axisDeviceId, double direction, double velocity, out string? error)
        {
            AxisJogs.Add($"{axisDeviceId}:{direction}:{velocity}");
            error = null;
            return true;
        }

        public bool TryAxisStopMotion(string axisDeviceId, out string? error)
        {
            AxisStops.Add(axisDeviceId);
            error = null;
            return true;
        }

        public bool TryPlatformSetMotion(string platformDeviceId, bool enabled, out string? error)
        {
            PlatformMotions.Add($"{platformDeviceId}:{enabled}");
            error = null;
            return true;
        }

        public bool TryPlatformAxisMoveTo(
            string platformDeviceId,
            string axisLetter,
            double position,
            out string? error)
        {
            PlatformAxisMoves.Add($"{platformDeviceId}:{axisLetter}:{position}");
            error = null;
            return true;
        }

        public bool TryPlatformAxisJog(
            string platformDeviceId,
            string axisLetter,
            double direction,
            double velocity,
            out string? error)
        {
            PlatformAxisJogs.Add($"{platformDeviceId}:{axisLetter}:{direction}:{velocity}");
            error = null;
            return true;
        }

        public bool TryPlatformAxisStopMotion(
            string platformDeviceId,
            string axisLetter,
            out string? error)
        {
            PlatformAxisStops.Add($"{platformDeviceId}:{axisLetter}");
            error = null;
            return true;
        }

        public bool TryGpioWriteOutput(string gpioDeviceId, string alias, bool value, out string? error) =>
            TryWriteDigitalOutput(gpioDeviceId, alias, value, out error);

        public bool TryGpioReadInput(string gpioDeviceId, string alias, out bool value, out string? error)
        {
            GpioReads.Add($"{gpioDeviceId}:{alias}");
            value = GpioReadValue;
            error = null;
            return true;
        }

        public bool TryGetDeviceSnapshot(
            string deviceId,
            out string? deviceType,
            out string? state,
            out bool driverConnected,
            out string? error)
        {
            Snapshots.Add(deviceId);
            deviceType = "axis";
            state = "running";
            driverConnected = DriverConnected;
            error = null;
            return true;
        }

        public bool TryEnsureDriverConnected(string deviceId, out string? error)
        {
            EnsureDrivers.Add(deviceId);
            if (!DriverConnected)
            {
                error = "driver_not_connected:sim";
                return false;
            }

            error = null;
            return true;
        }
    }

    private static (FlowInterpreter Interp, MVarStore Vars) Run(
        FlowDocument doc,
        string taskName = "n",
        IFlowRuntimeHost? host = null,
        int budget = 256)
    {
        var errors = doc.Validate();
        Assert.True(errors.Count == 0, string.Join("; ", errors));
        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, taskName, vars, host);
        interp.Reset();
        interp.Pump(budget);
        return (interp, vars);
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

    [Fact]
    public void All_known_kinds_are_listed()
    {
        Assert.Equal(29, FlowNodeKinds.All.Length);
        foreach (var kind in FlowNodeKinds.All)
        {
            Assert.True(FlowNodeKinds.IsKnown(kind));
        }
    }

    [Fact]
    public void Start_and_End_complete()
    {
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges = [new FlowEdge { From = "s", To = "e", Port = FlowPorts.Next }],
        };

        var (interp, vars) = Run(doc);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal("completed", vars.Get<string>("task.n.flow.state"));
    }

    [Fact]
    public void DeclareVar_number_bool_string()
    {
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "d1",
                    Kind = FlowNodeKinds.DeclareVar,
                    Props = Props(("name", "a"), ("type", "number"), ("init", "7")),
                },
                new FlowNode
                {
                    Id = "d2",
                    Kind = FlowNodeKinds.DeclareVar,
                    Props = Props(("name", "b"), ("type", "bool"), ("init", "true")),
                },
                new FlowNode
                {
                    Id = "d3",
                    Kind = FlowNodeKinds.DeclareVar,
                    Props = Props(("name", "c"), ("type", "string"), ("init", "\"hi\"")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "d1", Port = FlowPorts.Next },
                new FlowEdge { From = "d1", To = "d2", Port = FlowPorts.Next },
                new FlowEdge { From = "d2", To = "d3", Port = FlowPorts.Next },
                new FlowEdge { From = "d3", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, vars) = Run(doc);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(7.0, vars.Get<double>("task.n.flow.var.a"));
        Assert.True(vars.Get<bool>("task.n.flow.var.b"));
        Assert.Equal("hi", vars.Get<string>("task.n.flow.var.c"));
    }

    [Fact]
    public void SetVar_assigns_expression()
    {
        var doc = new FlowDocument
        {
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "10" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "set",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "x"), ("expr", "x * 2 + 1")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "set", Port = FlowPorts.Next },
                new FlowEdge { From = "set", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, vars) = Run(doc);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(21.0, vars.Get<double>("task.n.flow.var.x"));
    }

    [Fact]
    public void If_true_and_false_branches()
    {
        FlowDocument Build(bool flagInit)
        {
            return new FlowDocument
            {
                Variables = [new FlowVariable { Name = "flag", Type = "bool", Init = flagInit ? "true" : "false" }],
                Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
                Nodes =
                [
                    new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                    new FlowNode
                    {
                        Id = "i",
                        Kind = FlowNodeKinds.If,
                        Props = Props(("condition", "flag")),
                    },
                    new FlowNode
                    {
                        Id = "t",
                        Kind = FlowNodeKinds.SetVar,
                        Props = Props(("name", "path"), ("expr", "\"T\"")),
                    },
                    new FlowNode
                    {
                        Id = "f",
                        Kind = FlowNodeKinds.SetVar,
                        Props = Props(("name", "path"), ("expr", "\"F\"")),
                    },
                    new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
                ],
                Edges =
                [
                    new FlowEdge { From = "s", To = "i", Port = FlowPorts.Next },
                    new FlowEdge { From = "i", To = "t", Port = FlowPorts.True },
                    new FlowEdge { From = "i", To = "f", Port = FlowPorts.False },
                    new FlowEdge { From = "t", To = "e", Port = FlowPorts.Next },
                    new FlowEdge { From = "f", To = "e", Port = FlowPorts.Next },
                ],
            };
        }

        var (_, varsT) = Run(Build(true));
        Assert.Equal("T", varsT.Get<string>("task.n.flow.var.path"));

        var (_, varsF) = Run(Build(false));
        Assert.Equal("F", varsF.Get<string>("task.n.flow.var.path"));
    }

    [Fact]
    public void While_body_then_exit()
    {
        var doc = new FlowDocument
        {
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "w",
                    Kind = FlowNodeKinds.While,
                    Props = Props(("condition", "x < 4")),
                },
                new FlowNode
                {
                    Id = "inc",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "x"), ("expr", "x + 1")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "w", Port = FlowPorts.Next },
                new FlowEdge { From = "w", To = "inc", Port = FlowPorts.Body },
                new FlowEdge { From = "inc", To = "w", Port = FlowPorts.Next },
                new FlowEdge { From = "w", To = "e", Port = FlowPorts.Exit },
            ],
        };

        var (interp, vars) = Run(doc);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(4.0, vars.Get<double>("task.n.flow.var.x"));
    }

    [Fact]
    public void Delay_zero_ms_does_not_wait()
    {
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode { Id = "d", Kind = FlowNodeKinds.Delay, Props = Props(("ms", "0")) },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "d", Port = FlowPorts.Next },
                new FlowEdge { From = "d", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, _) = Run(doc);
        Assert.Equal(FlowRunState.Completed, interp.State);
    }

    [Fact]
    public void Delay_positive_ms_yields_then_completes()
    {
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode { Id = "d", Kind = FlowNodeKinds.Delay, Props = Props(("ms", "40")) },
                new FlowNode
                {
                    Id = "mark",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "done"), ("expr", "true")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "d", Port = FlowPorts.Next },
                new FlowEdge { From = "d", To = "mark", Port = FlowPorts.Next },
                new FlowEdge { From = "mark", To = "e", Port = FlowPorts.Next },
            ],
        };

        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "n", vars);
        interp.Reset();
        interp.Pump(16);
        Assert.Equal(FlowRunState.Waiting, interp.State);
        Assert.False(vars.TryGet<object>("task.n.flow.var.done", out _));

        Thread.Sleep(50);
        interp.Pump(16);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.True(vars.Get<bool>("task.n.flow.var.done"));
    }

    [Fact]
    public void Call_invokes_sub_function_and_returns()
    {
        var doc = new FlowDocument
        {
            Variables = [new FlowVariable { Name = "x", Type = "number", Init = "0" }],
            Functions =
            [
                new FlowFunction { Name = "main", EntryNodeId = "ms" },
                new FlowFunction { Name = "sub", EntryNodeId = "ss" },
            ],
            Nodes =
            [
                new FlowNode { Id = "ms", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "c",
                    Kind = FlowNodeKinds.Call,
                    Props = Props(("function", "sub")),
                },
                new FlowNode
                {
                    Id = "after",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "x"), ("expr", "x + 100")),
                },
                new FlowNode { Id = "me", Kind = FlowNodeKinds.End },
                new FlowNode { Id = "ss", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "set",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "x"), ("expr", "7")),
                },
                new FlowNode { Id = "se", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "ms", To = "c", Port = FlowPorts.Next },
                new FlowEdge { From = "c", To = "after", Port = FlowPorts.Next },
                new FlowEdge { From = "after", To = "me", Port = FlowPorts.Next },
                new FlowEdge { From = "ss", To = "set", Port = FlowPorts.Next },
                new FlowEdge { From = "set", To = "se", Port = FlowPorts.Next },
            ],
        };

        var (interp, vars) = Run(doc);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal(107.0, vars.Get<double>("task.n.flow.var.x"));
    }

    [Fact]
    public void OpWriteIo_calls_host()
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
                    Id = "io",
                    Kind = FlowNodeKinds.OpWriteIo,
                    Props = Props(("deviceId", "gpio1"), ("alias", "Y0"), ("value", "true")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "io", Port = FlowPorts.Next },
                new FlowEdge { From = "io", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, _) = Run(doc, host: host);
        Assert.Equal(FlowRunState.Completed, interp.State);
        var w = Assert.Single(host.Writes);
        Assert.Equal("gpio1", w.DeviceId);
        Assert.Equal("Y0", w.Alias);
        Assert.True(w.Value);
    }

    [Fact]
    public void OpWriteIo_failure_faults()
    {
        var host = new RecordingFlowHost { FailWrite = true };
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "io",
                    Kind = FlowNodeKinds.OpWriteIo,
                    Props = Props(("deviceId", "gpio1"), ("alias", "Y0"), ("value", "false")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "io", Port = FlowPorts.Next },
                new FlowEdge { From = "io", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, vars) = Run(doc, host: host);
        Assert.Equal(FlowRunState.Fault, interp.State);
        Assert.Contains("write_denied", interp.LastError ?? "", StringComparison.Ordinal);
        Assert.Equal("fault", vars.Get<string>("task.n.flow.state"));
    }

    [Fact]
    public void OpDeviceAction_calls_host_with_parameters()
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
                    Id = "act",
                    Kind = FlowNodeKinds.OpDeviceAction,
                    Props = Props(
                        ("deviceId", "cam1"),
                        ("action", "capture"),
                        ("parametersJson", "{\"exposure\":10}")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "act", Port = FlowPorts.Next },
                new FlowEdge { From = "act", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, _) = Run(doc, host: host);
        Assert.Equal(FlowRunState.Completed, interp.State);
        var a = Assert.Single(host.Actions);
        Assert.Equal("cam1", a.DeviceId);
        Assert.Equal("capture", a.Action);
        Assert.Contains("exposure", a.ParamsJson ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void OpDeviceAction_failure_faults()
    {
        var host = new RecordingFlowHost { FailAction = true };
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "act",
                    Kind = FlowNodeKinds.OpDeviceAction,
                    Props = Props(("deviceId", "d"), ("action", "noop")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "act", Port = FlowPorts.Next },
                new FlowEdge { From = "act", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, _) = Run(doc, host: host);
        Assert.Equal(FlowRunState.Fault, interp.State);
        Assert.Contains("action_denied", interp.LastError ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void OpLog_writes_lastLog()
    {
        var doc = new FlowDocument
        {
            Variables = [new FlowVariable { Name = "msg", Type = "string", Init = "\"ok\"" }],
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "log",
                    Kind = FlowNodeKinds.OpLog,
                    Props = Props(("message", "\"hello \" + msg")),
                },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "log", Port = FlowPorts.Next },
                new FlowEdge { From = "log", To = "e", Port = FlowPorts.Next },
            ],
        };

        var (interp, vars) = Run(doc);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Equal("hello ok", vars.Get<string>("task.n.flow.lastLog"));
    }

    [Fact]
    public void Unknown_kind_faults()
    {
        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes =
            [
                new FlowNode { Id = "s", Kind = FlowNodeKinds.Start },
                new FlowNode { Id = "bad", Kind = "notARealKind" },
                new FlowNode { Id = "e", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "s", To = "bad", Port = FlowPorts.Next },
                new FlowEdge { From = "bad", To = "e", Port = FlowPorts.Next },
            ],
        };

        // Skip Validate (unknown kind) — drive interpreter directly.
        Assert.Contains(doc.Validate(), e => e.Contains("unknown kind", StringComparison.OrdinalIgnoreCase)
                                             || e.Contains("notARealKind", StringComparison.OrdinalIgnoreCase)
                                             || e.Contains("kind", StringComparison.OrdinalIgnoreCase));

        var vars = new MVarStore();
        var interp = new FlowInterpreter(doc, "n", vars);
        interp.Reset();
        interp.Pump(32);
        Assert.Equal(FlowRunState.Fault, interp.State);
        Assert.Contains("unknown kind", interp.LastError ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Combined_pipeline_exercises_control_and_ops()
    {
        var host = new RecordingFlowHost();
        var doc = new FlowDocument
        {
            Variables =
            [
                new FlowVariable { Name = "i", Type = "number", Init = "0" },
                new FlowVariable { Name = "sum", Type = "number", Init = "0" },
            ],
            Functions =
            [
                new FlowFunction { Name = "main", EntryNodeId = "ms" },
                new FlowFunction { Name = "bump", EntryNodeId = "bs" },
            ],
            Nodes =
            [
                new FlowNode { Id = "ms", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "decl",
                    Kind = FlowNodeKinds.DeclareVar,
                    Props = Props(("name", "label"), ("type", "string"), ("init", "\"pipe\"")),
                },
                new FlowNode
                {
                    Id = "w",
                    Kind = FlowNodeKinds.While,
                    Props = Props(("condition", "i < 2")),
                },
                new FlowNode
                {
                    Id = "callBump",
                    Kind = FlowNodeKinds.Call,
                    Props = Props(("function", "bump")),
                },
                new FlowNode
                {
                    Id = "branch",
                    Kind = FlowNodeKinds.If,
                    Props = Props(("condition", "i == 2")),
                },
                new FlowNode
                {
                    Id = "io",
                    Kind = FlowNodeKinds.OpWriteIo,
                    Props = Props(("deviceId", "g"), ("alias", "OUT"), ("value", "i == 2")),
                },
                new FlowNode
                {
                    Id = "act",
                    Kind = FlowNodeKinds.OpDeviceAction,
                    Props = Props(("deviceId", "dev"), ("action", "pulse")),
                },
                new FlowNode
                {
                    Id = "log",
                    Kind = FlowNodeKinds.OpLog,
                    Props = Props(("message", "label + \":\" + sum")),
                },
                new FlowNode { Id = "d", Kind = FlowNodeKinds.Delay, Props = Props(("ms", "0")) },
                new FlowNode { Id = "me", Kind = FlowNodeKinds.End },
                // false branch of if (should not run when i==2)
                new FlowNode
                {
                    Id = "skip",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "sum"), ("expr", "-1")),
                },
                // bump()
                new FlowNode { Id = "bs", Kind = FlowNodeKinds.Start },
                new FlowNode
                {
                    Id = "incI",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "i"), ("expr", "i + 1")),
                },
                new FlowNode
                {
                    Id = "incSum",
                    Kind = FlowNodeKinds.SetVar,
                    Props = Props(("name", "sum"), ("expr", "sum + i")),
                },
                new FlowNode { Id = "be", Kind = FlowNodeKinds.End },
            ],
            Edges =
            [
                new FlowEdge { From = "ms", To = "decl", Port = FlowPorts.Next },
                new FlowEdge { From = "decl", To = "w", Port = FlowPorts.Next },
                new FlowEdge { From = "w", To = "callBump", Port = FlowPorts.Body },
                new FlowEdge { From = "callBump", To = "w", Port = FlowPorts.Next },
                new FlowEdge { From = "w", To = "branch", Port = FlowPorts.Exit },
                new FlowEdge { From = "branch", To = "io", Port = FlowPorts.True },
                new FlowEdge { From = "branch", To = "skip", Port = FlowPorts.False },
                new FlowEdge { From = "io", To = "act", Port = FlowPorts.Next },
                new FlowEdge { From = "act", To = "log", Port = FlowPorts.Next },
                new FlowEdge { From = "log", To = "d", Port = FlowPorts.Next },
                new FlowEdge { From = "d", To = "me", Port = FlowPorts.Next },
                new FlowEdge { From = "skip", To = "me", Port = FlowPorts.Next },
                new FlowEdge { From = "bs", To = "incI", Port = FlowPorts.Next },
                new FlowEdge { From = "incI", To = "incSum", Port = FlowPorts.Next },
                new FlowEdge { From = "incSum", To = "be", Port = FlowPorts.Next },
            ],
        };

        var (interp, vars) = Run(doc, host: host);
        Assert.Equal(FlowRunState.Completed, interp.State);
        // bump twice: i=1 sum=1; i=2 sum=3
        Assert.Equal(2.0, vars.Get<double>("task.n.flow.var.i"));
        Assert.Equal(3.0, vars.Get<double>("task.n.flow.var.sum"));
        Assert.Equal("pipe:3", vars.Get<string>("task.n.flow.lastLog"));
        Assert.Single(host.Writes);
        Assert.True(host.Writes[0].Value);
        Assert.Single(host.Actions);
        Assert.Equal("pulse", host.Actions[0].Action);
    }

    [Fact]
    public void MotionTask_function_blocks_all()
    {
        var host = new RecordingFlowHost { GpioReadValue = true, DriverConnected = true };
        var nodes = new List<FlowNode>
        {
            new() { Id = "s", Kind = FlowNodeKinds.Start },
            new()
            {
                Id = "ens",
                Kind = FlowNodeKinds.MotionEnsureDriver,
                Props = Props(("deviceId", "axis-x")),
            },
            new()
            {
                Id = "en",
                Kind = FlowNodeKinds.MotionAxisEnable,
                Props = Props(("deviceId", "axis-x"), ("enabled", "true")),
            },
            new()
            {
                Id = "mv",
                Kind = FlowNodeKinds.MotionAxisMoveTo,
                Props = Props(("deviceId", "axis-x"), ("position", "12.5")),
            },
            new()
            {
                Id = "jog",
                Kind = FlowNodeKinds.MotionAxisJog,
                Props = Props(("deviceId", "axis-x"), ("direction", "1"), ("velocity", "2")),
            },
            new()
            {
                Id = "stp",
                Kind = FlowNodeKinds.MotionAxisStop,
                Props = Props(("deviceId", "axis-x")),
            },
            new()
            {
                Id = "ps",
                Kind = FlowNodeKinds.MotionPlatformStart,
                Props = Props(("deviceId", "plat")),
            },
            new()
            {
                Id = "pm",
                Kind = FlowNodeKinds.MotionPlatformSetMotion,
                Props = Props(("deviceId", "plat"), ("enabled", "true")),
            },
            new()
            {
                Id = "pam",
                Kind = FlowNodeKinds.MotionPlatformAxisMoveTo,
                Props = Props(("deviceId", "plat"), ("axis", "Y"), ("position", "3")),
            },
            new()
            {
                Id = "paj",
                Kind = FlowNodeKinds.MotionPlatformAxisJog,
                Props = Props(("deviceId", "plat"), ("axis", "X"), ("direction", "-1"), ("velocity", "0.5")),
            },
            new()
            {
                Id = "pas",
                Kind = FlowNodeKinds.MotionPlatformAxisStop,
                Props = Props(("deviceId", "plat"), ("axis", "X")),
            },
            new()
            {
                Id = "pst",
                Kind = FlowNodeKinds.MotionPlatformStop,
                Props = Props(("deviceId", "plat")),
            },
            new()
            {
                Id = "gw",
                Kind = FlowNodeKinds.MotionGpioWrite,
                Props = Props(("deviceId", "gpio"), ("alias", "Y0"), ("value", "false")),
            },
            new()
            {
                Id = "gr",
                Kind = FlowNodeKinds.MotionGpioRead,
                Props = Props(("deviceId", "gpio"), ("alias", "X0"), ("name", "di")),
            },
            new()
            {
                Id = "snap",
                Kind = FlowNodeKinds.MotionDeviceSnapshot,
                Props = Props(("deviceId", "axis-x"), ("prefix", "snap")),
            },
            new()
            {
                Id = "sp",
                Kind = FlowNodeKinds.MotionSetParam,
                Props = Props(("key", "target"), ("expr", "42")),
            },
            new()
            {
                Id = "gp",
                Kind = FlowNodeKinds.MotionGetParam,
                Props = Props(("key", "target"), ("name", "p")),
            },
            new()
            {
                Id = "stv",
                Kind = FlowNodeKinds.MotionSetTaskVar,
                Props = Props(("key", "alive"), ("expr", "true")),
            },
            new()
            {
                Id = "sgv",
                Kind = FlowNodeKinds.MotionSetGlobalVar,
                Props = Props(("key", "machine.mode"), ("expr", "\"AUTO\"")),
            },
            new() { Id = "e", Kind = FlowNodeKinds.End },
        };

        var ids = nodes.Select(n => n.Id).ToList();
        var edges = new List<FlowEdge>();
        for (var i = 0; i < ids.Count - 1; i++)
        {
            edges.Add(new FlowEdge { From = ids[i], To = ids[i + 1], Port = FlowPorts.Next });
        }

        var doc = new FlowDocument
        {
            Functions = [new FlowFunction { Name = "main", EntryNodeId = "s" }],
            Nodes = nodes,
            Edges = edges,
        };

        var (interp, vars) = Run(doc, host: host);
        Assert.Equal(FlowRunState.Completed, interp.State);
        Assert.Contains("axis-x", host.EnsureDrivers);
        Assert.Contains("axis-x:True", host.AxisEnables);
        Assert.Contains("axis-x:12.5", host.AxisMoves);
        Assert.Contains("axis-x:1:2", host.AxisJogs);
        Assert.Contains("axis-x", host.AxisStops);
        Assert.Contains("plat:True", host.PlatformMotions);
        Assert.Contains("plat:False", host.PlatformMotions);
        Assert.Contains("plat:Y:3", host.PlatformAxisMoves);
        Assert.Contains("plat:X:-1:0.5", host.PlatformAxisJogs);
        Assert.Contains("plat:X", host.PlatformAxisStops);
        Assert.Contains(host.Writes, w => w is { DeviceId: "gpio", Alias: "Y0", Value: false });
        Assert.Contains("gpio:X0", host.GpioReads);
        Assert.True(vars.Get<bool>("task.n.flow.var.di"));
        Assert.Equal("axis", vars.Get<string>("task.n.flow.var.snap.type"));
        Assert.Equal("running", vars.Get<string>("task.n.flow.var.snap.state"));
        Assert.True(vars.Get<bool>("task.n.flow.var.snap.driverConnected"));
        Assert.Equal(42.0, vars.Get<double>("task.n.flow.var.p"));
        Assert.True(vars.Get<bool>("task.n.alive"));
        Assert.Equal("AUTO", vars.Get<string>("machine.mode"));
    }
}
