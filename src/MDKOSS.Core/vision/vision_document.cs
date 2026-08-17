using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Core.Vision;

/// <summary>Serialized vision algorithm (pipeline) stored in <see cref="MdkSetting.VisionConfig.Pipeline"/>.</summary>
public sealed class VisionDocument
{
    /// <summary>1 = linear implicit image slot; 2 = explicit dataflow ports/edges.</summary>
    public int Version { get; set; } = VisionVersions.Dataflow;

    /// <summary>
    /// Algorithm platform id (<c>opencv</c> default; <c>halcon</c> or custom via
    /// <see cref="VisionAlgorithmRegistry"/>).
    /// </summary>
    public string Algorithm { get; set; } = VisionAlgorithmRegistry.DefaultId;

    public List<VisionNode> Nodes { get; set; } = [];

    public List<VisionEdge> Edges { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static VisionDocument CreateEmpty()
    {
        const string startId = "n-start";
        const string endId = "n-end";
        const double left = 300;
        var doc = new VisionDocument
        {
            Version = VisionVersions.Dataflow,
            Algorithm = VisionAlgorithmRegistry.DefaultId,
            Nodes =
            [
                new VisionNode { Id = startId, Kind = VisionNodeKinds.Start, X = left, Y = 40, Order = 0 },
                new VisionNode { Id = endId, Kind = VisionNodeKinds.End, X = left, Y = 152, Order = 1 },
            ],
        };
        doc.RebuildLinearEdges();
        return doc;
    }

    /// <summary>
    /// Basic industrial inspect pipeline: load → gray → blur → threshold → findContours → outputPose.
    /// Suitable for camera-capture → detection-result demos (synthetic bright blob or real part).
    /// </summary>
    public static VisionDocument CreateBasicInspectPipeline()
    {
        const double left = 300;
        var nodes = new List<VisionNode>
        {
            new() { Id = "n-start", Kind = VisionNodeKinds.Start, X = left, Y = 40, Order = 0 },
            new()
            {
                Id = "n-load",
                Kind = VisionNodeKinds.LoadImage,
                X = left,
                Y = 120,
                Order = 1,
                Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["path"] = "",
                },
            },
            new() { Id = "n-gray", Kind = VisionNodeKinds.ToGray, X = left, Y = 200, Order = 2 },
            new()
            {
                Id = "n-blur",
                Kind = VisionNodeKinds.Blur,
                X = left,
                Y = 280,
                Order = 3,
                Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["kind"] = "gaussian",
                    ["ksize"] = "5",
                },
            },
            new()
            {
                Id = "n-th",
                Kind = VisionNodeKinds.Threshold,
                X = left,
                Y = 360,
                Order = 4,
                Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["mode"] = "binary",
                    ["thresh"] = "80",
                    ["maxVal"] = "255",
                },
            },
            new()
            {
                Id = "n-blob",
                Kind = VisionNodeKinds.FindContours,
                X = left,
                Y = 440,
                Order = 5,
                Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["thresh"] = "80",
                    ["minArea"] = "200",
                },
            },
            new()
            {
                Id = "n-out",
                Kind = VisionNodeKinds.OutputPose,
                X = left,
                Y = 520,
                Order = 6,
                Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["prefix"] = "vision",
                    ["requireOk"] = "false",
                },
            },
            new() { Id = "n-end", Kind = VisionNodeKinds.End, X = left, Y = 600, Order = 7 },
        };

        var doc = new VisionDocument
        {
            Version = VisionVersions.Dataflow,
            Algorithm = VisionAlgorithmRegistry.DefaultId,
            Nodes = nodes,
        };
        doc.RebuildLinearEdges();
        return doc;
    }

    public static VisionDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("pipelineJson is empty.");
        }

        var doc = JsonSerializer.Deserialize<VisionDocument>(json, JsonOptions)
                  ?? throw new InvalidOperationException("pipelineJson deserialize returned null.");
        doc.EnsureDataflow();
        return doc;
    }

    public static bool TryParse(string? json, out VisionDocument document, out string? error)
    {
        document = CreateEmpty();
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "pipelineJson_empty";
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

    /// <summary>
    /// Upgrade Version 1 (implicit current-image) graphs to Version 2 explicit image/pose data edges.
    /// Idempotent when already migrated.
    /// </summary>
    public void EnsureDataflow()
    {
        NormalizeOrders();
        if (Version >= VisionVersions.Dataflow && HasDataEdges())
        {
            return;
        }

        MigrateLinearToDataflow();
    }

    /// <summary>Rebuild control (next) + linear image/pose data edges from <see cref="VisionNode.Order"/>.</summary>
    public void RebuildLinearEdges()
    {
        NormalizeOrders();
        Edges.Clear();
        var ordered = OrderedNodes();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            Edges.Add(new VisionEdge
            {
                From = ordered[i].Id,
                To = ordered[i + 1].Id,
                Port = VisionPorts.Next,
            });
        }

        WireLinearDataEdges(ordered);
        Version = VisionVersions.Dataflow;
    }

    private void MigrateLinearToDataflow()
    {
        var ordered = OrderedNodes();
        // Preserve existing control edges when present; otherwise rebuild spine.
        if (Edges.Count == 0 || !Edges.Any(e => e.IsControl))
        {
            Edges.RemoveAll(e => e.IsControl);
            for (var i = 0; i < ordered.Count - 1; i++)
            {
                Edges.Add(new VisionEdge
                {
                    From = ordered[i].Id,
                    To = ordered[i + 1].Id,
                    Port = VisionPorts.Next,
                });
            }
        }

        Edges.RemoveAll(e => e.IsData);
        WireLinearDataEdges(ordered);
        Version = VisionVersions.Dataflow;
    }

    private void WireLinearDataEdges(IReadOnlyList<VisionNode> ordered)
    {
        string? lastImage = null;
        string? lastPose = null;
        foreach (var node in ordered)
        {
            var kind = (node.Kind ?? "").Trim();
            if (string.Equals(kind, VisionNodeKinds.Start, StringComparison.OrdinalIgnoreCase))
            {
                lastImage = node.Id;
                continue;
            }

            if (string.Equals(kind, VisionNodeKinds.End, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (VisionPortCatalog.AcceptsImage(kind) && !string.IsNullOrWhiteSpace(lastImage))
            {
                Edges.Add(VisionEdge.Data(lastImage!, VisionPorts.Image, node.Id, VisionPorts.Image));
            }

            if (VisionPortCatalog.AcceptsPose(kind) && !string.IsNullOrWhiteSpace(lastPose))
            {
                Edges.Add(VisionEdge.Data(lastPose!, VisionPorts.Pose, node.Id, VisionPorts.Pose));
            }

            if (VisionPortCatalog.ProducesImage(kind))
            {
                lastImage = node.Id;
            }

            if (VisionPortCatalog.ProducesPose(kind))
            {
                lastPose = node.Id;
            }
        }
    }

    public bool HasDataEdges() => Edges.Any(e => e.IsData);

    private void NormalizeOrders()
    {
        var ordered = OrderedNodes();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }
    }

    public List<VisionNode> OrderedNodes() =>
        Nodes
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Topological order from control + data edges; falls back to <see cref="OrderedNodes"/> on cycles.</summary>
    public List<VisionNode> ExecutionOrder()
    {
        EnsureDataflow();
        if (Nodes.Count == 0)
        {
            return [];
        }

        var byId = new Dictionary<string, VisionNode>(StringComparer.OrdinalIgnoreCase);
        var indeg = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                continue;
            }

            var id = node.Id.Trim();
            byId[id] = node;
            indeg[id] = 0;
            adj[id] = [];
        }

        foreach (var edge in Edges)
        {
            var from = (edge.From ?? "").Trim();
            var to = (edge.To ?? "").Trim();
            if (!byId.ContainsKey(from) || !byId.ContainsKey(to) || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            adj[from].Add(to);
            indeg[to]++;
        }

        var ready = OrderedNodes()
            .Where(n => !string.IsNullOrWhiteSpace(n.Id) && indeg.TryGetValue(n.Id.Trim(), out var d) && d == 0)
            .ToList();
        var result = new List<VisionNode>(Nodes.Count);
        while (ready.Count > 0)
        {
            var next = ready[0];
            ready.RemoveAt(0);
            result.Add(next);
            var fromId = next.Id.Trim();
            foreach (var to in adj[fromId])
            {
                indeg[to]--;
                if (indeg[to] == 0)
                {
                    ready.Add(byId[to]);
                    ready.Sort((a, b) =>
                    {
                        var c = a.Order.CompareTo(b.Order);
                        return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
                    });
                }
            }
        }

        return result.Count == byId.Count ? result : OrderedNodes();
    }

    public VisionEdge? FindIncomingData(string nodeId, string port)
    {
        foreach (var edge in Edges)
        {
            if (!edge.IsData)
            {
                continue;
            }

            if (string.Equals(edge.To, nodeId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(edge.EffectiveToPort, port, StringComparison.OrdinalIgnoreCase))
            {
                return edge;
            }
        }

        return null;
    }

    /// <summary>Structural validation. Returns empty list when OK.</summary>
    public IReadOnlyList<string> Validate()
    {
        EnsureDataflow();
        var errors = new List<string>();
        if (Version < 1)
        {
            errors.Add("version must be >= 1");
        }

        var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startCount = 0;
        var endCount = 0;
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

            if (string.IsNullOrWhiteSpace(node.Kind) || !VisionNodeKinds.IsKnown(node.Kind))
            {
                errors.Add($"node '{node.Id}' has unknown kind '{node.Kind}'");
            }

            if (string.Equals(node.Kind, VisionNodeKinds.Start, StringComparison.OrdinalIgnoreCase))
            {
                startCount++;
            }

            if (string.Equals(node.Kind, VisionNodeKinds.End, StringComparison.OrdinalIgnoreCase))
            {
                endCount++;
            }
        }

        if (startCount != 1)
        {
            errors.Add("exactly one start node is required");
        }

        if (endCount != 1)
        {
            errors.Add("exactly one end node is required");
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
        }

        return errors;
    }
}

public static class VisionVersions
{
    public const int Linear = 1;
    public const int Dataflow = 2;
}

public sealed class VisionNode
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = VisionNodeKinds.Start;
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Props { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Execution / layout order (also used as fallback when data edges are incomplete).</summary>
    public int Order { get; set; }
}

public sealed class VisionEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;

    /// <summary>Legacy control port (<see cref="VisionPorts.Next"/>) or destination data port name.</summary>
    public string Port { get; set; } = VisionPorts.Next;

    /// <summary>Source data port (image/pose). Null on pure control edges.</summary>
    public string? FromPort { get; set; }

    /// <summary>Destination data port. When set (with <see cref="FromPort"/>), marks a data edge.</summary>
    public string? ToPort { get; set; }

    [JsonIgnore]
    public bool IsControl =>
        string.IsNullOrWhiteSpace(FromPort)
        && string.IsNullOrWhiteSpace(ToPort)
        && string.Equals(Port, VisionPorts.Next, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsData => !IsControl;

    [JsonIgnore]
    public string EffectiveFromPort =>
        !string.IsNullOrWhiteSpace(FromPort)
            ? FromPort.Trim()
            : VisionPorts.Image;

    [JsonIgnore]
    public string EffectiveToPort =>
        !string.IsNullOrWhiteSpace(ToPort)
            ? ToPort.Trim()
            : (!string.IsNullOrWhiteSpace(Port) && !string.Equals(Port, VisionPorts.Next, StringComparison.OrdinalIgnoreCase)
                ? Port.Trim()
                : VisionPorts.Image);

    public static VisionEdge Data(string from, string fromPort, string to, string toPort) => new()
    {
        From = from,
        FromPort = fromPort,
        To = to,
        ToPort = toPort,
        Port = toPort,
    };
}

public static class VisionNodeKinds
{
    public const string Start = "start";
    public const string End = "end";

    /// <summary>Load image from file path.</summary>
    public const string LoadImage = "vision.loadImage";

    /// <summary>Convert to grayscale.</summary>
    public const string ToGray = "vision.toGray";

    /// <summary>Binary / adaptive threshold.</summary>
    public const string Threshold = "vision.threshold";

    /// <summary>Gaussian / median blur.</summary>
    public const string Blur = "vision.blur";

    /// <summary>Morphology: erode / dilate / open / close.</summary>
    public const string Morphology = "vision.morphology";

    /// <summary>Crop ROI (x,y,w,h).</summary>
    public const string Roi = "vision.roi";

    /// <summary>Template matching for industrial positioning.</summary>
    public const string TemplateMatch = "vision.templateMatch";

    /// <summary>Contour / blob detection.</summary>
    public const string FindContours = "vision.findContours";

    /// <summary>Hough circle detection.</summary>
    public const string FindCircles = "vision.findCircles";

    /// <summary>Hough line detection.</summary>
    public const string FindLines = "vision.findLines";

    /// <summary>Write pose / score into result variables.</summary>
    public const string OutputPose = "vision.outputPose";

    public static readonly string[] All =
    [
        Start, End,
        LoadImage, ToGray, Threshold, Blur, Morphology, Roi,
        TemplateMatch, FindContours, FindCircles, FindLines, OutputPose,
    ];

    public static readonly string[] Palette =
    [
        LoadImage, ToGray, Threshold, Blur, Morphology, Roi,
        TemplateMatch, FindContours, FindCircles, FindLines, OutputPose,
    ];

    public static bool IsKnown(string? kind) =>
        !string.IsNullOrWhiteSpace(kind)
        && All.Any(k => string.Equals(k, kind.Trim(), StringComparison.OrdinalIgnoreCase));

    public static bool IsTerminal(string? kind) =>
        string.Equals(kind, Start, StringComparison.OrdinalIgnoreCase)
        || string.Equals(kind, End, StringComparison.OrdinalIgnoreCase);
}

public static class VisionPorts
{
    public const string Next = "next";
    public const string Image = "image";
    public const string Pose = "pose";
}

/// <summary>Declares which ports each op kind consumes / produces.</summary>
public static class VisionPortCatalog
{
    public static bool AcceptsImage(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "vision.loadimage" => false,
        "vision.outputpose" => false,
        "start" or "end" => false,
        _ => VisionNodeKinds.IsKnown(kind) && !VisionNodeKinds.IsTerminal(kind),
    };

    public static bool ProducesImage(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "start" => true,
        "vision.loadimage" => true,
        "vision.togray" or "vision.threshold" or "vision.blur" or "vision.morphology" or "vision.roi" => true,
        "vision.templatematch" or "vision.findcontours" or "vision.findcircles" or "vision.findlines" => true,
        _ => false,
    };

    public static bool AcceptsPose(string? kind) =>
        string.Equals(kind, VisionNodeKinds.OutputPose, StringComparison.OrdinalIgnoreCase);

    public static bool ProducesPose(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "vision.templatematch" or "vision.findcontours" or "vision.findcircles" or "vision.findlines" => true,
        _ => false,
    };
}
