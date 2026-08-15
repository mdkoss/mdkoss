using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Core.Flow;

/// <summary>Serialized flow graph stored in <c>TaskConfig.Parameters["flowJson"]</c>.</summary>
public sealed class FlowDocument
{
    public int Version { get; set; } = 1;

    public List<FlowVariable> Variables { get; set; } = [];

    public List<FlowFunction> Functions { get; set; } = [];

    public List<FlowNode> Nodes { get; set; } = [];

    public List<FlowEdge> Edges { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static FlowDocument CreateEmpty()
    {
        var startId = "n-start";
        var endId = "n-end";
        // Vertical centered defaults (workflow layout)
        const double left = 300;
        return new FlowDocument
        {
            Version = 1,
            Functions = [new FlowFunction { Name = "main", EntryNodeId = startId }],
            Nodes =
            [
                new FlowNode { Id = startId, Kind = FlowNodeKinds.Start, X = left, Y = 40, Order = 0 },
                new FlowNode { Id = endId, Kind = FlowNodeKinds.End, X = left, Y = 152, Order = 1 },
            ],
            Edges =
            [
                new FlowEdge { From = startId, To = endId, Port = FlowPorts.Next },
            ],
        };
    }

    public static FlowDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("flowJson is empty.");
        }

        var doc = JsonSerializer.Deserialize<FlowDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException("flowJson deserialize returned null.");
        return doc;
    }

    public static bool TryParse(string? json, out FlowDocument document, out string? error)
    {
        document = CreateEmpty();
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "flowJson_empty";
            return false;
        }

        try
        {
            document = Parse(json);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Structural validation. Returns empty list when OK.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Version < 1)
        {
            errors.Add("version must be >= 1");
        }

        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                errors.Add("node id is empty");
                continue;
            }

            if (!nodeIds.Add(node.Id.Trim()))
            {
                errors.Add($"duplicate node id: {node.Id}");
            }

            if (string.IsNullOrWhiteSpace(node.Kind) || !FlowNodeKinds.IsKnown(node.Kind))
            {
                errors.Add($"node '{node.Id}' has unknown kind '{node.Kind}'");
            }
        }

        if (Functions.Count == 0)
        {
            errors.Add("at least one function is required (main)");
        }

        foreach (var fn in Functions)
        {
            if (string.IsNullOrWhiteSpace(fn.Name))
            {
                errors.Add("function name is empty");
                continue;
            }

            if (string.IsNullOrWhiteSpace(fn.EntryNodeId) || !nodeIds.Contains(fn.EntryNodeId))
            {
                errors.Add($"function '{fn.Name}' entryNodeId '{fn.EntryNodeId}' not found");
            }
            else
            {
                var entry = Nodes.First(n => string.Equals(n.Id, fn.EntryNodeId, StringComparison.OrdinalIgnoreCase));
                if (!string.Equals(entry.Kind, FlowNodeKinds.Start, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"function '{fn.Name}' entry must be a start node");
                }
            }
        }

        if (!Functions.Any(f => string.Equals(f.Name, "main", StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("function 'main' is required");
        }

        foreach (var edge in Edges)
        {
            if (string.IsNullOrWhiteSpace(edge.From) || !nodeIds.Contains(edge.From))
            {
                errors.Add($"edge from '{edge.From}' not found");
            }

            if (string.IsNullOrWhiteSpace(edge.To) || !nodeIds.Contains(edge.To))
            {
                errors.Add($"edge to '{edge.To}' not found");
            }

            if (string.IsNullOrWhiteSpace(edge.Port))
            {
                errors.Add("edge port is empty");
            }
        }

        foreach (var node in Nodes)
        {
            var kind = (node.Kind ?? "").Trim().ToLowerInvariant();
            var outs = Edges
                .Where(e => string.Equals(e.From, node.Id, StringComparison.OrdinalIgnoreCase))
                .Select(e => (e.Port ?? FlowPorts.Next).Trim().ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            switch (kind)
            {
                case "start":
                case "declarevar":
                case "setvar":
                case "delay":
                case "call":
                case "op.writeio":
                case "op.deviceaction":
                case "op.log":
                case "motion.axismoveto":
                case "motion.axisenable":
                case "motion.axisjog":
                case "motion.axisstop":
                case "motion.platformsetmotion":
                case "motion.platformstart":
                case "motion.platformstop":
                case "motion.platformaxismoveto":
                case "motion.platformaxisjog":
                case "motion.platformaxisstop":
                case "motion.gpiowrite":
                case "motion.gpioread":
                case "motion.devicesnapshot":
                case "motion.ensuredriver":
                case "motion.setparam":
                case "motion.getparam":
                case "motion.settaskvar":
                case "motion.setglobalvar":
                    if (!outs.Contains(FlowPorts.Next))
                    {
                        errors.Add($"node '{node.Id}' ({kind}) missing port '{FlowPorts.Next}'");
                    }

                    break;
                case "if":
                    if (!outs.Contains(FlowPorts.True) || !outs.Contains(FlowPorts.False))
                    {
                        errors.Add($"node '{node.Id}' (if) requires ports true and false");
                    }

                    break;
                case "while":
                    if (!outs.Contains(FlowPorts.Body) || !outs.Contains(FlowPorts.Exit))
                    {
                        errors.Add($"node '{node.Id}' (while) requires ports body and exit");
                    }

                    break;
                case "end":
                    break;
            }
        }

        var varNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in Variables)
        {
            if (string.IsNullOrWhiteSpace(v.Name))
            {
                errors.Add("variable name is empty");
                continue;
            }

            if (!varNames.Add(v.Name.Trim()))
            {
                errors.Add($"duplicate variable: {v.Name}");
            }
        }

        errors.AddRange(FlowComposite.ValidateTree(Nodes));
        return errors;
    }

    /// <summary>
    /// When nodes carry parentId/slot, regenerate <see cref="Edges"/> from the tree
    /// so runtime and editor stay consistent.
    /// </summary>
    public void SyncEdgesFromTree()
    {
        if (!FlowComposite.HasTreeMetadata(Nodes) && Edges.Count > 0)
        {
            // Legacy flat documents keep explicit edges until edited into a tree.
            return;
        }

        FlowComposite.RenumberOrders(Nodes);
        Edges = FlowComposite.BuildEdges(Nodes);
    }
}

public sealed class FlowVariable
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "number"; // bool | number | string
    public string? Init { get; set; }
}

public sealed class FlowFunction
{
    public string Name { get; set; } = "main";
    public string EntryNodeId { get; set; } = string.Empty;
}

public sealed class FlowNode
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = FlowNodeKinds.Start;
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Props { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Composite parent node id; null/empty = root spine.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentId { get; set; }

    /// <summary>Slot under parent: <see cref="FlowSlots.Then"/> / <see cref="FlowSlots.Else"/> / <see cref="FlowSlots.Body"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Slot { get; set; }

    /// <summary>Order among siblings in the same parent+slot.</summary>
    public int Order { get; set; }
}

public sealed class FlowEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Port { get; set; } = FlowPorts.Next;
}

public static class FlowNodeKinds
{
    public const string Start = "start";
    public const string End = "end";
    public const string DeclareVar = "declareVar";
    public const string SetVar = "setVar";
    public const string If = "if";
    public const string While = "while";
    public const string Delay = "delay";
    public const string Call = "call";
    public const string OpWriteIo = "op.writeIo";
    public const string OpDeviceAction = "op.deviceAction";
    public const string OpLog = "op.log";

    // MotionTask function blocks
    public const string MotionAxisMoveTo = "motion.axisMoveTo";
    public const string MotionAxisEnable = "motion.axisEnable";
    public const string MotionAxisJog = "motion.axisJog";
    public const string MotionAxisStop = "motion.axisStop";
    public const string MotionPlatformSetMotion = "motion.platformSetMotion";
    public const string MotionPlatformStart = "motion.platformStart";
    public const string MotionPlatformStop = "motion.platformStop";
    public const string MotionPlatformAxisMoveTo = "motion.platformAxisMoveTo";
    public const string MotionPlatformAxisJog = "motion.platformAxisJog";
    public const string MotionPlatformAxisStop = "motion.platformAxisStop";
    public const string MotionGpioWrite = "motion.gpioWrite";
    public const string MotionGpioRead = "motion.gpioRead";
    public const string MotionDeviceSnapshot = "motion.deviceSnapshot";
    public const string MotionEnsureDriver = "motion.ensureDriver";
    public const string MotionSetParam = "motion.setParam";
    public const string MotionGetParam = "motion.getParam";
    public const string MotionSetTaskVar = "motion.setTaskVar";
    public const string MotionSetGlobalVar = "motion.setGlobalVar";

    public static readonly string[] All =
    [
        Start, End, DeclareVar, SetVar, If, While, Delay, Call, OpWriteIo, OpDeviceAction, OpLog,
        MotionAxisMoveTo, MotionAxisEnable, MotionAxisJog, MotionAxisStop,
        MotionPlatformSetMotion, MotionPlatformStart, MotionPlatformStop,
        MotionPlatformAxisMoveTo, MotionPlatformAxisJog, MotionPlatformAxisStop,
        MotionGpioWrite, MotionGpioRead, MotionDeviceSnapshot, MotionEnsureDriver,
        MotionSetParam, MotionGetParam, MotionSetTaskVar, MotionSetGlobalVar,
    ];

    public static bool IsKnown(string? kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && All.Any(k => string.Equals(k, kind.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class FlowPorts
{
    public const string Next = "next";
    public const string True = "true";
    public const string False = "false";
    public const string Body = "body";
    public const string Exit = "exit";
}
