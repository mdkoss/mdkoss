using OpenCvSharp;

namespace MDKOSS.Core.Vision;

/// <summary>Runs a linear <see cref="VisionDocument"/> pipeline with OpenCV op blocks.</summary>
public sealed class VisionExecutor
{
    /// <summary>
    /// Execute the pipeline. Optional <paramref name="inputImagePath"/> overrides the first
    /// <c>vision.loadImage</c> path when provided.
    /// </summary>
    public VisionRunResult Run(VisionDocument document, string? inputImagePath = null, string? debugImagePath = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var errors = document.Validate();
        if (errors.Count > 0)
        {
            return new VisionRunResult
            {
                Ok = false,
                Error = string.Join("; ", errors),
            };
        }

        var ordered = document.Nodes
            .OrderBy(n => n.Order)
            .ThenBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var ctx = new VisionContext();
        try
        {
            if (!string.IsNullOrWhiteSpace(inputImagePath))
            {
                if (!File.Exists(inputImagePath))
                {
                    return new VisionRunResult { Ok = false, Error = $"input image not found: {inputImagePath}" };
                }

                var img = Cv2.ImRead(inputImagePath, ImreadModes.Color);
                if (img.Empty())
                {
                    return new VisionRunResult { Ok = false, Error = $"cannot read input image: {inputImagePath}" };
                }

                ctx.ReplaceImage(img);
                ctx.Log($"seed input ← {inputImagePath}");
            }

            foreach (var node in ordered)
            {
                var kind = (node.Kind ?? "").Trim();
                if (VisionNodeKinds.IsTerminal(kind))
                {
                    continue;
                }

                Dispatch(ctx, node, kind, inputImagePath);
            }

            if (!string.IsNullOrWhiteSpace(debugImagePath) && ctx.Image is not null && !ctx.Image.Empty())
            {
                var dir = Path.GetDirectoryName(debugImagePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Cv2.ImWrite(debugImagePath, ctx.Image);
            }

            return new VisionRunResult
            {
                Ok = ctx.Pose.Ok || ctx.Image is not null,
                Pose = ctx.Pose,
                Vars = new Dictionary<string, object?>(ctx.Vars, StringComparer.OrdinalIgnoreCase),
                Log = [.. ctx.Messages],
                DebugImagePath = debugImagePath,
            };
        }
        catch (Exception ex)
        {
            return new VisionRunResult
            {
                Ok = false,
                Error = ex.Message,
                Pose = ctx.Pose,
                Vars = new Dictionary<string, object?>(ctx.Vars, StringComparer.OrdinalIgnoreCase),
                Log = [.. ctx.Messages, $"error: {ex.Message}"],
            };
        }
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

    private static void Dispatch(VisionContext ctx, VisionNode node, string kind, string? seededInput)
    {
        switch (kind.ToLowerInvariant())
        {
            case "vision.loadimage":
                if (!string.IsNullOrWhiteSpace(seededInput) && ctx.Image is not null && !ctx.Image.Empty())
                {
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
