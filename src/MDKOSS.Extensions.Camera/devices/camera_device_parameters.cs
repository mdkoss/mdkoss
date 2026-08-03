using System.Globalization;

namespace MDKOSS.Extensions.Camera;

/// <summary>Parsed parameters for <see cref="ExtCameraDevice"/>.</summary>
public sealed class ExtCameraDeviceParameters
{
    public string Backend { get; init; } = "sim";

    public int DeviceIndex { get; init; }

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public int ExposureMs { get; init; } = 10;

    public double NoisePx { get; init; } = 0.5;

    public static ExtCameraDeviceParameters ParseConfig(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return new ExtCameraDeviceParameters
        {
            Backend = ReadString(parameters, "backend", "sim"),
            DeviceIndex = ReadInt(parameters, "deviceIndex", 0),
            Width = Math.Max(1, ReadInt(parameters, "width", 1280)),
            Height = Math.Max(1, ReadInt(parameters, "height", 720)),
            ExposureMs = Math.Max(0, ReadInt(parameters, "exposureMs", 10)),
            NoisePx = Math.Max(0, ReadDouble(parameters, "noisePx", 0.5)),
        };
    }

    private static string ReadString(IReadOnlyDictionary<string, string> parameters, string key, string fallback)
    {
        return parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? raw.Trim()
            : fallback;
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static double ReadDouble(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }
}
