using OpenCvSharp;

namespace MDKOSS.Extensions.Camera;

/// <summary>
/// GenICam PFNC pixel codes and the conversion to an 8-bit BGR/mono <see cref="Mat"/>.
/// HikRobot (GVSP), Daheng, Huaray, MindVision and Basler all report these same codes,
/// so one converter serves every vendor backend.
/// </summary>
public static class CameraPixel
{
    public const uint Mono8 = 0x01080001;
    public const uint Mono10 = 0x01100003;
    public const uint Mono12 = 0x01100005;
    public const uint Mono16 = 0x01100007;
    public const uint BayerGR8 = 0x01080008;
    public const uint BayerRG8 = 0x01080009;
    public const uint BayerGB8 = 0x0108000A;
    public const uint BayerBG8 = 0x0108000B;
    public const uint Rgb8 = 0x02180014;
    public const uint Bgr8 = 0x02180015;

    public static string Describe(uint code) => code switch
    {
        Mono8 => "mono8",
        Mono10 => "mono10",
        Mono12 => "mono12",
        Mono16 => "mono16",
        BayerGR8 => "bayerGR8",
        BayerRG8 => "bayerRG8",
        BayerGB8 => "bayerGB8",
        BayerBG8 => "bayerBG8",
        Rgb8 => "rgb8",
        Bgr8 => "bgr8",
        _ => "0x" + code.ToString("X8"),
    };

    /// <summary>Bytes one pixel occupies on the wire; 0 when the code is not supported.</summary>
    public static int BytesPerPixel(uint code) => code switch
    {
        Mono8 or BayerGR8 or BayerRG8 or BayerGB8 or BayerBG8 => 1,
        Mono10 or Mono12 or Mono16 => 2,
        Rgb8 or Bgr8 => 3,
        _ => 0,
    };

    public static bool IsSupported(uint code) => BytesPerPixel(code) > 0;

    /// <summary>
    /// Decodes a frame into an 8-bit <see cref="Mat"/> (mono stays 1 channel, colour becomes BGR).
    /// Caller owns the returned Mat.
    /// </summary>
    public static Mat ToMat(CameraFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var expected = (long)frame.Width * frame.Height * BytesPerPixel(frame.PixelFormat);
        if (frame.Width <= 0 || frame.Height <= 0 || expected <= 0 || frame.Data.Length < expected)
        {
            throw new InvalidOperationException(
                $"frame_size_mismatch:{CameraPixel.Describe(frame.PixelFormat)}:{frame.Data.Length}/{expected}");
        }

        switch (frame.PixelFormat)
        {
            case Mono8:
                return Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Data).Clone();
            case Bgr8:
                return Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC3, frame.Data).Clone();
            case Rgb8:
            {
                using var rgb = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC3, frame.Data);
                var bgr = new Mat();
                Cv2.CvtColor(rgb, bgr, ColorConversionCodes.RGB2BGR);
                return bgr;
            }
            case Mono10 or Mono12 or Mono16:
            {
                using var wide = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_16UC1, frame.Data);
                var mono = new Mat();
                var shift = frame.PixelFormat switch { Mono10 => 4.0, Mono12 => 16.0, _ => 256.0 };
                wide.ConvertTo(mono, MatType.CV_8UC1, 1.0 / shift);
                return mono;
            }
            default:
            {
                using var raw = Mat.FromPixelData(frame.Height, frame.Width, MatType.CV_8UC1, frame.Data);
                var bgr = new Mat();
                Cv2.CvtColor(raw, bgr, BayerCode(frame.PixelFormat));
                return bgr;
            }
        }
    }

    /// <summary>Encodes a frame for HTTP / disk. <paramref name="extension"/> is e.g. <c>.png</c> or <c>.jpg</c>.</summary>
    public static byte[] Encode(CameraFrame frame, string extension)
    {
        using var mat = ToMat(frame);
        var ext = NormalizeExtension(extension);
        return Cv2.ImEncode(ext, mat, out var buffer) ? buffer : [];
    }

    public static string NormalizeExtension(string? extension)
    {
        var ext = (extension ?? "").Trim().TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" => ".jpg",
            "bmp" => ".bmp",
            "tif" or "tiff" => ".tiff",
            _ => ".png",
        };
    }

    public static string ContentType(string extension) => NormalizeExtension(extension) switch
    {
        ".jpg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".tiff" => "image/tiff",
        _ => "image/png",
    };

    // OpenCV names a Bayer pattern after the 2x2 block starting at the second row/column,
    // which is one step off the GenICam naming — hence the crossed mapping below.
    private static ColorConversionCodes BayerCode(uint code) => code switch
    {
        BayerBG8 => ColorConversionCodes.BayerRG2BGR,
        BayerGB8 => ColorConversionCodes.BayerGR2BGR,
        BayerRG8 => ColorConversionCodes.BayerBG2BGR,
        BayerGR8 => ColorConversionCodes.BayerGB2BGR,
        _ => throw new InvalidOperationException("unsupported_pixel_format:" + Describe(code)),
    };
}
