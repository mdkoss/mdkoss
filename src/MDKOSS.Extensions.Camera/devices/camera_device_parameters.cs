using System.Globalization;

namespace MDKOSS.Extensions.Camera;

/// <summary>Parsed parameters for <see cref="ExtCameraDevice"/>.</summary>
public sealed class ExtCameraDeviceParameters
{
    /// <summary>Catalog key (<c>hik</c> / <c>daheng</c> / <c>basler</c> / <c>sim</c> …) or one of its aliases.</summary>
    public string Backend { get; init; } = "sim";

    /// <summary>Zero-based position in the SDK's enumeration order.</summary>
    public int DeviceIndex { get; init; }

    /// <summary>Preferred over <see cref="DeviceIndex"/> when set; MindVision / TIS treat it as the friendly name.</summary>
    public string SerialNumber { get; init; } = "";

    public string IpAddress { get; init; } = "";

    /// <summary>Overrides the catalog's runtime DLL name (different SDK versions ship different files).</summary>
    public string NativeDll { get; init; } = "";

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public double ExposureUs { get; init; } = 10_000;

    public double Gain { get; init; }

    public CameraTriggerMode TriggerMode { get; init; } = CameraTriggerMode.Continuous;

    /// <summary>GenICam enum symbol (<c>Mono8</c> / <c>BGR8</c> / <c>BayerRG8</c> …); empty keeps the camera's setting.</summary>
    public string PixelFormatName { get; init; } = "";

    public int TimeoutMs { get; init; } = 2000;

    /// <summary>Opens the camera when the runtime starts the device.</summary>
    public bool AutoOpen { get; init; } = true;

    /// <summary>Falls back to the simulator when the vendor SDK or camera is unavailable.</summary>
    public bool FallbackToSim { get; init; } = true;

    /// <summary>Image file or folder for the <c>file</c> backend; stream URL for the <c>uvc</c> backend.</summary>
    public string SourcePath { get; init; } = "";

    /// <summary>When set, each capture is written here and published as <c>lastImagePath</c> for vision devices.</summary>
    public string SaveDir { get; init; } = "";

    public string SaveFormat { get; init; } = "png";

    /// <summary>Simulator only: jitter of the synthetic target, in pixels.</summary>
    public double NoisePx { get; init; } = 0.5;

    /// <summary>Resolved catalog entry; unknown keys degrade to <see cref="CameraCatalog.Sim"/>.</summary>
    public CameraKind Kind => CameraCatalog.Resolve(Backend);

    /// <summary>Exposure in milliseconds, kept for status payloads and older configs.</summary>
    public int ExposureMs => (int)Math.Round(ExposureUs / 1000.0);

    public static ExtCameraDeviceParameters ParseConfig(IReadOnlyDictionary<string, string>? parameters)
    {
        parameters ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exposureUs = ReadDouble(parameters, "exposureUs", 0);
        if (exposureUs <= 0)
        {
            exposureUs = ReadDouble(parameters, "exposureMs", 10) * 1000.0;
        }

        return new ExtCameraDeviceParameters
        {
            Backend = ReadString(parameters, "backend", "sim"),
            DeviceIndex = Math.Max(0, ReadInt(parameters, "deviceIndex", 0)),
            SerialNumber = ReadString(parameters, "serialNumber", ""),
            IpAddress = ReadString(parameters, "ip", ""),
            NativeDll = ReadString(parameters, "nativeDll", ""),
            Width = Math.Max(0, ReadInt(parameters, "width", 1280)),
            Height = Math.Max(0, ReadInt(parameters, "height", 720)),
            ExposureUs = Math.Max(0, exposureUs),
            Gain = Math.Max(0, ReadDouble(parameters, "gain", 0)),
            TriggerMode = ParseTrigger(ReadString(parameters, "triggerMode", "continuous")),
            PixelFormatName = ReadString(parameters, "pixelFormat", ""),
            TimeoutMs = Math.Max(1, ReadInt(parameters, "timeoutMs", 2000)),
            AutoOpen = ReadBool(parameters, "autoOpen", true),
            FallbackToSim = ReadBool(parameters, "fallbackToSim", true),
            SourcePath = ReadString(parameters, "sourcePath", ""),
            SaveDir = ReadString(parameters, "saveDir", ""),
            SaveFormat = ReadString(parameters, "saveFormat", "png"),
            NoisePx = Math.Max(0, ReadDouble(parameters, "noisePx", 0.5)),
        };
    }

    /// <summary>Copy with a different backend — used when a live camera degrades to the simulator.</summary>
    public ExtCameraDeviceParameters WithBackend(string backend) => new()
    {
        Backend = backend,
        DeviceIndex = DeviceIndex,
        SerialNumber = SerialNumber,
        IpAddress = IpAddress,
        NativeDll = NativeDll,
        Width = Width,
        Height = Height,
        ExposureUs = ExposureUs,
        Gain = Gain,
        TriggerMode = TriggerMode,
        PixelFormatName = PixelFormatName,
        TimeoutMs = TimeoutMs,
        AutoOpen = AutoOpen,
        FallbackToSim = FallbackToSim,
        SourcePath = SourcePath,
        SaveDir = SaveDir,
        SaveFormat = SaveFormat,
        NoisePx = NoisePx,
    };

    public static CameraTriggerMode ParseTrigger(string? raw) => (raw ?? "").Trim().ToLowerInvariant() switch
    {
        "software" or "soft" or "sw" or "软触发" => CameraTriggerMode.Software,
        "hardware" or "hard" or "line" or "external" or "硬触发" => CameraTriggerMode.Hardware,
        _ => CameraTriggerMode.Continuous,
    };

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

    private static bool ReadBool(IReadOnlyDictionary<string, string> parameters, string key, bool fallback)
    {
        if (!parameters.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" or "on" => true,
            "0" or "false" or "no" or "n" or "off" => false,
            _ => fallback,
        };
    }
}
