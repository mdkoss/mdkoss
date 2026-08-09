namespace MDKOSS.Core;

/// <summary>
/// Default <c>parameters</c> templates by driver/device/task type for config UI seeding.
/// </summary>
public static class DeviceParameterPresets
{
    public static Dictionary<string, string> ForDriver(string? type) =>
        DriverParameterPresets.ForType(type);

    public static Dictionary<string, string> ForDevice(string? type, string? defaultDriverId = null)
    {
        var drv = string.IsNullOrWhiteSpace(defaultDriverId) ? "drv-m1" : defaultDriverId.Trim();
        return (type ?? "").Trim().ToLowerInvariant() switch
        {
            "gpio" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["in.startButton"] = $"{drv}:0|启动按钮",
                ["in.stopButton"] = $"{drv}:1|停止按钮",
                ["out.tower.green"] = $"{drv}:0|绿灯",
                ["out.tower.red"] = $"{drv}:1|红灯",
            },
            "vio" => VioDeviceParameterSet.DefaultParameters(),
            "axis" => AxisDeviceParameterSet.DefaultParameters(MAxisKind.Linear),
            "linear" or "lin" or "直线" or "直线轴" =>
                AxisDeviceParameterSet.DefaultParameters(MAxisKind.Linear),
            "rotary" or "rot" or "rotate" or "旋转" or "旋转轴" =>
                AxisDeviceParameterSet.DefaultParameters(MAxisKind.Rotary),
            "platform" => PlatformDeviceParameterSet.DefaultParameters("xyz", drv),
            "xy" => PlatformDeviceParameterSet.DefaultParameters("xy", drv),
            "xyz" => PlatformDeviceParameterSet.DefaultParameters("xyz", drv),
            "xyzu" => PlatformDeviceParameterSet.DefaultParameters("xyzu", drv),
            "xyzuv" => PlatformDeviceParameterSet.DefaultParameters("xyzuv", drv),
            "xyzuvw" => PlatformDeviceParameterSet.DefaultParameters("xyzuvw", drv),
            "x" => PlatformDeviceParameterSet.DefaultParameters("x", drv),
            // Platforms bind Axis device ids via axis.X …; DefaultParameters ignores drv for cameradev.
            "cameradev" => new(StringComparer.OrdinalIgnoreCase) { ["role"] = "downlook" },
            "extcamera" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["backend"] = "sim",
                ["deviceIndex"] = "0",
                ["width"] = "1280",
                ["height"] = "720",
            },
            "visiondev" or "vision" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["visionId"] = "vision-inspect",
                ["cameraDeviceId"] = "cam-top",
                ["resultPrefix"] = "vision",
                ["generateTestImage"] = "true",
            },
            "tray" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["role"] = "source",
                ["rows"] = "8",
                ["cols"] = "8",
            },
            _ => new(StringComparer.OrdinalIgnoreCase),
        };
    }

    public static Dictionary<string, string> ForTask(string? type) =>
        (type ?? "").Trim().ToLowerInvariant() switch
        {
            "polldriver" => new(StringComparer.OrdinalIgnoreCase) { ["varPrefix"] = "driver" },
            // gpioDeviceId optional: blank → runtime uses the first (shared) GpioDevice
            "operation" => new(StringComparer.OrdinalIgnoreCase),
            "cycle" => new(StringComparer.OrdinalIgnoreCase),
            "flow" => new(StringComparer.OrdinalIgnoreCase) { ["loop"] = "true", ["flowJson"] = "{}" },
            _ => new(StringComparer.OrdinalIgnoreCase),
        };

    /// <summary>
    /// Merge template keys into <paramref name="existing"/> without overwriting non-empty values
    /// when <paramref name="overwriteEmptyOnly"/> is true; when false, replace with a fresh template.
    /// </summary>
    public static Dictionary<string, string> ApplyTemplate(
        Dictionary<string, string>? existing,
        Dictionary<string, string> template,
        bool overwriteEmptyOnly)
    {
        if (!overwriteEmptyOnly || existing is null || existing.Count == 0)
        {
            return new Dictionary<string, string>(template, StringComparer.OrdinalIgnoreCase);
        }

        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in template)
        {
            if (!merged.TryGetValue(kv.Key, out var cur) || string.IsNullOrWhiteSpace(cur))
            {
                merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }
}
