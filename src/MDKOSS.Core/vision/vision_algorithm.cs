namespace MDKOSS.Core.Vision;

/// <summary>
/// Pluggable vision algorithm platform (OpenCV built-in; Halcon / others via registry).
/// Pipeline JSON uses <see cref="VisionDocument.Algorithm"/> to select the backend id.
/// </summary>
public interface IVisionAlgorithmBackend
{
    /// <summary>Stable id written into pipeline JSON (e.g. opencv, halcon).</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>False when native SDK / license is not present on this machine.</summary>
    bool IsAvailable { get; }

    VisionRunResult Run(VisionDocument document, string? inputImagePath = null, string? debugImagePath = null);

    VisionRunResult Run(VisionDocument document, VisionRunRequest request) =>
        Run(document, request.InputImagePath, request.DebugImagePath);
}

/// <summary>Global registry of vision algorithm backends. Hosts may <see cref="Register"/> custom platforms.</summary>
public static class VisionAlgorithmRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, IVisionAlgorithmBackend> Backends =
        new(StringComparer.OrdinalIgnoreCase);

    static VisionAlgorithmRegistry()
    {
        Register(new OpenCvVisionBackend());
        Register(new HalconVisionBackend());
    }

    public const string DefaultId = OpenCvVisionBackend.BackendId;

    public static void Register(IVisionAlgorithmBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (string.IsNullOrWhiteSpace(backend.Id))
        {
            throw new ArgumentException("backend id is required.", nameof(backend));
        }

        lock (Sync)
        {
            Backends[backend.Id.Trim()] = backend;
        }
    }

    public static IReadOnlyList<IVisionAlgorithmBackend> List()
    {
        lock (Sync)
        {
            return Backends.Values
                .OrderBy(b => b.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>Resolve by id; blank / unknown falls back to OpenCV when available.</summary>
    public static IVisionAlgorithmBackend Resolve(string? algorithmId)
    {
        lock (Sync)
        {
            if (!string.IsNullOrWhiteSpace(algorithmId)
                && Backends.TryGetValue(algorithmId.Trim(), out var exact))
            {
                return exact;
            }

            if (Backends.TryGetValue(DefaultId, out var opencv))
            {
                return opencv;
            }

            return Backends.Values.FirstOrDefault()
                   ?? throw new InvalidOperationException("no vision algorithm backend registered");
        }
    }
}

/// <summary>Axis-aligned ROI used by <c>vision.roi</c> and the editor overlay.</summary>
public readonly record struct VisionRoiRect(int X, int Y, int W, int H)
{
    public static VisionRoiRect FromProps(IReadOnlyDictionary<string, string>? props, int fallbackW = 200, int fallbackH = 200)
    {
        props ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static int Read(IReadOnlyDictionary<string, string> map, string key, int fallback) =>
            map.TryGetValue(key, out var raw) && int.TryParse(raw, out var v) ? v : fallback;

        return new VisionRoiRect(
            Read(props, "x", 0),
            Read(props, "y", 0),
            Math.Max(1, Read(props, "w", fallbackW)),
            Math.Max(1, Read(props, "h", fallbackH)));
    }

    public VisionRoiRect Normalize()
    {
        var x = X;
        var y = Y;
        var w = W;
        var h = H;
        if (w < 0)
        {
            x += w;
            w = -w;
        }

        if (h < 0)
        {
            y += h;
            h = -h;
        }

        return new VisionRoiRect(x, y, Math.Max(1, w), Math.Max(1, h));
    }

    public VisionRoiRect ClampToImage(int imageWidth, int imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return Normalize();
        }

        var n = Normalize();
        var x = Math.Clamp(n.X, 0, Math.Max(0, imageWidth - 1));
        var y = Math.Clamp(n.Y, 0, Math.Max(0, imageHeight - 1));
        var w = Math.Clamp(n.W, 1, imageWidth - x);
        var h = Math.Clamp(n.H, 1, imageHeight - y);
        return new VisionRoiRect(x, y, w, h);
    }

    public Dictionary<string, string> ToProps() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["x"] = X.ToString(),
        ["y"] = Y.ToString(),
        ["w"] = W.ToString(),
        ["h"] = H.ToString(),
    };
}
