using OpenCvSharp;

namespace MDKOSS.Core.Vision;

/// <summary>Default OpenCV-backed vision algorithm platform (explicit dataflow execution).</summary>
public sealed class OpenCvVisionBackend : IVisionAlgorithmBackend
{
    public const string BackendId = "opencv";

    public string Id => BackendId;
    public string DisplayName => "OpenCV";
    public bool IsAvailable => true;

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
        var errors = document.Validate();
        if (errors.Count > 0)
        {
            return new VisionRunResult
            {
                Ok = false,
                Error = string.Join("; ", errors),
            };
        }

        var seed = LoadSeed(request, out var seedError);
        if (seedError is not null)
        {
            seed?.Dispose();
            return new VisionRunResult { Ok = false, Error = seedError };
        }

        var startId = document.Nodes
            .FirstOrDefault(n => string.Equals(n.Kind, VisionNodeKinds.Start, StringComparison.OrdinalIgnoreCase))
            ?.Id;
        var remainingConsumers = CountImageConsumers(document);
        var traces = new List<VisionNodeTrace>();
        var traceDir = request.KeepIntermediates
            ? (string.IsNullOrWhiteSpace(request.TraceDirectory)
                ? Path.Combine(Path.GetTempPath(), "mdkoss-vision-trace", Guid.NewGuid().ToString("N"))
                : request.TraceDirectory.Trim())
            : null;
        if (!string.IsNullOrWhiteSpace(traceDir))
        {
            Directory.CreateDirectory(traceDir);
        }

        using var ctx = new VisionContext { KeepIntermediates = request.KeepIntermediates };
        try
        {
            if (seed is not null)
            {
                ctx.SetOriginal(seed);
                if (!string.IsNullOrWhiteSpace(startId))
                {
                    ctx.PublishPinned(startId, seed);
                }

                ctx.Log(request.InputImageBytes is { Length: > 0 }
                    ? $"seed input ← bytes[{request.InputImageBytes.Length}]"
                    : $"seed input ← {request.InputImagePath}");
                seed = null;
            }

            var seeded = ctx.OriginalImage is not null;
            foreach (var node in document.ExecutionOrder())
            {
                var kind = (node.Kind ?? "").Trim();
                if (string.Equals(kind, VisionNodeKinds.End, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(kind, VisionNodeKinds.Start, StringComparison.OrdinalIgnoreCase))
                {
                    if (request.KeepIntermediates)
                    {
                        traces.Add(CaptureTrace(ctx, node, kind, input: ctx.OriginalImage, output: ctx.OriginalImage, varsBefore: [], traceDir));
                    }

                    continue;
                }

                BindInputs(document, ctx, node, kind, startId);
                if (string.Equals(kind, VisionNodeKinds.LoadImage, StringComparison.OrdinalIgnoreCase)
                    && seeded
                    && ctx.Image is null
                    && !string.IsNullOrWhiteSpace(startId))
                {
                    ctx.TryBindImageFrom(startId);
                }

                Mat? inputSnap = null;
                if (request.KeepIntermediates && ctx.Image is not null && !ctx.Image.Empty())
                {
                    inputSnap = ctx.Image.Clone();
                }

                var varsBefore = ctx.Vars.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                Dispatch(ctx, node, kind, seeded);
                ctx.CommitOutput(node.Id);

                if (request.KeepIntermediates)
                {
                    traces.Add(CaptureTrace(
                        ctx, node, kind, inputSnap, ctx.GetPublishedImage(node.Id), varsBefore, traceDir));
                }

                inputSnap?.Dispose();
                ReleaseConsumed(document, ctx, node, remainingConsumers);
            }

            var debugSrc = ctx.LastPublishedImage ?? ctx.OriginalImage;
            if (!string.IsNullOrWhiteSpace(request.DebugImagePath) && debugSrc is not null && !debugSrc.Empty())
            {
                var dir = Path.GetDirectoryName(request.DebugImagePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Cv2.ImWrite(request.DebugImagePath, debugSrc);
            }

            return new VisionRunResult
            {
                Ok = ctx.Pose.Ok || debugSrc is not null,
                Pose = ctx.Pose.Clone(),
                Vars = new Dictionary<string, object?>(ctx.Vars, StringComparer.OrdinalIgnoreCase),
                Log = [.. ctx.Messages],
                DebugImagePath = request.DebugImagePath,
                NodeTraces = traces,
            };
        }
        catch (Exception ex)
        {
            return new VisionRunResult
            {
                Ok = false,
                Error = ex.Message,
                Pose = ctx.Pose.Clone(),
                Vars = new Dictionary<string, object?>(ctx.Vars, StringComparer.OrdinalIgnoreCase),
                Log = [.. ctx.Messages, $"error: {ex.Message}"],
                NodeTraces = traces,
            };
        }
        finally
        {
            seed?.Dispose();
        }
    }

    private static Mat? LoadSeed(VisionRunRequest request, out string? error)
    {
        error = null;
        if (request.InputImageBytes is { Length: > 0 })
        {
            var img = Cv2.ImDecode(request.InputImageBytes, ImreadModes.Color);
            if (img.Empty())
            {
                img.Dispose();
                error = "cannot decode input image bytes";
                return null;
            }

            return img;
        }

        if (string.IsNullOrWhiteSpace(request.InputImagePath))
        {
            return null;
        }

        if (!File.Exists(request.InputImagePath))
        {
            error = $"input image not found: {request.InputImagePath}";
            return null;
        }

        var loaded = Cv2.ImRead(request.InputImagePath, ImreadModes.Color);
        if (loaded.Empty())
        {
            loaded.Dispose();
            error = $"cannot read input image: {request.InputImagePath}";
            return null;
        }

        return loaded;
    }

    private static void BindInputs(VisionDocument document, VisionContext ctx, VisionNode node, string kind, string? startId)
    {
        var imageEdge = document.FindIncomingData(node.Id, VisionPorts.Image);
        if (imageEdge is not null)
        {
            if (!ctx.TryBindImageFrom(imageEdge.From))
            {
                throw new InvalidOperationException($"node '{node.Id}' image input '{imageEdge.From}' is empty.");
            }
        }
        else if (VisionPortCatalog.AcceptsImage(kind) && !string.IsNullOrWhiteSpace(startId))
        {
            ctx.TryBindImageFrom(startId);
        }

        var poseEdge = document.FindIncomingData(node.Id, VisionPorts.Pose);
        if (poseEdge is not null)
        {
            ctx.TryBindPoseFrom(poseEdge.From);
        }
    }

    private static Dictionary<string, int> CountImageConsumers(VisionDocument document)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in document.Edges)
        {
            if (!edge.IsData
                || !string.Equals(edge.EffectiveToPort, VisionPorts.Image, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var from = (edge.From ?? "").Trim();
            if (from.Length == 0)
            {
                continue;
            }

            counts[from] = counts.GetValueOrDefault(from) + 1;
        }

        return counts;
    }

    private static void ReleaseConsumed(
        VisionDocument document,
        VisionContext ctx,
        VisionNode node,
        Dictionary<string, int> remaining)
    {
        foreach (var edge in document.Edges)
        {
            if (!edge.IsData
                || !string.Equals(edge.To, node.Id, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(edge.EffectiveToPort, VisionPorts.Image, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var from = (edge.From ?? "").Trim();
            if (from.Length == 0 || !remaining.ContainsKey(from))
            {
                continue;
            }

            remaining[from]--;
            ctx.ReleaseIfUnused(from, remaining[from]);
        }
    }

    private static VisionNodeTrace CaptureTrace(
        VisionContext ctx,
        VisionNode node,
        string kind,
        Mat? input,
        Mat? output,
        HashSet<string> varsBefore,
        string? traceDir)
    {
        var trace = new VisionNodeTrace
        {
            NodeId = node.Id,
            Kind = kind,
            OutputPose = ctx.GetPublishedPose(node.Id)?.Clone() ?? ctx.Pose.Clone(),
        };

        if (input is not null && !input.Empty())
        {
            trace.InputWidth = input.Width;
            trace.InputHeight = input.Height;
            trace.InputImagePath = WriteTracePng(traceDir, node.Id, "in", input);
        }

        if (output is not null && !output.Empty())
        {
            trace.OutputWidth = output.Width;
            trace.OutputHeight = output.Height;
            trace.OutputImagePath = WriteTracePng(traceDir, node.Id, "out", output);
        }

        foreach (var kv in ctx.Vars)
        {
            if (!varsBefore.Contains(kv.Key))
            {
                trace.OutputVars[kv.Key] = kv.Value;
            }
        }

        if (trace.OutputVars.Count == 0 && VisionPortCatalog.ProducesPose(kind) && trace.OutputPose is not null)
        {
            foreach (var kv in trace.OutputPose.ToDictionary("pose"))
            {
                trace.OutputVars[kv.Key] = kv.Value;
            }
        }

        return trace;
    }

    private static string? WriteTracePng(string? dir, string nodeId, string suffix, Mat image)
    {
        if (string.IsNullOrWhiteSpace(dir))
        {
            return null;
        }

        var safe = string.Join("_", (nodeId ?? "node").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var path = Path.Combine(dir, $"{safe}-{suffix}.png");
        Cv2.ImWrite(path, image);
        return path;
    }

    private static void Dispatch(VisionContext ctx, VisionNode node, string kind, bool seededInput)
    {
        switch (kind.ToLowerInvariant())
        {
            case "vision.loadimage":
                if (seededInput && ctx.OriginalImage is not null)
                {
                    if (ctx.Image is null || ctx.Image.Empty())
                    {
                        ctx.ReplaceImage(ctx.OriginalImage.Clone());
                    }

                    ctx.Log("loadImage skipped (seeded input)");
                    return;
                }

                VisionOps.LoadImage(ctx, node);
                break;
            case "vision.togray":
                VisionOps.ToGray(ctx, node);
                break;
            case "vision.threshold":
                VisionOps.Threshold(ctx, node);
                break;
            case "vision.blur":
                VisionOps.Blur(ctx, node);
                break;
            case "vision.morphology":
                VisionOps.Morphology(ctx, node);
                break;
            case "vision.roi":
                VisionOps.Roi(ctx, node);
                break;
            case "vision.templatematch":
                VisionOps.TemplateMatch(ctx, node);
                break;
            case "vision.findcontours":
                VisionOps.FindContours(ctx, node);
                break;
            case "vision.findcircles":
                VisionOps.FindCircles(ctx, node);
                break;
            case "vision.findlines":
                VisionOps.FindLines(ctx, node);
                break;
            case "vision.outputpose":
                VisionOps.OutputPose(ctx, node);
                break;
            default:
                throw new InvalidOperationException($"unsupported vision op: {kind}");
        }
    }
}
