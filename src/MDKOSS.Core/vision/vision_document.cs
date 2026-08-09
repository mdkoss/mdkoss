using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Core.Vision;

/// <summary>Serialized vision pipeline stored in <see cref="MdkSetting.VisionConfig.Pipeline"/>.</summary>
public sealed class VisionDocument
{
    public int Version { get; set; } = 1;

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
        return new VisionDocument
        {
            Version = 1,
            Nodes =
            [
                new VisionNode { Id = startId, Kind = VisionNodeKinds.Start, X = left, Y = 40, Order = 0 },
                new VisionNode { Id = endId, Kind = VisionNodeKinds.End, X = left, Y = 152, Order = 1 },
            ],
            Edges =
            [
                new VisionEdge { From = startId, To = endId, Port = VisionPorts.Next },
            ],
        };
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

        var doc = new VisionDocument { Version = 1, Nodes = nodes };
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

    /// <summary>Rebuild linear next-edges from node <see cref="VisionNode.Order"/>.</summary>
    public void RebuildLinearEdges()
    {
        var ordered = Nodes
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Order = i;
        }

        Edges.Clear();
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

    /// <summary>Structural validation. Returns empty list when OK.</summary>
    public IReadOnlyList<string> Validate()
    {
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

public sealed class VisionNode
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = VisionNodeKinds.Start;
    public double X { get; set; }
    public double Y { get; set; }
    public Dictionary<string, string> Props { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Order along the linear pipeline spine.</summary>
    public int Order { get; set; }
}

public sealed class VisionEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Port { get; set; } = VisionPorts.Next;
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
}
