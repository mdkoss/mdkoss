using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDKOSS.Core;

/// <summary>
/// Minimal runtime setting model loaded from JSON.
/// </summary>
public sealed class MdkSetting
{
    /// <summary>Project display name (distinguishes machine/project identity).</summary>
    public string ProjectName { get; set; } = "MDKOSS";

    /// <summary>Main loop cycle hint in milliseconds.</summary>
    public int CycleMs { get; set; } = 20;

    /// <summary>
    /// Optional HTTP prefix for the monitoring UI (e.g. <c>http://127.0.0.1:5081/</c>).
    /// When unset, the runtime uses its built-in default.
    /// </summary>
    public string? MonitoringPrefix { get; set; }

    /// <summary>
    /// CEF / monitor home page under <see cref="MonitoringPrefix"/> (e.g. <c>indexDieBonder.html</c>).
    /// When unset or blank, hosts fall back to <c>index.html</c>.
    /// </summary>
    public string? StartPage { get; set; }

    public List<DriverConfig> Drivers { get; set; } = [];

    public List<TaskConfig> Tasks { get; set; } = [];

    /// <summary>General devices (gpio/vio/camera/…); excludes <see cref="Axes"/> and <see cref="Platforms"/>.</summary>
    public List<DeviceConfig> Devices { get; set; } = [];

    /// <summary>Axis devices (<c>type=axis</c>), stored as top-level JSON <c>axes</c>.</summary>
    public List<DeviceConfig> Axes { get; set; } = [];

    /// <summary>Platform devices (platform / xy / xyz / …), stored as top-level JSON <c>platforms</c>.</summary>
    public List<DeviceConfig> Platforms { get; set; } = [];

    /// <summary>Seed variables loaded at startup.</summary>
    public Dictionary<string, object?> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Subset of <see cref="Vars"/> keys managed by <see cref="Recipes"/>.
    /// When empty, keys are inferred from all recipe entries.
    /// </summary>
    public List<string> RecipeVarKeys { get; set; } = [];

    /// <summary>Recipe applied automatically during runtime bootstrap.</summary>
    public string? ActiveRecipeId { get; set; }

    /// <summary>Named presets for <see cref="RecipeVarKeys"/>.</summary>
    public List<RecipeConfig> Recipes { get; set; } = [];

    /// <summary>
    /// SQLite database path. When unset, defaults to <c>data/mdk.db</c> under
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public string? DatabasePath { get; set; }

    /// <summary>Default SQLite path next to the app: <c>data/mdk.db</c>.</summary>
    public static string DefaultDatabasePath =>
        Path.Combine(AppContext.BaseDirectory, "data", "mdk.db");

    /// <summary>Default settings file next to the app: <c>configs/sample.setting.json</c>.</summary>
    public static string DefaultSettingsPath => Path.Combine(AppContext.BaseDirectory, "configs", "sample.setting.json");

    private static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>All device-like configs in bootstrap order: devices → axes → platforms.</summary>
    [JsonIgnore]
    public IEnumerable<DeviceConfig> AllDeviceConfigs =>
        Devices.Concat(Axes).Concat(Platforms);

    /// <summary>Loads setting from a JSON file path.</summary>
    public static MdkSetting Load(string path)
    {
        var json = File.ReadAllText(path);
        var setting = JsonSerializer.Deserialize<MdkSetting>(json, JsonOptions) ?? new MdkSetting();
        setting.NormalizeSections();
        return setting;
    }

    /// <summary>Persists setting to a JSON file path.</summary>
    public void Save(string path)
    {
        NormalizeSections();
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Ensures axis/platform entries live under <see cref="Axes"/> / <see cref="Platforms"/>,
    /// not under <see cref="Devices"/> (migrates legacy JSON that nested them in devices).
    /// </summary>
    public void NormalizeSections()
    {
        Axes ??= [];
        Platforms ??= [];
        Devices ??= [];

        var moveAxis = Devices
            .Where(d => string.Equals(d.Type, "axis", StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var d in moveAxis)
        {
            Devices.Remove(d);
            if (!Axes.Any(a => string.Equals(a.Id, d.Id, StringComparison.OrdinalIgnoreCase)))
            {
                d.Type = "axis";
                Axes.Add(d);
            }
        }

        var movePlat = Devices
            .Where(d => PlatformDeviceParameterSet.IsPlatformFamilyType((d.Type ?? "").Trim().ToLowerInvariant()))
            .ToList();
        foreach (var d in movePlat)
        {
            Devices.Remove(d);
            if (!Platforms.Any(p => string.Equals(p.Id, d.Id, StringComparison.OrdinalIgnoreCase)))
            {
                Platforms.Add(d);
            }
        }

        // Drop accidental axis/platform duplicates left inside Devices after merge.
        Devices.RemoveAll(d =>
            string.Equals(d.Type, "axis", StringComparison.OrdinalIgnoreCase)
            || PlatformDeviceParameterSet.IsPlatformFamilyType((d.Type ?? "").Trim().ToLowerInvariant()));
    }

    /// <summary>Driver registration config.</summary>
    public sealed class DriverConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "gts";
        public bool Enabled { get; set; } = true;
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Task registration config.</summary>
    public sealed class TaskConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "pollDriver";
        public string DriverId { get; set; } = string.Empty;
        public int IntervalMs { get; set; } = 100;
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Device / axis / platform registration config.</summary>
    public sealed class DeviceConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "gpio";
        public string DriverId { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Named values for the recipe-scoped subset of <see cref="Vars"/>.</summary>
    public sealed class RecipeConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Dictionary<string, object?> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
