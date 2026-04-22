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

    public List<DriverConfig> Drivers { get; set; } = [];

    public List<TaskConfig> Tasks { get; set; } = [];

    public List<DeviceConfig> Devices { get; set; } = [];

    /// <summary>Seed variables loaded at startup.</summary>
    public Dictionary<string, object?> Vars { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Loads setting from a JSON file path.</summary>
    public static MdkSetting Load(string path)
    {
        var json = File.ReadAllText(path);
        var setting = JsonSerializer.Deserialize<MdkSetting>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return setting ?? new MdkSetting();
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
}
