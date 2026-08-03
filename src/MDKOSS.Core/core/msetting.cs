using System.Text.Json;

namespace MDKOSS.Core;

/// <summary>
/// Minimal runtime setting model loaded from JSON.
/// </summary>
public sealed class MdkSetting
{
    /// <summary>Project display name.</summary>
    public string ProjectName { get; set; } = "MDKOSS";

    /// <summary>Main loop cycle hint in milliseconds.</summary>
    public int CycleMs { get; set; } = 20;

    /// <summary>
    /// Optional HTTP prefix for the monitoring UI (e.g. <c>http://127.0.0.1:5081/</c>).
    /// When unset, the runtime uses its built-in default.
    /// </summary>
    public string? MonitoringPrefix { get; set; }

    public List<DriverConfig> Drivers { get; set; } = [];

    public List<TaskConfig> Tasks { get; set; } = [];

    public List<DeviceConfig> Devices { get; set; } = [];

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
        WriteIndented = true
    };

    /// <summary>Loads setting from a JSON file path.</summary>
    public static MdkSetting Load(string path)
    {
        var json = File.ReadAllText(path);
        var setting = JsonSerializer.Deserialize<MdkSetting>(json, JsonOptions);

        return setting ?? new MdkSetting();
    }

    /// <summary>Persists setting to a JSON file path.</summary>
    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(path, json);
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

    /// <summary>Device registration config.</summary>
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
