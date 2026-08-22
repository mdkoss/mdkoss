using MDKOSS.Core.Flow;

namespace MDKOSS.Iec61131;

/// <summary>Compiles a <see cref="FlowDocument"/> into a cyclic IEC step sequencer.</summary>
public static class FlowStepCompiler
{
    public static IecPou Compile(
        FlowDocument document,
        string pouName,
        string sourceName,
        bool cyclic,
        bool loop,
        IecSymbols symbols,
        IReadOnlyList<IecIoPoint> ioPoints,
        Dictionary<string, string> functionPouNames,
        List<IecNote> notes,
        List<IecNodeMap> nodeMaps)
    {
        document.SyncEdgesFromTree();
        var pou = new IecPou
        {
            Name = pouName,
            Kind = IecPouKind.FunctionBlock,
            SourceName = sourceName,
            Cyclic = cyclic,
            Loop = loop,
        };

        string Map(string ident) => symbols.Resolve(ident);

        foreach (var v in document.Variables)
        {
            if (string.IsNullOrWhiteSpace(v.Name))
            {
                continue;
            }

            var type = IecTypeMap.FromFlow(v.Type);
            var name = symbols.Register(v.Name);
            pou.Locals.Add(new IecVariable
            {
                Name = name,
                Type = type,
                Init = string.IsNullOrWhiteSpace(v.Init)
                    ? IecTypeMap.DefaultInit(type)
                    : IecExpr.ToSt(v.Init, Map),
                SourceKey = v.Name,
                Comment = "flow variable",
            });
        }

        var nodeStep = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stepNum = 10;
        foreach (var node in document.Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

            nodeStep[node.Id] = stepNum;
            stepNum += 10;
        }

        var startNode = document.Nodes.FirstOrDefault(n =>
            string.Equals(n.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase));
        pou.StartStep = startNode is not null && nodeStep.TryGetValue(startNode.Id, out var start)
            ? start
            : 10;

        int Target(string fromId, string port, int fallback)
        {
            var edge = document.Edges.FirstOrDefault(e =>
                string.Equals(e.From, fromId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.Port, port, StringComparison.OrdinalIgnoreCase));
            if (edge is not null && nodeStep.TryGetValue(edge.To, out var to))
            {
                return to;
            }

            return fallback;
        }

        foreach (var node in document.Nodes)
        {
            if (!nodeStep.TryGetValue(node.Id, out var number))
            {
                continue;
            }

            var kind = (node.Kind ?? "").Trim();
            var step = CompileNode(
                node,
                number,
                kind,
                loop,
                pou.StartStep,
                Map,
                ioPoints,
                functionPouNames,
                Target,
                pou,
                symbols,
                notes);
            pou.Steps.Add(step);
            nodeMaps.Add(new IecNodeMap
            {
                Pou = pou.Name,
                NodeId = node.Id,
                Kind = kind,
                Step = number,
            });
        }

        pou.Steps.Sort((a, b) => a.Number.CompareTo(b.Number));
        return pou;
    }

    private static IecStep CompileNode(
        FlowNode node,
        int number,
        string kind,
        bool loop,
        int startStep,
        Func<string, string> map,
        IReadOnlyList<IecIoPoint> ioPoints,
        Dictionary<string, string> functionPouNames,
        Func<string, string, int, int> target,
        IecPou pou,
        IecSymbols symbols,
        List<IecNote> notes)
    {
        var next = target(node.Id, FlowPorts.Next, 0);
        var step = new IecStep
        {
            Number = number,
            NodeId = node.Id,
            FlowKind = kind,
            Next = next,
            Comment = $"{kind} {node.Id}",
        };

        var k = kind.ToLowerInvariant();
        switch (k)
        {
            case "start":
                step.Kind = IecStepKind.Goto;
                step.Next = next == 0 ? startStep : next;
                break;
            case "end":
                if (loop)
                {
                    step.Kind = IecStepKind.Goto;
                    step.Next = startStep;
                    step.Comment = "end → loop main";
                }
                else
                {
                    step.Kind = IecStepKind.Complete;
                    step.Comment = "end";
                }

                break;
            case "declarevar":
            case "setvar":
                EnsureLocal(pou, symbols, Prop(node, "name"), IecType.Real, "setVar");
                step.Kind = IecStepKind.Assign;
                step.Target = map(Prop(node, "name"));
                step.Expression = IecExpr.ToSt(Prop(node, k == "declarevar" ? "init" : "expr", "0"), map);
                break;
            case "if":
                step.Kind = IecStepKind.IfGoto;
                step.Expression = IecExpr.ToSt(Prop(node, "condition", "false"), map);
                step.Next = target(node.Id, FlowPorts.True, next);
                step.AltNext = target(node.Id, FlowPorts.False, next);
                break;
            case "while":
                step.Kind = IecStepKind.IfGoto;
                step.Expression = IecExpr.ToSt(Prop(node, "condition", "false"), map);
                step.Next = target(node.Id, FlowPorts.Body, number);
                step.AltNext = target(node.Id, FlowPorts.Exit, next);
                step.Comment = $"while {node.Id}";
                break;
            case "delay":
                step.Kind = IecStepKind.Delay;
                step.DelayMs = ParseDelayMs(Prop(node, "ms", "0"), map);
                step.TimerName = symbols.Register("ton:" + node.Id, IecNames.Timer(node.Id));
                pou.Instances.Add(new IecInstance
                {
                    Name = step.TimerName,
                    TypeName = "TON",
                    Comment = node.Id,
                });
                break;
            case "call":
            {
                var fn = Prop(node, "function", "main");
                if (!functionPouNames.TryGetValue(fn, out var fbType))
                {
                    notes.Add(new IecNote
                    {
                        Severity = "warn",
                        Message = $"call '{fn}' has no exported FB; step {number} will halt",
                    });
                    step.Kind = IecStepKind.Halt;
                    step.Comment = $"call missing {fn}";
                    break;
                }

                step.Kind = IecStepKind.HostCall;
                step.HostType = fbType;
                step.HostInstance = symbols.Register("fb:" + node.Id, IecNames.FbInstance(node.Id));
                pou.Instances.Add(new IecInstance
                {
                    Name = step.HostInstance,
                    TypeName = fbType,
                    Comment = $"call {fn}",
                });
                break;
            }
            case "op.writeio":
            case "motion.gpiowrite":
                step.Kind = IecStepKind.WriteIo;
                step.IoName = ResolveIo(ioPoints, Prop(node, "deviceId"), Prop(node, "alias"), isOutput: true, notes, number);
                step.Expression = IecExpr.ToSt(Prop(node, "value", "true"), map);
                break;
            case "motion.gpioread":
            {
                var dest = Prop(node, "name", "io");
                EnsureLocal(pou, symbols, dest, IecType.Bool, "gpioRead");
                step.Kind = IecStepKind.ReadIo;
                step.Target = map(dest);
                step.IoName = ResolveIo(ioPoints, Prop(node, "deviceId"), Prop(node, "alias"), isOutput: false, notes, number);
                break;
            }
            case "op.log":
                step.Kind = IecStepKind.Log;
                step.Expression = IecExpr.ToSt(Prop(node, "message", ""), map);
                break;
            case "motion.setparam":
            {
                var pName = IecNames.Param(Prop(node, "key"));
                EnsureLocal(pou, symbols, pName, GuessType(Prop(node, "expr")), "param");
                step.Kind = IecStepKind.Assign;
                step.Target = symbols.Resolve(pName);
                step.Expression = IecExpr.ToSt(Prop(node, "expr", "0"), map);
                break;
            }
            case "motion.getparam":
                EnsureLocal(pou, symbols, Prop(node, "name"), IecType.Real, "getParam");
                step.Kind = IecStepKind.Assign;
                step.Target = map(Prop(node, "name"));
                step.Expression = symbols.Resolve(IecNames.Param(Prop(node, "key")));
                break;
            case "motion.settaskvar":
            {
                var suffix = Prop(node, "key");
                var name = IecNames.TaskVar(suffix);
                EnsureLocal(pou, symbols, name, GuessType(Prop(node, "expr")), "taskVar");
                step.Kind = IecStepKind.Assign;
                step.Target = symbols.Resolve(name);
                step.Expression = IecExpr.ToSt(Prop(node, "expr", "0"), map);
                break;
            }
            case "motion.setglobalvar":
                step.Kind = IecStepKind.Assign;
                step.Target = map(Prop(node, "key"));
                step.Expression = IecExpr.ToSt(Prop(node, "expr", "0"), map);
                break;
            case "motion.axismoveto":
                Host(step, pou, symbols, node, HostFunctionBlocks.AxisMoveTo, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Position", IecExpr.ToSt(Prop(node, "position", "0"), map)));
                break;
            case "motion.axisenable":
                Host(step, pou, symbols, node, HostFunctionBlocks.AxisEnable, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Enabled", IecExpr.ToSt(Prop(node, "enabled", "true"), map)));
                break;
            case "motion.axisjog":
                Host(step, pou, symbols, node, HostFunctionBlocks.AxisJog, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Direction", IecExpr.ToSt(Prop(node, "direction", "1"), map)),
                    ("Velocity", IecExpr.ToSt(Prop(node, "velocity", "1"), map)));
                break;
            case "motion.axisstop":
                Host(step, pou, symbols, node, HostFunctionBlocks.AxisStop, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))));
                break;
            case "motion.platformsetmotion":
                Host(step, pou, symbols, node, HostFunctionBlocks.PlatformSetMotion, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Enabled", IecExpr.ToSt(Prop(node, "enabled", "true"), map)));
                break;
            case "motion.platformstart":
                Host(step, pou, symbols, node, HostFunctionBlocks.PlatformSetMotion, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Enabled", "TRUE"));
                break;
            case "motion.platformstop":
                Host(step, pou, symbols, node, HostFunctionBlocks.PlatformSetMotion, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Enabled", "FALSE"));
                break;
            case "motion.platformaxismoveto":
                Host(step, pou, symbols, node, HostFunctionBlocks.PlatformAxisMoveTo, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Axis", Str(Prop(node, "axis", "X"))),
                    ("Position", IecExpr.ToSt(Prop(node, "position", "0"), map)));
                break;
            case "motion.platformaxisjog":
                Host(step, pou, symbols, node, HostFunctionBlocks.PlatformAxisJog, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Axis", Str(Prop(node, "axis", "X"))),
                    ("Direction", IecExpr.ToSt(Prop(node, "direction", "1"), map)),
                    ("Velocity", IecExpr.ToSt(Prop(node, "velocity", "1"), map)));
                break;
            case "motion.platformaxisstop":
                Host(step, pou, symbols, node, HostFunctionBlocks.PlatformAxisStop, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Axis", Str(Prop(node, "axis", "X"))));
                break;
            case "motion.devicesnapshot":
            {
                var prefix = Prop(node, "prefix", "snap");
                EnsureLocal(pou, symbols, prefix + ".type", IecType.String, "snapshot");
                EnsureLocal(pou, symbols, prefix + ".state", IecType.String, "snapshot");
                EnsureLocal(pou, symbols, prefix + ".driverConnected", IecType.Bool, "snapshot");
                Host(step, pou, symbols, node, HostFunctionBlocks.DeviceSnapshot, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))));
                step.HostOutputs.Add(new IecHostArg { Parameter = "DeviceType", Value = map(prefix + ".type") });
                step.HostOutputs.Add(new IecHostArg { Parameter = "State", Value = map(prefix + ".state") });
                step.HostOutputs.Add(new IecHostArg { Parameter = "DriverConnected", Value = map(prefix + ".driverConnected") });
                break;
            }
            case "motion.ensuredriver":
                Host(step, pou, symbols, node, HostFunctionBlocks.EnsureDriver, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))));
                break;
            case "op.deviceaction":
                Host(step, pou, symbols, node, HostFunctionBlocks.DeviceAction, next,
                    ("DeviceId", Str(Prop(node, "deviceId"))),
                    ("Action", Str(Prop(node, "action"))),
                    ("ParametersJson", Str(Prop(node, "parametersJson", "{}"))));
                notes.Add(new IecNote
                {
                    Severity = "info",
                    Message = $"op.deviceAction '{Prop(node, "deviceId")}.{Prop(node, "action")}' exported as host handshake FB",
                });
                break;
            default:
                notes.Add(new IecNote
                {
                    Severity = "warn",
                    Message = $"Unknown flow kind '{kind}' on {node.Id} → halt step {number}",
                });
                step.Kind = IecStepKind.Halt;
                break;
        }

        return step;
    }

    private static void Host(
        IecStep step,
        IecPou pou,
        IecSymbols symbols,
        FlowNode node,
        string typeName,
        int next,
        params (string Parameter, string Value)[] args)
    {
        step.Kind = IecStepKind.HostCall;
        step.HostType = typeName;
        step.HostInstance = symbols.Register("fb:" + node.Id, IecNames.FbInstance(node.Id));
        step.Next = next;
        foreach (var (parameter, value) in args)
        {
            step.HostArgs.Add(new IecHostArg { Parameter = parameter, Value = value });
        }

        pou.Instances.Add(new IecInstance
        {
            Name = step.HostInstance,
            TypeName = typeName,
            Comment = $"{step.FlowKind} {node.Id}",
        });
    }

    private static void EnsureLocal(IecPou pou, IecSymbols symbols, string? source, IecType type, string comment)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var name = symbols.Register(source);
        if (pou.Locals.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        pou.Locals.Add(new IecVariable
        {
            Name = name,
            Type = type,
            Init = IecTypeMap.DefaultInit(type),
            SourceKey = source,
            Comment = comment,
        });
    }

    private static IecType GuessType(string? expr)
    {
        var t = (expr ?? "").Trim();
        if (t.Equals("true", StringComparison.OrdinalIgnoreCase)
            || t.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return IecType.Bool;
        }

        if ((t.StartsWith('"') && t.EndsWith('"')) || (t.StartsWith('\'') && t.EndsWith('\'')))
        {
            return IecType.String;
        }

        return IecType.Real;
    }

    private static string ResolveIo(
        IReadOnlyList<IecIoPoint> ioPoints,
        string deviceId,
        string alias,
        bool isOutput,
        List<IecNote> notes,
        int step)
    {
        var hit = IecIoMapper.Find(ioPoints, deviceId, alias);
        if (hit is not null)
        {
            return hit.Name;
        }

        var fallback = isOutput ? IecNames.IoOutput(alias) : IecNames.IoInput(alias);
        notes.Add(new IecNote
        {
            Severity = "warn",
            Message = $"Step {step}: IO alias '{alias}' on '{deviceId}' not in GPIO map; using {fallback}",
        });
        return fallback;
    }

    private static int ParseDelayMs(string expr, Func<string, string> map)
    {
        if (int.TryParse(expr.Trim(), out var ms))
        {
            return Math.Max(0, ms);
        }

        _ = map;
        if (double.TryParse(expr.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
        {
            return Math.Max(0, (int)d);
        }

        return 0;
    }

    private static string Str(string value) => IecExpr.ToStString(value ?? "");

    private static string Prop(FlowNode node, string key, string fallback = "")
    {
        if (node.Props.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            return v;
        }

        return fallback;
    }
}
