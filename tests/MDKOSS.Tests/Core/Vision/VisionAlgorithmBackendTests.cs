using MDKOSS.Core.Vision;
using OpenCvSharp;

namespace MDKOSS.Tests.Core.Vision;

public sealed class VisionAlgorithmBackendTests
{
    [Fact]
    public void Registry_lists_opencv_and_halcon()
    {
        var ids = VisionAlgorithmRegistry.List().Select(b => b.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(OpenCvVisionBackend.BackendId, ids);
        Assert.Contains(HalconVisionBackend.BackendId, ids);
        Assert.True(VisionAlgorithmRegistry.Resolve(null).IsAvailable);
        Assert.Equal(OpenCvVisionBackend.BackendId, VisionAlgorithmRegistry.Resolve("").Id);
    }

    [Fact]
    public void Halcon_stub_is_unavailable_and_executor_rejects()
    {
        var backend = VisionAlgorithmRegistry.Resolve(HalconVisionBackend.BackendId);
        Assert.False(backend.IsAvailable);

        var doc = VisionDocument.CreateBasicInspectPipeline();
        doc.Algorithm = HalconVisionBackend.BackendId;
        var result = new VisionExecutor().Run(doc);
        Assert.False(result.Ok);
        Assert.Contains("not available", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OpenCv_run_writes_debug_image_and_pose()
    {
        var input = Path.Combine(Path.GetTempPath(), "mdkoss-vision-test-input.png");
        var debug = Path.Combine(Path.GetTempPath(), "mdkoss-vision-test-debug.png");
        try
        {
            using (var mat = new Mat(240, 320, MatType.CV_8UC3, new Scalar(10, 10, 10)))
            {
                Cv2.Circle(mat, new Point(160, 120), 40, new Scalar(240, 240, 240), -1);
                Cv2.ImWrite(input, mat);
            }

            var doc = VisionDocument.CreateBasicInspectPipeline();
            doc.Algorithm = OpenCvVisionBackend.BackendId;
            var result = new VisionExecutor().Run(doc, input, debug);
            Assert.True(result.Ok, result.Error);
            Assert.True(File.Exists(debug));
            Assert.True(result.Pose.Ok);
            Assert.InRange(result.Pose.X, 140, 180);
            Assert.InRange(result.Pose.Y, 100, 140);
        }
        finally
        {
            TryDelete(input);
            TryDelete(debug);
        }
    }

    [Fact]
    public void VisionRoiRect_clamp_and_normalize()
    {
        var flipped = new VisionRoiRect(50, 50, -20, -10).Normalize();
        Assert.Equal(30, flipped.X);
        Assert.Equal(40, flipped.Y);
        Assert.Equal(20, flipped.W);
        Assert.Equal(10, flipped.H);

        var clamped = new VisionRoiRect(300, 300, 100, 100).ClampToImage(320, 240);
        Assert.Equal(300, clamped.X);
        Assert.Equal(239, clamped.Y);
        Assert.Equal(20, clamped.W);
        Assert.Equal(1, clamped.H);

        var props = VisionRoiRect.FromProps(new Dictionary<string, string>
        {
            ["x"] = "10",
            ["y"] = "20",
            ["w"] = "30",
            ["h"] = "40",
        }).ToProps();
        Assert.Equal("10", props["x"]);
        Assert.Equal("40", props["h"]);
    }

    [Fact]
    public void Document_roundtrips_algorithm_field()
    {
        var doc = VisionDocument.CreateEmpty();
        doc.Algorithm = HalconVisionBackend.BackendId;
        var json = doc.ToJson();
        var parsed = VisionDocument.Parse(json);
        Assert.Equal(HalconVisionBackend.BackendId, parsed.Algorithm);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore temp cleanup
        }
    }
}
