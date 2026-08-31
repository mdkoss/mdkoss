using System.Runtime.InteropServices;
using OpenCvSharp;

namespace MDKOSS.Extensions.Camera;

/// <summary>Shared helpers for the backends that produce frames through OpenCV.</summary>
internal static class MatFrame
{
    /// <summary>Copies a Mat into a PFNC-tagged frame (1 channel → mono8, otherwise bgr8).</summary>
    public static CameraFrame FromMat(Mat mat, long frameId)
    {
        using var normalized = Normalize(mat);
        var source = normalized.IsContinuous() ? normalized : normalized.Clone();
        try
        {
            var format = source.Channels() == 1 ? CameraPixel.Mono8 : CameraPixel.Bgr8;
            var bytes = new byte[source.Total() * source.ElemSize()];
            Marshal.Copy(source.Data, bytes, 0, bytes.Length);
            return new CameraFrame(source.Width, source.Height, format, bytes, frameId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        finally
        {
            if (!ReferenceEquals(source, normalized))
            {
                source.Dispose();
            }
        }
    }

    /// <summary>Forces 8-bit depth and 1 or 3 channels — files on disk may be 16-bit or carry alpha.</summary>
    private static Mat Normalize(Mat mat)
    {
        var stage = mat;
        var owned = false;

        if (mat.Depth() != MatType.CV_8U)
        {
            var scale = mat.Depth() == MatType.CV_16U ? 1.0 / 256.0 : 1.0;
            var eightBit = new Mat();
            mat.ConvertTo(eightBit, MatType.MakeType(MatType.CV_8U, mat.Channels()), scale);
            stage = eightBit;
            owned = true;
        }

        if (stage.Channels() is 1 or 3)
        {
            return owned ? stage : stage.Clone();
        }

        var reduced = new Mat();
        if (stage.Channels() == 4)
        {
            Cv2.CvtColor(stage, reduced, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            Cv2.ExtractChannel(stage, reduced, 0);
        }

        if (owned)
        {
            stage.Dispose();
        }

        return reduced;
    }
}

/// <summary>Software camera — synthetic frames so demos and CI run without any hardware.</summary>
internal sealed class SimCameraBackend : CameraBackend
{
    private readonly Random _random = new();
    private ExtCameraDeviceParameters? _parameters;
    private long _frameId;

    public override string Vendor => "内置仿真";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        error = "";
        lock (Gate)
        {
            _parameters = parameters;
            return true;
        }
    }

    public override void Close()
    {
        lock (Gate)
        {
            _parameters = null;
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate() =>
        [new CameraDeviceInfo(0, "Simulated Area-Scan", "SIM-0", Vendor, "software")];

    public override bool TrySetExposure(double microseconds) => true;

    public override bool TrySetGain(double gain) => true;

    public override bool TrySetTrigger(CameraTriggerMode mode) => true;

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        frame = null;
        error = "";
        lock (Gate)
        {
            if (_parameters is null)
            {
                error = "not_open";
                return false;
            }

            var width = Math.Max(32, _parameters.Width);
            var height = Math.Max(32, _parameters.Height);
            var jitter = _parameters.NoisePx;
            using var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(20, 20, 20));
            var cx = (int)Math.Round(width / 2.0 + Noise(jitter));
            var cy = (int)Math.Round(height / 2.0 + Noise(jitter));
            var radius = Math.Max(8, Math.Min(width, height) / 8);
            Cv2.Circle(mat, new Point(cx, cy), radius, new Scalar(240, 240, 240), -1);
            Cv2.Circle(mat, new Point(cx, cy), Math.Max(2, radius / 4), new Scalar(40, 40, 40), -1);
            frame = MatFrame.FromMat(mat, ++_frameId);
            return true;
        }
    }

    private double Noise(double amplitude) =>
        amplitude <= 0 ? 0 : (_random.NextDouble() * 2 - 1) * amplitude;
}

/// <summary>
/// Replays images from a file or folder (<c>sourcePath</c>) — lets recipes and vision pipelines
/// be tuned offline against captures taken on the line.
/// </summary>
internal sealed class FileCameraBackend : CameraBackend
{
    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".tif", ".tiff"];

    private string[] _files = [];
    private int _cursor;
    private long _frameId;

    public override string Vendor => "本地图像回放";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        error = "";
        lock (Gate)
        {
            var path = parameters.SourcePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "source_path_empty";
                return false;
            }

            if (File.Exists(path))
            {
                _files = [path];
            }
            else if (Directory.Exists(path))
            {
                _files = Directory
                    .EnumerateFiles(path)
                    .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            else
            {
                error = "source_path_not_found:" + path;
                return false;
            }

            if (_files.Length == 0)
            {
                error = "source_path_empty_folder:" + path;
                return false;
            }

            _cursor = 0;
            return true;
        }
    }

    public override void Close()
    {
        lock (Gate)
        {
            _files = [];
            _cursor = 0;
        }
    }

    public override IReadOnlyList<CameraDeviceInfo> Enumerate()
    {
        lock (Gate)
        {
            return _files
                .Select((f, i) => new CameraDeviceInfo(i, Path.GetFileName(f), "", Vendor, "file"))
                .ToArray();
        }
    }

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        frame = null;
        error = "";
        lock (Gate)
        {
            if (_files.Length == 0)
            {
                error = "not_open";
                return false;
            }

            var path = _files[_cursor % _files.Length];
            _cursor = (_cursor + 1) % _files.Length;
            using var mat = Cv2.ImRead(path, ImreadModes.Unchanged);
            if (mat.Empty())
            {
                error = "image_decode_failed:" + path;
                return false;
            }

            frame = MatFrame.FromMat(mat, ++_frameId);
            return true;
        }
    }
}

/// <summary>
/// UVC / DirectShow cameras through OpenCV — covers generic USB cameras and any source
/// OpenCV can open by URL (<c>sourcePath</c>, e.g. an RTSP or GigE stream).
/// </summary>
internal sealed class OpenCvCameraBackend : CameraBackend
{
    private VideoCapture? _capture;
    private long _frameId;

    public override string Vendor => "通用 USB 相机";

    public override bool TryOpen(ExtCameraDeviceParameters parameters, out string error)
    {
        error = "";
        lock (Gate)
        {
            Close();
            try
            {
                _capture = string.IsNullOrWhiteSpace(parameters.SourcePath)
                    ? new VideoCapture(parameters.DeviceIndex, VideoCaptureAPIs.DSHOW)
                    : new VideoCapture(parameters.SourcePath);

                if (!_capture.IsOpened())
                {
                    error = "capture_open_failed";
                    Close();
                    return false;
                }

                if (parameters.Width > 0)
                {
                    _capture.Set(VideoCaptureProperties.FrameWidth, parameters.Width);
                }

                if (parameters.Height > 0)
                {
                    _capture.Set(VideoCaptureProperties.FrameHeight, parameters.Height);
                }

                TrySetExposure(parameters.ExposureUs);
                TrySetGain(parameters.Gain);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Close();
                return false;
            }
        }
    }

    public override void Close()
    {
        lock (Gate)
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }
    }

    public override bool TrySetExposure(double microseconds)
    {
        lock (Gate)
        {
            if (_capture is null || microseconds <= 0)
            {
                return false;
            }

            // UVC exposure is log2(seconds); drivers ignore the write when auto-exposure is on.
            _capture.Set(VideoCaptureProperties.AutoExposure, 0.25);
            return _capture.Set(
                VideoCaptureProperties.Exposure,
                Math.Round(Math.Log2(Math.Max(microseconds, 1) / 1_000_000.0)));
        }
    }

    public override bool TrySetGain(double gain)
    {
        lock (Gate)
        {
            return _capture is not null && _capture.Set(VideoCaptureProperties.Gain, gain);
        }
    }

    public override bool TryGrab(int timeoutMs, out CameraFrame? frame, out string error)
    {
        frame = null;
        error = "";
        lock (Gate)
        {
            if (_capture is null)
            {
                error = "not_open";
                return false;
            }

            using var mat = new Mat();
            if (!_capture.Read(mat) || mat.Empty())
            {
                error = "grab_timeout";
                return false;
            }

            frame = MatFrame.FromMat(mat, ++_frameId);
            return true;
        }
    }
}
