namespace MDKOSS.Core.Vision;

/// <summary>
/// Runs a linear <see cref="VisionDocument"/> pipeline via the selected
/// <see cref="IVisionAlgorithmBackend"/> (default OpenCV; Halcon and others via registry).
/// </summary>
public sealed class VisionExecutor
{
    private readonly IVisionAlgorithmBackend? _forcedBackend;

    public VisionExecutor()
    {
    }

    public VisionExecutor(IVisionAlgorithmBackend backend)
    {
        _forcedBackend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    /// <summary>
    /// Execute the pipeline. Optional <paramref name="inputImagePath"/> overrides the first
    /// <c>vision.loadImage</c> path when provided.
    /// </summary>
    public VisionRunResult Run(VisionDocument document, string? inputImagePath = null, string? debugImagePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var backend = _forcedBackend ?? VisionAlgorithmRegistry.Resolve(document.Algorithm);
        if (!backend.IsAvailable)
        {
            return new VisionRunResult
            {
                Ok = false,
                Error = $"vision algorithm '{backend.Id}' is not available on this machine",
                Log = [$"backend={backend.Id} available=false"],
                DebugImagePath = debugImagePath,
            };
        }

        return backend.Run(document, inputImagePath, debugImagePath);
    }

    /// <summary>Parse pipeline JSON and run.</summary>
    public VisionRunResult RunJson(string? pipelineJson, string? inputImagePath = null, string? debugImagePath = null)
    {
        if (!VisionDocument.TryParse(pipelineJson, out var doc, out var error))
        {
            return new VisionRunResult { Ok = false, Error = error };
        }

        return Run(doc, inputImagePath, debugImagePath);
    }
}
