using System.Globalization;
using OpenCvSharp;

namespace MDKOSS.Core.Vision;

/// <summary>Shared OpenCV helpers and industrial vision op implementations.</summary>
internal static class VisionOps
{
    public static Mat RequireImage(VisionContext ctx)
    {
        if (ctx.Image is null || ctx.Image.Empty())
        {
            throw new InvalidOperationException("当前无图像：请先执行 vision.loadImage。");
        }

        return ctx.Image;
    }

    public static Mat EnsureGray(Mat src)
    {
        if (src.Channels() == 1)
        {
            return src.Clone();
        }

        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    public static void LoadImage(VisionContext ctx, VisionNode node)
    {
        var path = Prop(node, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("vision.loadImage 需要 path 参数。");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"图像文件不存在: {path}", path);
        }

        var img = Cv2.ImRead(path, ImreadModes.Color);
        if (img.Empty())
        {
            throw new InvalidOperationException($"无法读取图像: {path}");
        }

        ctx.ReplaceImage(img);
        ctx.Log($"loadImage ← {path} ({img.Width}x{img.Height})");
    }

    public static void ToGray(VisionContext ctx, VisionNode node)
    {
        var src = RequireImage(ctx);
        var gray = EnsureGray(src);
        ctx.ReplaceImage(gray);
        ctx.Log("toGray");
    }

    public static void Threshold(VisionContext ctx, VisionNode node)
    {
        var src = EnsureGray(RequireImage(ctx));
        var mode = Prop(node, "mode", "binary").ToLowerInvariant();
        var thresh = PropDouble(node, "thresh", 128);
        var maxVal = PropDouble(node, "maxVal", 255);
        var dst = new Mat();

        switch (mode)
        {
            case "otsu":
                Cv2.Threshold(src, dst, 0, maxVal, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                break;
            case "inv":
            case "binary_inv":
                Cv2.Threshold(src, dst, thresh, maxVal, ThresholdTypes.BinaryInv);
                break;
            case "adaptive":
            case "adaptive_mean":
                Cv2.AdaptiveThreshold(
                    src, dst, maxVal,
                    AdaptiveThresholdTypes.MeanC,
                    ThresholdTypes.Binary,
                    PropOdd(node, "blockSize", 11),
                    PropDouble(node, "c", 2));
                break;
            case "adaptive_gaussian":
                Cv2.AdaptiveThreshold(
                    src, dst, maxVal,
                    AdaptiveThresholdTypes.GaussianC,
                    ThresholdTypes.Binary,
                    PropOdd(node, "blockSize", 11),
                    PropDouble(node, "c", 2));
                break;
            default:
                Cv2.Threshold(src, dst, thresh, maxVal, ThresholdTypes.Binary);
                break;
        }

        src.Dispose();
        ctx.ReplaceImage(dst);
        ctx.Log($"threshold mode={mode}");
    }

    public static void Blur(VisionContext ctx, VisionNode node)
    {
        var src = RequireImage(ctx);
        var kind = Prop(node, "kind", "gaussian").ToLowerInvariant();
        var k = PropOdd(node, "ksize", 5);
        var dst = new Mat();

        switch (kind)
        {
            case "median":
                Cv2.MedianBlur(src, dst, k);
                break;
            case "box":
            case "average":
                Cv2.Blur(src, dst, new Size(k, k));
                break;
            default:
                Cv2.GaussianBlur(src, dst, new Size(k, k), PropDouble(node, "sigma", 0));
                break;
        }

        ctx.ReplaceImage(dst);
        ctx.Log($"blur kind={kind} ksize={k}");
    }

    public static void Morphology(VisionContext ctx, VisionNode node)
    {
        var src = RequireImage(ctx);
        var opName = Prop(node, "op", "open").ToLowerInvariant();
        var k = PropOdd(node, "ksize", 3);
        var iterations = Math.Max(1, PropInt(node, "iterations", 1));
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(k, k));
        var morph = opName switch
        {
            "erode" => MorphTypes.Erode,
            "dilate" => MorphTypes.Dilate,
            "close" => MorphTypes.Close,
            "gradient" => MorphTypes.Gradient,
            _ => MorphTypes.Open,
        };

        var dst = new Mat();
        Cv2.MorphologyEx(src, dst, morph, kernel, iterations: iterations);
        ctx.ReplaceImage(dst);
        ctx.Log($"morphology op={opName} ksize={k} iter={iterations}");
    }

    public static void Roi(VisionContext ctx, VisionNode node)
    {
        var src = RequireImage(ctx);
        var x = PropInt(node, "x", 0);
        var y = PropInt(node, "y", 0);
        var w = PropInt(node, "w", src.Width);
        var h = PropInt(node, "h", src.Height);
        x = Math.Clamp(x, 0, Math.Max(0, src.Width - 1));
        y = Math.Clamp(y, 0, Math.Max(0, src.Height - 1));
        w = Math.Clamp(w, 1, src.Width - x);
        h = Math.Clamp(h, 1, src.Height - y);
        var rect = new Rect(x, y, w, h);
        ctx.RoiOffsetX = x;
        ctx.RoiOffsetY = y;
        ctx.ReplaceImage(new Mat(src, rect).Clone());
        ctx.Log($"roi ({x},{y},{w},{h})");
    }

    public static void TemplateMatch(VisionContext ctx, VisionNode node)
    {
        var src = EnsureGray(RequireImage(ctx));
        var templatePath = Prop(node, "templatePath");
        if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
        {
            throw new FileNotFoundException($"模板图像不存在: {templatePath}", templatePath);
        }

        using var templateColor = Cv2.ImRead(templatePath, ImreadModes.Color);
        if (templateColor.Empty())
        {
            throw new InvalidOperationException($"无法读取模板: {templatePath}");
        }

        using var template = EnsureGray(templateColor);
        if (template.Width > src.Width || template.Height > src.Height)
        {
            throw new InvalidOperationException("模板尺寸大于当前图像。");
        }

        using var result = new Mat();
        Cv2.MatchTemplate(src, template, result, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(result, out _, out var maxVal, out _, out var maxLoc);

        var threshold = PropDouble(node, "minScore", 0.7);
        var ok = maxVal >= threshold;
        var cx = maxLoc.X + template.Width / 2.0 + ctx.RoiOffsetX;
        var cy = maxLoc.Y + template.Height / 2.0 + ctx.RoiOffsetY;
        var angle = PropDouble(node, "angle", 0);

        ctx.Pose = new VisionPose
        {
            Ok = ok,
            X = cx,
            Y = cy,
            AngleDeg = angle,
            Score = maxVal,
            Message = ok ? "templateMatch_ok" : "templateMatch_below_threshold",
        };
        
        ctx.SetVar("match.x", cx);
        ctx.SetVar("match.y", cy);
        ctx.SetVar("match.score", maxVal);
        ctx.SetVar("match.ok", ok);

        // Draw marker on working color preview if possible
        if (ctx.Image is not null && !ctx.Image.Empty())
        {
            var draw = ctx.Image.Channels() == 1
                ? new Mat()
                : ctx.Image.Clone();
            if (ctx.Image.Channels() == 1)
            {
                Cv2.CvtColor(ctx.Image, draw, ColorConversionCodes.GRAY2BGR);
            }

            var tl = new Point(maxLoc.X, maxLoc.Y);
            var br = new Point(maxLoc.X + template.Width, maxLoc.Y + template.Height);
            Cv2.Rectangle(draw, tl, br, ok ? Scalar.Lime : Scalar.Red, 2);
            Cv2.Circle(draw, new Point((int)(maxLoc.X + template.Width / 2.0), (int)(maxLoc.Y + template.Height / 2.0)),
                4, ok ? Scalar.Lime : Scalar.Red, -1);
            ctx.ReplaceImage(draw);
        }

        src.Dispose();
        ctx.Log($"templateMatch score={maxVal:F4} ok={ok} @({cx:F1},{cy:F1})");
    }

    public static void FindContours(VisionContext ctx, VisionNode node)
    {
        var src = EnsureGray(RequireImage(ctx));
        using var binary = new Mat();
        Cv2.Threshold(src, binary, PropDouble(node, "thresh", 128), 255, ThresholdTypes.Binary);
        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var minArea = PropDouble(node, "minArea", 50);
        var candidates = contours
            .Select(c => (Contour: c, Area: Cv2.ContourArea(c)))
            .Where(x => x.Area >= minArea)
            .OrderByDescending(x => x.Area)
            .ToList();

        var ok = candidates.Count > 0;
        double cx = 0, cy = 0, angle = 0, score = 0;
        if (ok)
        {
            var best = candidates[0];
            var moments = Cv2.Moments(best.Contour);
            if (Math.Abs(moments.M00) > 1e-9)
            {
                cx = moments.M10 / moments.M00 + ctx.RoiOffsetX;
                cy = moments.M01 / moments.M00 + ctx.RoiOffsetY;
            }

            var rect = Cv2.MinAreaRect(best.Contour);
            angle = rect.Angle;
            score = best.Area;
        }

        ctx.Pose = new VisionPose
        {
            Ok = ok,
            X = cx,
            Y = cy,
            AngleDeg = angle,
            Score = score,
            Message = ok ? $"contours={candidates.Count}" : "no_contour",
        };
        ctx.SetVar("blob.count", candidates.Count);
        ctx.SetVar("blob.x", cx);
        ctx.SetVar("blob.y", cy);
        ctx.SetVar("blob.area", score);
        ctx.SetVar("blob.ok", ok);

        var draw = new Mat();
        Cv2.CvtColor(src, draw, ColorConversionCodes.GRAY2BGR);
        if (ok)
        {
            Cv2.DrawContours(draw, [candidates[0].Contour], -1, Scalar.Lime, 2);
            Cv2.Circle(draw, new Point((int)(cx - ctx.RoiOffsetX), (int)(cy - ctx.RoiOffsetY)), 4, Scalar.Red, -1);
        }

        src.Dispose();
        ctx.ReplaceImage(draw);
        ctx.Log($"findContours count={candidates.Count} ok={ok}");
    }

    public static void FindCircles(VisionContext ctx, VisionNode node)
    {
        var src = EnsureGray(RequireImage(ctx));
        using var blurred = new Mat();
        Cv2.GaussianBlur(src, blurred, new Size(9, 9), 2);
        var circles = Cv2.HoughCircles(
            blurred,
            HoughModes.Gradient,
            dp: PropDouble(node, "dp", 1),
            minDist: PropDouble(node, "minDist", 20),
            param1: PropDouble(node, "param1", 100),
            param2: PropDouble(node, "param2", 30),
            minRadius: PropInt(node, "minRadius", 5),
            maxRadius: PropInt(node, "maxRadius", 0));

        var ok = circles is { Length: > 0 };
        double cx = 0, cy = 0, radius = 0;
        if (ok)
        {
            var best = circles.OrderByDescending(c => c.Radius).First();
            cx = best.Center.X + ctx.RoiOffsetX;
            cy = best.Center.Y + ctx.RoiOffsetY;
            radius = best.Radius;
        }

        ctx.Pose = new VisionPose
        {
            Ok = ok,
            X = cx,
            Y = cy,
            AngleDeg = 0,
            Score = radius,
            Message = ok ? $"circles={circles!.Length}" : "no_circle",
        };
        ctx.SetVar("circle.x", cx);
        ctx.SetVar("circle.y", cy);
        ctx.SetVar("circle.r", radius);
        ctx.SetVar("circle.ok", ok);

        var draw = new Mat();
        Cv2.CvtColor(src, draw, ColorConversionCodes.GRAY2BGR);
        if (ok)
        {
            Cv2.Circle(draw, new Point((int)(cx - ctx.RoiOffsetX), (int)(cy - ctx.RoiOffsetY)), (int)radius, Scalar.Lime, 2);
            Cv2.Circle(draw, new Point((int)(cx - ctx.RoiOffsetX), (int)(cy - ctx.RoiOffsetY)), 3, Scalar.Red, -1);
        }

        src.Dispose();
        ctx.ReplaceImage(draw);
        ctx.Log($"findCircles count={(circles?.Length ?? 0)} ok={ok}");
    }

    public static void FindLines(VisionContext ctx, VisionNode node)
    {
        var src = EnsureGray(RequireImage(ctx));
        using var edges = new Mat();
        Cv2.Canny(src, edges, PropDouble(node, "canny1", 50), PropDouble(node, "canny2", 150));
        var lines = Cv2.HoughLinesP(
            edges,
            rho: 1,
            theta: Math.PI / 180,
            threshold: PropInt(node, "threshold", 50),
            minLineLength: PropDouble(node, "minLength", 30),
            maxLineGap: PropDouble(node, "maxGap", 10));

        var ok = lines is { Length: > 0 };
        double cx = 0, cy = 0, angle = 0, score = 0;
        if (ok)
        {
            var best = lines
                .Select(l =>
                {
                    var dx = l.P2.X - l.P1.X;
                    var dy = l.P2.Y - l.P1.Y;
                    var len = Math.Sqrt(dx * dx + dy * dy);
                    return (Line: l, Len: len, Angle: Math.Atan2(dy, dx) * 180.0 / Math.PI);
                })
                .OrderByDescending(x => x.Len)
                .First();
            cx = (best.Line.P1.X + best.Line.P2.X) / 2.0 + ctx.RoiOffsetX;
            cy = (best.Line.P1.Y + best.Line.P2.Y) / 2.0 + ctx.RoiOffsetY;
            angle = best.Angle;
            score = best.Len;
        }

        ctx.Pose = new VisionPose
        {
            Ok = ok,
            X = cx,
            Y = cy,
            AngleDeg = angle,
            Score = score,
            Message = ok ? $"lines={lines!.Length}" : "no_line",
        };
        ctx.SetVar("line.x", cx);
        ctx.SetVar("line.y", cy);
        ctx.SetVar("line.angle", angle);
        ctx.SetVar("line.length", score);
        ctx.SetVar("line.ok", ok);

        var draw = new Mat();
        Cv2.CvtColor(src, draw, ColorConversionCodes.GRAY2BGR);
        if (ok && lines is not null)
        {
            foreach (var line in lines.Take(20))
            {
                Cv2.Line(draw, line.P1, line.P2, Scalar.Lime, 2);
            }
        }

        src.Dispose();
        ctx.ReplaceImage(draw);
        ctx.Log($"findLines count={(lines?.Length ?? 0)} ok={ok}");
    }

    public static void OutputPose(VisionContext ctx, VisionNode node)
    {
        var prefix = Prop(node, "prefix", "vision");
        var requireOk = PropBool(node, "requireOk", false);
        if (requireOk && !ctx.Pose.Ok)
        {
            throw new InvalidOperationException("outputPose: 上游定位未成功 (pose.ok=false)。");
        }

        foreach (var kv in ctx.Pose.ToDictionary(prefix))
        {
            ctx.SetVar(kv.Key, kv.Value);
        }

        ctx.Log($"outputPose prefix={prefix} ok={ctx.Pose.Ok} x={ctx.Pose.X:F2} y={ctx.Pose.Y:F2} ang={ctx.Pose.AngleDeg:F2}");
    }

    public static string Prop(VisionNode node, string key, string fallback = "") =>
        node.Props.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    public static int PropInt(VisionNode node, string key, int fallback) =>
        int.TryParse(Prop(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static double PropDouble(VisionNode node, string key, double fallback) =>
        double.TryParse(Prop(node, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public static bool PropBool(VisionNode node, string key, bool fallback)
    {
        var raw = Prop(node, key);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" => true,
            "0" or "false" or "no" or "n" => false,
            _ => bool.TryParse(raw, out var b) ? b : fallback,
        };
    }

    public static int PropOdd(VisionNode node, string key, int fallback)
    {
        var v = PropInt(node, key, fallback);
        if (v < 1)
        {
            v = 1;
        }

        if (v % 2 == 0)
        {
            v++;
        }

        return v;
    }
}

/// <summary>Mutable execution state for a vision pipeline run.</summary>
internal sealed class VisionContext : IDisposable
{
    public Mat? Image { get; private set; }
    public int RoiOffsetX { get; set; }
    public int RoiOffsetY { get; set; }
    public VisionPose Pose { get; set; } = new();
    public Dictionary<string, object?> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Messages { get; } = [];

    public void ReplaceImage(Mat next)
    {
        Image?.Dispose();
        Image = next;
    }

    public void SetVar(string key, object? value) => Vars[key] = value;

    public void Log(string message) => Messages.Add(message);

    public void Dispose()
    {
        Image?.Dispose();
        Image = null;
    }
}
