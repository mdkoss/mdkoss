using MDKOSS.Core;
using MDKOSS.Extensions.Camera;
using OpenCvSharp;

namespace MDKOSS.Tests.Extensions.Camera;

public sealed class CameraCatalogTests
{
    [Theory]
    [InlineData("sim")]
    [InlineData("file")]
    [InlineData("uvc")]
    [InlineData("hik")]
    [InlineData("daheng")]
    [InlineData("huaray")]
    [InlineData("mindvision")]
    [InlineData("basler")]
    [InlineData("flir")]
    [InlineData("tis")]
    public void Catalog_resolves_every_type(string type)
    {
        Assert.True(CameraCatalog.TryGet(type, out var kind));
        Assert.Equal(type, kind.Type);
        Assert.False(string.IsNullOrWhiteSpace(kind.Vendor));
    }

    [Theory]
    [InlineData("MVS", "hik")]
    [InlineData("hikvision", "hik")]
    [InlineData("galaxy", "daheng")]
    [InlineData("pylon", "basler")]
    [InlineData("spinnaker", "flir")]
    [InlineData("imagingsource", "tis")]
    [InlineData("usb", "uvc")]
    [InlineData("folder", "file")]
    public void Catalog_resolves_vendor_aliases(string alias, string expected)
    {
        Assert.True(CameraCatalog.TryGet(alias, out var kind));
        Assert.Equal(expected, kind.Type);
    }

    [Fact]
    public void Catalog_covers_market_cameras_and_defaults_to_sim()
    {
        Assert.Equal(10, CameraCatalog.All.Count);
        Assert.False(CameraCatalog.TryGet("no-such-camera", out _));
        Assert.Equal("sim", CameraCatalog.Resolve("no-such-camera").Type);

        var vendorSdks = CameraCatalog.All.Where(k => k.NeedsVendorSdk).ToList();
        Assert.Equal(7, vendorSdks.Count);
        Assert.All(vendorSdks, k => Assert.EndsWith(".dll", k.NativeDll, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ExtCameraParameterTests
{
    [Fact]
    public void ParseConfig_defaults_to_simulator()
    {
        var parameters = ExtCameraDeviceParameters.ParseConfig(null);
        Assert.Equal("sim", parameters.Kind.Type);
        Assert.Equal(10_000, parameters.ExposureUs);
        Assert.Equal(10, parameters.ExposureMs);
        Assert.Equal(CameraTriggerMode.Continuous, parameters.TriggerMode);
        Assert.True(parameters.FallbackToSim);
        Assert.True(parameters.AutoOpen);
    }

    [Fact]
    public void ParseConfig_reads_vendor_settings()
    {
        var parameters = ExtCameraDeviceParameters.ParseConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = "MVS",
            ["deviceIndex"] = "2",
            ["serialNumber"] = "DA1234",
            ["nativeDll"] = "MvCameraControl_v4.dll",
            ["width"] = "2448",
            ["height"] = "2048",
            ["exposureUs"] = "8500",
            ["gain"] = "3.5",
            ["triggerMode"] = "software",
            ["pixelFormat"] = "BayerRG8",
            ["timeoutMs"] = "1500",
            ["fallbackToSim"] = "false",
        });

        Assert.Equal("hik", parameters.Kind.Type);
        Assert.Equal(2, parameters.DeviceIndex);
        Assert.Equal("DA1234", parameters.SerialNumber);
        Assert.Equal("MvCameraControl_v4.dll", parameters.NativeDll);
        Assert.Equal(8500, parameters.ExposureUs);
        Assert.Equal(3.5, parameters.Gain);
        Assert.Equal(CameraTriggerMode.Software, parameters.TriggerMode);
        Assert.Equal("BayerRG8", parameters.PixelFormatName);
        Assert.Equal(1500, parameters.TimeoutMs);
        Assert.False(parameters.FallbackToSim);
    }

    [Fact]
    public void ParseConfig_keeps_legacy_exposure_ms()
    {
        var parameters = ExtCameraDeviceParameters.ParseConfig(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["exposureMs"] = "12",
        });

        Assert.Equal(12_000, parameters.ExposureUs);
        Assert.Equal(12, parameters.ExposureMs);
    }
}

public sealed class CameraPixelTests
{
    [Fact]
    public void Mono8_decodes_to_single_channel()
    {
        var frame = new CameraFrame(4, 2, CameraPixel.Mono8, new byte[8], 1, 0);
        using var mat = CameraPixel.ToMat(frame);
        Assert.Equal(1, mat.Channels());
        Assert.Equal(new Size(4, 2), mat.Size());
    }

    [Fact]
    public void Bayer_decodes_to_bgr()
    {
        var frame = new CameraFrame(4, 4, CameraPixel.BayerRG8, new byte[16], 1, 0);
        using var mat = CameraPixel.ToMat(frame);
        Assert.Equal(3, mat.Channels());
        Assert.Equal("bayerRG8", CameraPixel.Describe(CameraPixel.BayerRG8));
    }

    [Fact]
    public void Short_buffer_is_rejected()
    {
        var frame = new CameraFrame(64, 64, CameraPixel.Bgr8, new byte[16], 1, 0);
        Assert.Throws<InvalidOperationException>(() => CameraPixel.ToMat(frame));
    }

    [Fact]
    public void Unknown_format_is_not_supported()
    {
        Assert.False(CameraPixel.IsSupported(0xDEADBEEF));
        Assert.Equal(".jpg", CameraPixel.NormalizeExtension("JPEG"));
        Assert.Equal("image/png", CameraPixel.ContentType("png"));
    }
}

public sealed class ExtCameraDeviceTests
{
    [Fact]
    public void Simulator_opens_and_captures()
    {
        using var camera = NewCamera(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = "sim",
            ["width"] = "320",
            ["height"] = "240",
        });

        Assert.True(camera.Open());
        Assert.True(camera.IsOpen);

        var result = camera.TriggerCapture("demo");
        Assert.NotNull(result);
        Assert.True(result!.Ok);
        Assert.Equal(320, result.Width);
        Assert.Equal(240, result.Height);
        Assert.Equal("bgr8", result.PixelFormat);
        Assert.Equal(1, camera.CaptureCount);
        Assert.NotEmpty(camera.EncodeLastFrame("png"));
    }

    [Fact]
    public void Capture_before_open_returns_null()
    {
        using var camera = NewCamera(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = "sim",
            ["autoOpen"] = "false",
        });

        Assert.Null(camera.TriggerCapture("demo"));
    }

    [Theory]
    [InlineData("hik")]
    [InlineData("daheng")]
    [InlineData("huaray")]
    [InlineData("mindvision")]
    [InlineData("basler")]
    [InlineData("flir")]
    [InlineData("tis")]
    public void Vendor_backend_without_sdk_falls_back_to_sim(string backend)
    {
        using var camera = NewCamera(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = backend,
            ["width"] = "128",
            ["height"] = "128",
        });

        Assert.True(camera.Open());
        Assert.Equal("sim", camera.EffectiveKind.Type);
        Assert.False(string.IsNullOrWhiteSpace(camera.LastError));
        Assert.NotNull(camera.TriggerCapture("demo"));
    }

    [Fact]
    public void Vendor_backend_without_sdk_and_without_fallback_stays_closed()
    {
        using var camera = NewCamera(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = "hik",
            ["fallbackToSim"] = "false",
        });

        Assert.False(camera.Open());
        Assert.False(camera.IsOpen);
        Assert.False(string.IsNullOrWhiteSpace(camera.LastError));
    }

    [Fact]
    public void File_backend_replays_folder_and_publishes_image_path()
    {
        var source = Path.Combine(Path.GetTempPath(), "mdkoss-extcam-src-" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(Path.GetTempPath(), "mdkoss-extcam-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            using (var seed = new Mat(48, 64, MatType.CV_8UC3, new Scalar(10, 20, 30)))
            {
                Cv2.ImWrite(Path.Combine(source, "frame-01.png"), seed);
            }

            var vars = new MVarStore();
            using var camera = NewCamera(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["backend"] = "file",
                    ["sourcePath"] = source,
                    ["saveDir"] = save,
                    ["saveFormat"] = "png",
                },
                vars);

            Assert.True(camera.Open());
            var result = camera.TriggerCapture("replay");
            Assert.NotNull(result);
            Assert.Equal(64, result!.Width);
            Assert.Equal(48, result.Height);
            Assert.True(File.Exists(result.ImagePath));

            Assert.True(vars.TryGet<object>($"device.{camera.Name}.{camera.Id}.lastImagePath", out var published));
            Assert.Equal(result.ImagePath, Convert.ToString(published));
        }
        finally
        {
            Directory.Delete(source, true);
            if (Directory.Exists(save))
            {
                Directory.Delete(save, true);
            }
        }
    }

    [Theory]
    [InlineData("16uc3", 3)]
    [InlineData("8uc4", 3)]
    [InlineData("16uc1", 1)]
    public void File_backend_normalizes_depth_and_alpha(string matType, int expectedChannels)
    {
        var type = matType switch
        {
            "16uc3" => MatType.CV_16UC3,
            "8uc4" => MatType.CV_8UC4,
            _ => MatType.CV_16UC1,
        };

        var source = Path.Combine(Path.GetTempPath(), "mdkoss-extcam-fmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            using (var seed = new Mat(16, 24, type, Scalar.All(1000)))
            {
                Cv2.ImWrite(Path.Combine(source, "frame.png"), seed);
            }

            using var camera = NewCamera(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["backend"] = "file",
                ["sourcePath"] = source,
                ["fallbackToSim"] = "false",
            });

            Assert.True(camera.Open());
            var result = camera.TriggerCapture("fmt");
            Assert.NotNull(result);
            Assert.Equal(expectedChannels == 1 ? "mono8" : "bgr8", result!.PixelFormat);
            Assert.Equal(24 * 16 * expectedChannels, result.Bytes);
            Assert.NotEmpty(camera.EncodeLastFrame("png"));
        }
        finally
        {
            Directory.Delete(source, true);
        }
    }

    [Fact]
    public void File_backend_without_source_falls_back_to_sim()
    {
        using var camera = NewCamera(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["backend"] = "file",
        });

        Assert.True(camera.Open());
        Assert.Equal("sim", camera.EffectiveKind.Type);
        Assert.Contains("source_path", camera.LastError, StringComparison.OrdinalIgnoreCase);
    }

    private static ExtCameraDevice NewCamera(Dictionary<string, string> parameters, MVarStore? vars = null) =>
        new("cam-test", "Test Camera", ExtCameraDeviceParameters.ParseConfig(parameters), vars ?? new MVarStore());
}
