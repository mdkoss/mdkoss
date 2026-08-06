namespace MDKOSS.Core;

/// <summary>
/// Default <c>parameters</c> templates by driver/device/task type for config UI seeding.
/// </summary>
public static class DeviceParameterPresets
{
    public static Dictionary<string, string> ForDriver(string? type) =>
        (type ?? "").Trim().ToLowerInvariant() switch
        {
            "sim" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["ip"] = "127.0.0.1",
                ["port"] = "5000",
                ["note"] = "VirtualCard / SIM",
            },
            "gts" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["card"] = "0",
                ["note"] = "GTS motion card",
            },
            "dmc" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["card"] = "0",
                ["note"] = "DMC motion card",
            },
            _ => new(StringComparer.OrdinalIgnoreCase) { ["key"] = "value" },
        };

    public static Dictionary<string, string> ForDevice(string? type, string? defaultDriverId = null)
    {
        var drv = string.IsNullOrWhiteSpace(defaultDriverId) ? "drv-m1" : defaultDriverId.Trim();
        return (type ?? "").Trim().ToLowerInvariant() switch
        {
            "gpio" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["in.startButton"] = "0|启动按钮",
                ["in.stopButton"] = "1|停止按钮",
                ["out.tower.green"] = "0|绿灯",
                ["out.tower.red"] = "1|红灯",
            },
            "vio" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["in.TestVio"] = "virtual|TestVio",
                ["out.TestVio"] = "virtual|TestVio",
            },
            "axis" => AxisDeviceParameterSet.DefaultParameters(),
            "platform" => PlatformDeviceParameterSet.DefaultParameters("xyz", drv),
            "xy" => PlatformDeviceParameterSet.DefaultParameters("xy", drv),
            "xyz" => PlatformDeviceParameterSet.DefaultParameters("xyz", drv),
            "xyzu" => PlatformDeviceParameterSet.DefaultParameters("xyzu", drv),
            "xyzuv" => PlatformDeviceParameterSet.DefaultParameters("xyzuv", drv),
            "xyzuvw" => PlatformDeviceParameterSet.DefaultParameters("xyzuvw", drv),
            "x" => PlatformDeviceParameterSet.DefaultParameters("x", drv),
            "cameradev" => new(StringComparer.OrdinalIgnoreCase) { ["role"] = "downlook" },
            "extcamera" => new(StringComparer.OrdinalIgnoreCase)
            {
                ["backend"] = "sim",
                ["deviceIndex"] = "0",
                ["width"] = "1280",
                ["height"] = "720",
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
            "operation" => new(StringComparer.OrdinalIgnoreCase) { ["gpioDeviceId"] = "gpio-main" },
            "cycle" => new(StringComparer.OrdinalIgnoreCase) { ["gpioDeviceId"] = "gpio-main" },
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
