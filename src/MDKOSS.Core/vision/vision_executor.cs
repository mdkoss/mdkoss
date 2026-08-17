namespace MDKOSS.Core.Vision;

/// <summary>
/// Runs a <see cref="VisionDocument"/> algorithm via the selected
/// <see cref="IVisionAlgorithmBackend"/> (default OpenCV; Halcon and others via registry).
/// External callers use <see cref="Execute"/> with a vision id and original image only.
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
    /// Execute the named algorithm file on an original image. The caller does not touch pipeline JSON.
    /// </summary>
    public VisionRunResult Execute(
        string visionId,
        VisionRunRequest request,
        Func<string, VisionDocument?> resolvePipeline)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(resolvePipeline);
        if (string.IsNullOrWhiteSpace(visionId))
        {
            return new VisionRunResult { Ok = false, Error = "visionId_empty" };
        }

        var doc = resolvePipeline(visionId.Trim());
        if (doc is null || doc.Nodes.Count == 0)
        {
            return new VisionRunResult { Ok = false, Error = $"vision_not_found:{visionId.Trim()}" };
        }

        return Run(doc, request);
    }

    /// <summary>
    /// Execute the pipeline. Optional <paramref name="inputImagePath"/> seeds the original image
    /// (overrides the first <c>vision.loadImage</c> when present).
    /// </summary>
    public VisionRunResult Run(VisionDocument document, string? inputImagePath = null, string? debugImagePath = null) =>
        Run(document, new VisionRunRequest
        {
            InputImagePath = inputImagePath,
            DebugImagePath = debugImagePath,
        });

    public VisionRunResult Run(VisionDocument document, VisionRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        var backend = _forcedBackend ?? VisionAlgorithmRegistry.Resolve(document.Algorithm);
        if (!backend.IsAvailable)
        {
            return new VisionRunResult
            {
                Ok = false,
                Error = $"vision algorithm '{backend.Id}' is not available on this machine",
                Log = [$"backend={backend.Id} available=false"],
                DebugImagePath = request.DebugImagePath,
            };
        }

        return backend.Run(document, request);
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
