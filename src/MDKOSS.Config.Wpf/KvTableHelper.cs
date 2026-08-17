using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using MDKOSS.Cef.Extensions;
using MDKOSS.Core;

namespace MDKOSS.Config.Wpf;

public sealed class KvPairRow : INotifyPropertyChanged
{
    private string _key = string.Empty;
    private string _value = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Key
    {
        get => _key;
        set { _key = value; OnPropertyChanged(); }
    }

    public string Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Known type / preset catalogs for ComboBox suggestions.</summary>
public static class ConfigTypeCatalog
{
    public static IReadOnlyList<string> DriverTypes { get; } =
        DriverParameterPresets.KnownTypes;

    /// <summary>Devices 模块可选类型（不含 axis / platform 族，请在对应模块编辑）。</summary>
    public static IReadOnlyList<string> DeviceTypes { get; } =
    [
        "gpio", "vio",
        "cameradev", "visiondev", "serialdev", "tcpdev", "mysqldev", "extcamera", "devpyscript", "devmodserver", "devmodclient", "tray",
    ];

    public static IReadOnlyList<string> TaskTypes { get; } =
        ["pollDriver", "operation", "machine", "cycle", "motion", "flow", "pnpCycle", "pnpConveyor"];

    public static IReadOnlyList<string> GpioDirections { get; } =
        ["in", "out"];

    public static IReadOnlyList<string> PlatformTypes { get; } =
        ["platform", "x", "xy", "xyz", "xyzu", "xyzuv", "xyzuvw"];

    /// <summary>Axis 模块类型：直线轴 / 旋转轴（兼容旧值 <c>axis</c>）。</summary>
    public static IReadOnlyList<string> AxisTypes { get; } =
        ["linear", "rotary", "axis"];

    public static IReadOnlyList<string> TypesForModule(ConfigModule module) => module switch
    {
        ConfigModule.Drivers => DriverTypes,
        ConfigModule.Devices => DeviceTypes,
        ConfigModule.Axis => AxisTypes,
        ConfigModule.Platform => PlatformTypes,
        ConfigModule.Tasks => TaskTypes,
        ConfigModule.Gpios => GpioDirections,
        ConfigModule.Vios => ["vio"],
        ConfigModule.Hmi => HmiWidgetCatalog.All.Select(w => w.Type).ToList(),
        _ => [],
    };

    public static string DefaultType(ConfigModule module) => module switch
    {
        ConfigModule.Drivers => "sim",
        ConfigModule.Devices => "gpio",
        ConfigModule.Axis => "linear",
        ConfigModule.Platform => "xyz",
        ConfigModule.Tasks => "pollDriver",
        ConfigModule.Gpios => "in",
        ConfigModule.Vios => "vio",
        ConfigModule.Hmi => "value",
        _ => "",
    };

    /// <summary>Default parameters for the given module + type (empty dict when unknown).</summary>
    public static Dictionary<string, string> DefaultParameters(ConfigModule module, string? type, string? driverId = null) =>
        module switch
        {
            ConfigModule.Drivers => DeviceParameterPresets.ForDriver(type),
            ConfigModule.Devices or ConfigModule.Axis or ConfigModule.Platform =>
                DeviceParameterPresets.ForDevice(type, driverId),
            ConfigModule.Tasks => DeviceParameterPresets.ForTask(type),
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
}

public static class KvTableHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void LoadStringDict(ObservableCollection<KvPairRow> rows, IReadOnlyDictionary<string, string> dict)
    {
        rows.Clear();
        foreach (var kv in dict.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new KvPairRow { Key = kv.Key, Value = kv.Value });
        }
    }

    public static void LoadObjectDict(ObservableCollection<KvPairRow> rows, IReadOnlyDictionary<string, object?> dict)
    {
        rows.Clear();
        foreach (var kv in dict.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new KvPairRow
            {
                Key = kv.Key,
                Value = kv.Value switch
                {
                    null => "",
                    JsonElement je => je.ToString(),
                    _ => Convert.ToString(kv.Value) ?? "",
                },
            });
        }
    }

    public static void LoadFromJsonObject(ObservableCollection<KvPairRow> rows, string json)
    {
        rows.Clear();
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var prop in doc.RootElement.EnumerateObject().OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                rows.Add(new KvPairRow
                {
                    Key = prop.Name,
                    Value = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? ""
                        : prop.Value.ToString(),
                });
            }
        }
        catch
        {
            // leave empty; caller may keep raw JSON
        }
    }

    public static Dictionary<string, string> ToStringDict(IEnumerable<KvPairRow> rows)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                continue;
            }

            dict[row.Key.Trim()] = row.Value ?? "";
        }

        return dict;
    }

    public static Dictionary<string, object?> ToObjectDict(IEnumerable<KvPairRow> rows)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                continue;
            }

            dict[row.Key.Trim()] = ParseScalar(row.Value);
        }

        return dict;
    }

    public static string ToJson(IEnumerable<KvPairRow> rows, bool asObjectValues = false)
    {
        if (asObjectValues)
        {
            return JsonSerializer.Serialize(ToObjectDict(rows), JsonOptions);
        }

        return JsonSerializer.Serialize(ToStringDict(rows), JsonOptions);
    }

    private static object? ParseScalar(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var t = raw.Trim();
        if (t.Length == 0)
        {
            return "";
        }

        if (bool.TryParse(t, out var b))
        {
            return b;
        }

        if (long.TryParse(t, out var l))
        {
            return l;
        }

        if (double.TryParse(t, out var d))
        {
            return d;
        }

        return raw;
    }
}
