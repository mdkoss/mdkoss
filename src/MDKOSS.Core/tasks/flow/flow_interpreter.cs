using System.Globalization;
using System.Text.Json;

namespace MDKOSS.Core.Flow;

public enum FlowRunState
{
    Idle,
    Running,
    Waiting,
    Completed,
    Fault,
}

/// <summary>Tick-driven flow interpreter with call/while stack and delay yield.</summary>
public sealed class FlowInterpreter
{
    private readonly FlowDocument _doc;
    private readonly string _taskName;
    private readonly MVarStore _vars;
    private readonly IFlowRuntimeHost _host;
    private readonly Dictionary<string, object?> _locals = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, object?> _params = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FlowNode> _nodes;
    private readonly List<(string NodeId, string ResumePort)> _stack = [];
    private readonly HashSet<string> _whileActive = new(StringComparer.OrdinalIgnoreCase);

    private string? _pc;
    private DateTime? _waitUntilUtc;
    private FlowRunState _state = FlowRunState.Idle;
    private string? _lastError;

    public FlowInterpreter(FlowDocument document, string taskName, MVarStore vars, IFlowRuntimeHost? host = null)
    {
        _doc = document ?? throw new ArgumentNullException(nameof(document));
        _taskName = string.IsNullOrWhiteSpace(taskName) ? "flow" : taskName.Trim();
        _vars = vars ?? throw new ArgumentNullException(nameof(vars));
        _host = host ?? NullFlowRuntimeHost.Instance;
        _nodes = document.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
    }

    public FlowRunState State => _state;
    public string? ProgramCounter => _pc;
    public string? LastError => _lastError;

    /// <param name="reinitializeVariables">
    /// When true (first start), clears locals and applies document <c>variables[].init</c>.
    /// When false (loop restart), keeps locals so counters persist across main re-entry.
    /// </param>
    public void Reset(bool reinitializeVariables = true)
    {
        _stack.Clear();
        _whileActive.Clear();
        _waitUntilUtc = null;
        _lastError = null;
        if (reinitializeVariables)
        {
            _locals.Clear();
            _params.Clear();
            InitVariables();
        }

        var main = _doc.Functions.FirstOrDefault(f =>
            string.Equals(f.Name, "main", StringComparison.OrdinalIgnoreCase));
        if (main is null || !_nodes.TryGetValue(main.EntryNodeId, out _))
        {
            Fault("main_entry_missing");
            return;
        }

        _pc = main.EntryNodeId;
        _state = FlowRunState.Running;
        PublishStatus();
    }

    /// <summary>Aborts the current run and returns to idle (does not re-init locals).</summary>
    public void Halt()
    {
        _waitUntilUtc = null;
        _stack.Clear();
        _whileActive.Clear();
        _pc = null;
        _state = FlowRunState.Idle;
        PublishStatus();
    }

    /// <summary>Execute up to <paramref name="maxSteps"/> nodes, stopping on delay/end/fault.</summary>
    public void Pump(int maxSteps = 256)
    {
        if (_state is FlowRunState.Completed or FlowRunState.Fault or FlowRunState.Idle)
        {
            return;
        }

        if (_state == FlowRunState.Waiting)
        {
            if (_waitUntilUtc is null || DateTime.UtcNow < _waitUntilUtc.Value)
            {
                PublishStatus();
                return;
            }

            _waitUntilUtc = null;
            _state = FlowRunState.Running;
        }

        var steps = 0;
        while (_state == FlowRunState.Running && steps < maxSteps)
        {
            steps++;
            if (_pc is null || !_nodes.TryGetValue(_pc, out var node))
            {
                Fault("invalid_pc");
                break;
            }

            try
            {
                Step(node);
            }
            catch (Exception ex)
            {
                Fault(ex.Message);
                break;
            }
        }

        if (_state == FlowRunState.Running && steps >= maxSteps)
        {
            Fault("step_budget_exceeded");
        }

        PublishStatus();
    }

    private void Step(FlowNode node)
    {
        var kind = (node.Kind ?? "").Trim().ToLowerInvariant();
        switch (kind)
        {
            case "start":
                Advance(node.Id, FlowPorts.Next);
                break;
            case "end":
                if (_stack.Count > 0)
                {
                    var frame = _stack[^1];
                    _stack.RemoveAt(_stack.Count - 1);
                    _pc = frame.NodeId;
                    // resume after call: follow next from call site
                    if (_nodes.TryGetValue(frame.NodeId, out var callNode)
                        && string.Equals(callNode.Kind, FlowNodeKinds.Call, StringComparison.OrdinalIgnoreCase))
                    {
                        Advance(frame.NodeId, FlowPorts.Next);
                    }
                    else
                    {
                        // while body returned to while node — re-evaluate
                        _pc = frame.NodeId;
                    }
                }
                else
                {
                    _state = FlowRunState.Completed;
                    _pc = node.Id;
                }

                break;
            case "declarevar":
            {
                var name = Prop(node, "name");
                var type = Prop(node, "type", "number");
                var initExpr = Prop(node, "init", "0");
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("declareVar missing name");
                }

                var value = EvalTyped(initExpr, type);
                SetLocal(name, value);
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "setvar":
            {
                var name = Prop(node, "name");
                var expr = Prop(node, "expr", "0");
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("setVar missing name");
                }

                SetLocal(name, FlowExpr.Eval(expr, Resolve));
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "if":
            {
                var cond = Prop(node, "condition", "false");
                var ok = FlowExpr.EvalBool(cond, Resolve);
                Advance(node.Id, ok ? FlowPorts.True : FlowPorts.False);
                break;
            }
            case "while":
            {
                var cond = Prop(node, "condition", "false");
                var ok = FlowExpr.EvalBool(cond, Resolve);
                if (ok)
                {
                    _whileActive.Add(node.Id);
                    Advance(node.Id, FlowPorts.Body);
                }
                else
                {
                    _whileActive.Remove(node.Id);
                    Advance(node.Id, FlowPorts.Exit);
                }

                break;
            }
            case "delay":
            {
                var msExpr = Prop(node, "ms", "0");
                var ms = (int)Math.Max(0, FlowExpr.EvalNumber(msExpr, Resolve));
                var next = FindTarget(node.Id, FlowPorts.Next);
                _pc = next;
                if (ms <= 0)
                {
                    break;
                }

                _waitUntilUtc = DateTime.UtcNow.AddMilliseconds(ms);
                _state = FlowRunState.Waiting;
                break;
            }
            case "call":
            {
                var fnName = Prop(node, "function", "main");
                var fn = _doc.Functions.FirstOrDefault(f =>
                    string.Equals(f.Name, fnName, StringComparison.OrdinalIgnoreCase));
                if (fn is null)
                {
                    throw new InvalidOperationException($"function not found: {fnName}");
                }

                _stack.Add((node.Id, FlowPorts.Next));
                _pc = fn.EntryNodeId;
                break;
            }
            case "op.writeio":
            {
                var deviceId = Prop(node, "deviceId");
                var alias = Prop(node, "alias");
                var valueExpr = Prop(node, "value", "true");
                var value = FlowExpr.EvalBool(valueExpr, Resolve);
                if (!_host.TryWriteDigitalOutput(deviceId, alias, value, out var err))
                {
                    throw new InvalidOperationException(err ?? "write_io_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "op.deviceaction":
            {
                var deviceId = Prop(node, "deviceId");
                var action = Prop(node, "action");
                Dictionary<string, JsonElement>? parameters = null;
                var paramsJson = Prop(node, "parametersJson", "");
                if (!string.IsNullOrWhiteSpace(paramsJson))
                {
                    parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(paramsJson);
                }

                var result = _host.ExecuteDeviceAction(deviceId, action, parameters);
                if (!result.Success)
                {
                    throw new InvalidOperationException(result.Error ?? "device_action_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "op.log":
            {
                var msgExpr = Prop(node, "message", "");
                var msg = FlowExpr.ToStringValue(FlowExpr.Eval(msgExpr, Resolve));
                AppLog.Info($"[flow:{_taskName}] {msg}");
                _vars.Set($"task.{_taskName}.flow.lastLog", msg);
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.axismoveto":
            {
                var deviceId = Prop(node, "deviceId");
                var pos = FlowExpr.EvalNumber(Prop(node, "position", "0"), Resolve);
                if (!_host.TryAxisMoveTo(deviceId, pos, out var err))
                {
                    throw new InvalidOperationException(err ?? "axis_move_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.axisenable":
            {
                var deviceId = Prop(node, "deviceId");
                var enabled = FlowExpr.EvalBool(Prop(node, "enabled", "true"), Resolve);
                if (!_host.TryAxisSetMotionEnabled(deviceId, enabled, out var err))
                {
                    throw new InvalidOperationException(err ?? "axis_enable_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.axisjog":
            {
                var deviceId = Prop(node, "deviceId");
                var direction = FlowExpr.EvalNumber(Prop(node, "direction", "1"), Resolve);
                var velocity = FlowExpr.EvalNumber(Prop(node, "velocity", "1"), Resolve);
                if (!_host.TryAxisJog(deviceId, direction, velocity, out var err))
                {
                    throw new InvalidOperationException(err ?? "axis_jog_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.axisstop":
            {
                var deviceId = Prop(node, "deviceId");
                if (!_host.TryAxisStopMotion(deviceId, out var err))
                {
                    throw new InvalidOperationException(err ?? "axis_stop_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.platformsetmotion":
            {
                var deviceId = Prop(node, "deviceId");
                var enabled = FlowExpr.EvalBool(Prop(node, "enabled", "true"), Resolve);
                if (!_host.TryPlatformSetMotion(deviceId, enabled, out var err))
                {
                    throw new InvalidOperationException(err ?? "platform_set_motion_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.platformstart":
            {
                var deviceId = Prop(node, "deviceId");
                if (!_host.TryPlatformSetMotion(deviceId, true, out var err))
                {
                    throw new InvalidOperationException(err ?? "platform_start_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.platformstop":
            {
                var deviceId = Prop(node, "deviceId");
                if (!_host.TryPlatformSetMotion(deviceId, false, out var err))
                {
                    throw new InvalidOperationException(err ?? "platform_stop_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.platformaxismoveto":
            {
                var deviceId = Prop(node, "deviceId");
                var axis = Prop(node, "axis", "X");
                var pos = FlowExpr.EvalNumber(Prop(node, "position", "0"), Resolve);
                if (!_host.TryPlatformAxisMoveTo(deviceId, axis, pos, out var err))
                {
                    throw new InvalidOperationException(err ?? "platform_axis_move_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.platformaxisjog":
            {
                var deviceId = Prop(node, "deviceId");
                var axis = Prop(node, "axis", "X");
                var direction = FlowExpr.EvalNumber(Prop(node, "direction", "1"), Resolve);
                var velocity = FlowExpr.EvalNumber(Prop(node, "velocity", "1"), Resolve);
                if (!_host.TryPlatformAxisJog(deviceId, axis, direction, velocity, out var err))
                {
                    throw new InvalidOperationException(err ?? "platform_axis_jog_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.platformaxisstop":
            {
                var deviceId = Prop(node, "deviceId");
                var axis = Prop(node, "axis", "X");
                if (!_host.TryPlatformAxisStopMotion(deviceId, axis, out var err))
                {
                    throw new InvalidOperationException(err ?? "platform_axis_stop_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.gpiowrite":
            {
                var deviceId = Prop(node, "deviceId");
                var alias = Prop(node, "alias");
                var value = FlowExpr.EvalBool(Prop(node, "value", "true"), Resolve);
                if (!_host.TryGpioWriteOutput(deviceId, alias, value, out var err))
                {
                    throw new InvalidOperationException(err ?? "gpio_write_failed");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.gpioread":
            {
                var deviceId = Prop(node, "deviceId");
                var alias = Prop(node, "alias");
                var name = Prop(node, "name", "io");
                if (!_host.TryGpioReadInput(deviceId, alias, out var value, out var err))
                {
                    throw new InvalidOperationException(err ?? "gpio_read_failed");
                }

                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("gpioRead missing name");
                }

                SetLocal(name.Trim(), value);
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.devicesnapshot":
            {
                var deviceId = Prop(node, "deviceId");
                if (!_host.TryGetDeviceSnapshot(deviceId, out var type, out var state, out var connected, out var err))
                {
                    throw new InvalidOperationException(err ?? "snapshot_failed");
                }

                var prefix = Prop(node, "prefix", "snap");
                SetLocal($"{prefix}.type", type ?? "");
                SetLocal($"{prefix}.state", state ?? "");
                SetLocal($"{prefix}.driverConnected", connected);
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.ensuredriver":
            {
                var deviceId = Prop(node, "deviceId");
                if (!_host.TryEnsureDriverConnected(deviceId, out var err))
                {
                    throw new InvalidOperationException(err ?? "driver_not_connected");
                }

                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.setparam":
            {
                var key = Prop(node, "key");
                var expr = Prop(node, "expr", "0");
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("setParam missing key");
                }

                _params[key.Trim()] = FlowExpr.Eval(expr, Resolve);
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.getparam":
            {
                var key = Prop(node, "key");
                var name = Prop(node, "name");
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("getParam requires key and name");
                }

                _params.TryGetValue(key.Trim(), out var raw);
                SetLocal(name.Trim(), raw);
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.settaskvar":
            {
                var suffix = Prop(node, "key");
                var expr = Prop(node, "expr", "0");
                if (string.IsNullOrWhiteSpace(suffix))
                {
                    throw new InvalidOperationException("setTaskVar missing key");
                }

                _vars.Set($"task.{_taskName}.{suffix.Trim()}", FlowExpr.Eval(expr, Resolve));
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            case "motion.setglobalvar":
            {
                var key = Prop(node, "key");
                var expr = Prop(node, "expr", "0");
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new InvalidOperationException("setGlobalVar missing key");
                }

                _vars.Set(key.Trim(), FlowExpr.Eval(expr, Resolve));
                Advance(node.Id, FlowPorts.Next);
                break;
            }
            default:
                throw new InvalidOperationException($"unknown kind: {node.Kind}");
        }
    }

    private void Advance(string fromId, string port)
    {
        var to = FindTarget(fromId, port);
        if (to is null)
        {
            throw new InvalidOperationException($"no edge from '{fromId}' port '{port}'");
        }

        _pc = to;
    }

    private string? FindTarget(string fromId, string port)
    {
        var edge = _doc.Edges.FirstOrDefault(e =>
            string.Equals(e.From, fromId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(e.Port, port, StringComparison.OrdinalIgnoreCase));
        return edge?.To;
    }

    private void InitVariables()
    {
        foreach (var v in _doc.Variables)
        {
            if (string.IsNullOrWhiteSpace(v.Name))
            {
                continue;
            }

            var value = EvalTyped(v.Init ?? DefaultInit(v.Type), v.Type);
            SetLocal(v.Name.Trim(), value);
        }
    }

    private static string DefaultInit(string? type) => (type ?? "number").Trim().ToLowerInvariant() switch
    {
        "bool" => "false",
        "string" => "\"\"",
        _ => "0",
    };

    private object? EvalTyped(string? expr, string? type)
    {
        var raw = FlowExpr.Eval(expr, Resolve);
        return (type ?? "number").Trim().ToLowerInvariant() switch
        {
            "bool" => FlowExpr.ToBool(raw),
            "string" => FlowExpr.ToStringValue(raw),
            _ => FlowExpr.ToNumber(raw),
        };
    }

    private void SetLocal(string name, object? value)
    {
        _locals[name] = value;
        // also mirror to MVarStore for monitoring
        _vars.Set($"task.{_taskName}.flow.var.{name}", value);
    }

    private object? Resolve(string name)
    {
        if (_locals.TryGetValue(name, out var local))
        {
            return local;
        }

        // allow reading global vars
        if (_vars.TryGet<object>(name, out var g))
        {
            return g;
        }

        return null;
    }

    private static string Prop(FlowNode node, string key, string fallback = "")
    {
        if (node.Props.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            return v;
        }

        return fallback;
    }

    private void Fault(string error)
    {
        _state = FlowRunState.Fault;
        _lastError = error;
        AppLog.Error($"Flow task '{_taskName}' fault: {error}");
        PublishStatus();
    }

    private void PublishStatus()
    {
        _vars.Set($"task.{_taskName}.flow.state", _state.ToString().ToLowerInvariant());
        _vars.Set($"task.{_taskName}.flow.pc", _pc ?? "");
        _vars.Set($"task.{_taskName}.flow.lastError", _lastError ?? "");
    }
}
