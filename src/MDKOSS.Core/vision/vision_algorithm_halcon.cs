namespace MDKOSS.Core.Vision;

/// <summary>
/// Halcon algorithm backend placeholder. Register a real implementation (or replace via
/// <see cref="VisionAlgorithmRegistry.Register"/>) when Halcon DLLs / license are available.
/// </summary>
public sealed class HalconVisionBackend : IVisionAlgorithmBackend
{
    public const string BackendId = "halcon";

    public string Id => BackendId;
    public string DisplayName => "Halcon";

    /// <summary>
    /// Built-in stub is never available; host apps can <see cref="VisionAlgorithmRegistry.Register"/>
    /// a concrete Halcon backend with the same id to replace it.
    /// </summary>
    public bool IsAvailable => false;

    public VisionRunResult Run(VisionDocument document, string? inputImagePath = null, string? debugImagePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return new VisionRunResult
        {
            Ok = false,
            Error = "halcon backend is not installed; register a concrete IVisionAlgorithmBackend with id 'halcon'",
            Log =
            [
                "halcon: stub backend — extend via VisionAlgorithmRegistry.Register",
            ],
            DebugImagePath = debugImagePath,
        };
    }
}
