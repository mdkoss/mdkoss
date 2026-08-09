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
}
