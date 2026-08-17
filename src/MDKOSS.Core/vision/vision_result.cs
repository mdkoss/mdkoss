namespace MDKOSS.Core.Vision;

/// <summary>Industrial vision pose / match result written by pipeline ops.</summary>
public sealed class VisionPose
{
    public double X { get; set; }
    public double Y { get; set; }
    public double AngleDeg { get; set; }
    public double Score { get; set; }
    public bool Ok { get; set; }
    public string? Message { get; set; }

    public Dictionary<string, object?> ToDictionary(string prefix = "vision")
    {
        var p = string.IsNullOrWhiteSpace(prefix) ? "vision" : prefix.Trim();
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{p}.ok"] = Ok,
            [$"{p}.x"] = X,
            [$"{p}.y"] = Y,
            [$"{p}.angle"] = AngleDeg,
            [$"{p}.score"] = Score,
            [$"{p}.message"] = Message ?? "",
        };
    }

    public VisionPose Clone() => new()
    {
        X = X,
        Y = Y,
        AngleDeg = AngleDeg,
        Score = Score,
        Ok = Ok,
        Message = Message,
    };
}

/// <summary>Execution outcome of a <see cref="VisionDocument"/> pipeline.</summary>
public sealed class VisionRunResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public VisionPose Pose { get; set; } = new();
    public Dictionary<string, object?> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Log { get; set; } = [];

    /// <summary>Optional debug image path written by executor when <c>saveDebug</c> is set.</summary>
    public string? DebugImagePath { get; set; }

    /// <summary>
    /// Per-node input/output snapshots. Populated only when
    /// <see cref="VisionRunRequest.KeepIntermediates"/> is true (editor trial / debug).
    /// </summary>
    public List<VisionNodeTrace> NodeTraces { get; set; } = [];
}

/// <summary>Original image for <see cref="VisionExecutor"/> — file path and/or in-memory encoded bytes.</summary>
public sealed class VisionRunRequest
{
    public string? InputImagePath { get; set; }
    public byte[]? InputImageBytes { get; set; }
    public string? DebugImagePath { get; set; }

    /// <summary>Editor / debug: keep every node image and write <see cref="VisionNodeTrace"/> files.</summary>
    public bool KeepIntermediates { get; set; }

    /// <summary>Directory for per-node PNG traces. Created when <see cref="KeepIntermediates"/> is set.</summary>
    public string? TraceDirectory { get; set; }

    public static VisionRunRequest FromPath(string path, string? debugImagePath = null) => new()
    {
        InputImagePath = path,
        DebugImagePath = debugImagePath,
    };

    public static VisionRunRequest FromBytes(byte[] bytes, string? debugImagePath = null) => new()
    {
        InputImageBytes = bytes,
        DebugImagePath = debugImagePath,
    };
}

/// <summary>Per-operator snapshot for editor step-through inspection.</summary>
public sealed class VisionNodeTrace
{
    public string NodeId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? InputImagePath { get; set; }
    public string? OutputImagePath { get; set; }
    public int InputWidth { get; set; }
    public int InputHeight { get; set; }
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public Dictionary<string, object?> OutputVars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public VisionPose? OutputPose { get; set; }
}
