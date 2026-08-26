using System.Text.Json;
using System.Text.Json.Serialization;
using MDKOSS.Core.Data;

namespace MDKOSS.Core;

/// <summary>
/// Cloud / local monitor row for one runtime instance.
/// Maps to public MySQL table <c>machine</c> (see scripts/mdkossdb/schema_machine.sql).
/// </summary>
public sealed class MachineMonitorRecord
{
    public const string TableName = "machine";

    /// <summary>Parameterized upsert used by the cloud-machine heartbeat task.</summary>
    public const string UpsertSql = """
        INSERT INTO machine (
            id, name, version, machine_type, is_running, machine_state, machine_message,
            monitoring_prefix, host_name,
            setting_json, vars_json, recipe_json, orders_json,
            drivers_json, devices_json, tasks_json, alarms_json, snapshot_json,
            last_heartbeat_utc
        ) VALUES (
            @id, @name, @version, @machine_type, @is_running, @machine_state, @machine_message,
            @monitoring_prefix, @host_name,
            CAST(@setting_json AS JSON), CAST(@vars_json AS JSON), CAST(@recipe_json AS JSON),
            CAST(@orders_json AS JSON), CAST(@drivers_json AS JSON), CAST(@devices_json AS JSON),
            CAST(@tasks_json AS JSON), CAST(@alarms_json AS JSON), CAST(@snapshot_json AS JSON),
            @last_heartbeat_utc
        ) AS new
        ON DUPLICATE KEY UPDATE
            name = new.name,
            version = new.version,
            machine_type = new.machine_type,
            is_running = new.is_running,
            machine_state = new.machine_state,
            machine_message = new.machine_message,
            monitoring_prefix = new.monitoring_prefix,
            host_name = new.host_name,
            setting_json = new.setting_json,
            vars_json = new.vars_json,
            recipe_json = new.recipe_json,
            orders_json = new.orders_json,
            drivers_json = new.drivers_json,
            devices_json = new.devices_json,
            tasks_json = new.tasks_json,
            alarms_json = new.alarms_json,
            snapshot_json = new.snapshot_json,
            last_heartbeat_utc = new.last_heartbeat_utc
        """;

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string MachineType { get; init; } = string.Empty;

    public bool IsRunning { get; init; }

    public string MachineState { get; init; } = string.Empty;

    public string? MachineMessage { get; init; }

    public string? MonitoringPrefix { get; init; }

    public string? HostName { get; init; }

    public object? Setting { get; init; }

    public IReadOnlyDictionary<string, object?> Vars { get; init; } =
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    public object? Recipe { get; init; }

    public object? Orders { get; init; }

    public object? Drivers { get; init; }

    public object? Devices { get; init; }

    public object? Tasks { get; init; }

    public object? Alarms { get; init; }

    public DateTime LastHeartbeatUtc { get; init; }

    public IReadOnlyDictionary<string, object?> ToUpsertParameters()
    {
        var json = MachineMonitor.JsonOptions;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = Id,
            ["name"] = Name,
            ["version"] = Version,
            ["machine_type"] = MachineType,
            ["is_running"] = IsRunning ? 1 : 0,
            ["machine_state"] = MachineState,
            ["machine_message"] = MachineMessage ?? (object)DBNull.Value,
            ["monitoring_prefix"] = MonitoringPrefix ?? (object)DBNull.Value,
            ["host_name"] = HostName ?? (object)DBNull.Value,
            ["setting_json"] = JsonSerializer.Serialize(Setting, json),
            ["vars_json"] = JsonSerializer.Serialize(Vars, json),
            ["recipe_json"] = JsonSerializer.Serialize(Recipe, json),
            ["orders_json"] = JsonSerializer.Serialize(Orders, json),
            ["drivers_json"] = JsonSerializer.Serialize(Drivers, json),
            ["devices_json"] = JsonSerializer.Serialize(Devices, json),
            ["tasks_json"] = JsonSerializer.Serialize(Tasks, json),
            ["alarms_json"] = JsonSerializer.Serialize(Alarms, json),
            ["snapshot_json"] = JsonSerializer.Serialize(this, json),
            ["last_heartbeat_utc"] = LastHeartbeatUtc.ToString("yyyy-MM-dd HH:mm:ss"),
        };
    }
}

/// <summary>Builds <see cref="MachineMonitorRecord"/> from a live <see cref="MdkRuntime"/>.</summary>
public static class MachineMonitor
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public static string ResolveId(MdkSetting setting, string? hostName = null)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (!string.IsNullOrWhiteSpace(setting.MachineId))
        {
            return TrimId(setting.MachineId);
        }

        var host = string.IsNullOrWhiteSpace(hostName) ? Environment.MachineName : hostName.Trim();
        var project = string.IsNullOrWhiteSpace(setting.ProjectName) ? "MDKOSS" : setting.ProjectName.Trim();
        return TrimId($"{host}:{project}");
    }

    public static string ResolveType(MdkSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        return string.IsNullOrWhiteSpace(setting.MachineType)
            ? string.Empty
            : setting.MachineType.Trim();
    }

    public static MachineMonitorRecord Build(MdkRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var setting = runtime.Setting;
        var snap = runtime.GetSnapshot();
        var hostName = Environment.MachineName;
        var vars = snap.Vars;
        var recipeKeys = runtime.RecipeManager.RecipeVarKeys;
        var currentRecipeVars = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in recipeKeys)
        {
            if (vars.TryGetValue(key, out var value))
            {
                currentRecipeVars[key] = value;
            }
        }

        vars.TryGetValue("machine.state", out var stateRaw);
        vars.TryGetValue("machine.message", out var messageRaw);

        return new MachineMonitorRecord
        {
            Id = ResolveId(setting, hostName),
            Name = string.IsNullOrWhiteSpace(setting.ProjectName) ? "MDKOSS" : setting.ProjectName.Trim(),
            Version = string.IsNullOrWhiteSpace(snap.Version) ? MdkProduct.Version : snap.Version,
            MachineType = ResolveType(setting),
            IsRunning = snap.IsRunning,
            MachineState = stateRaw?.ToString() ?? string.Empty,
            MachineMessage = messageRaw?.ToString(),
            MonitoringPrefix = runtime.MonitoringPrefix,
            HostName = hostName,
            Setting = SanitizeSetting(setting),
            Vars = vars,
            Recipe = new
            {
                snapshot = runtime.GetRecipeSnapshot(),
                currentVars = currentRecipeVars,
                recipes = setting.Recipes,
                persisted = SafeListRecipes(runtime.DataStore),
            },
            Orders = SafeListOrders(runtime.DataStore),
            Drivers = snap.Drivers,
            Devices = snap.Devices,
            Tasks = runtime.GetTaskSnapshots(),
            Alarms = new
            {
                active = runtime.AlarmManager.GetActive(),
                catalog = setting.Alarms,
            },
            LastHeartbeatUtc = DateTime.UtcNow,
        };
    }

    private static string TrimId(string raw)
    {
        var trimmed = raw.Trim();
        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }

    private static IReadOnlyList<ProductionOrderRecord> SafeListOrders(MdkDataStore store)
    {
        try
        {
            return store.ListOrders();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Machine monitor orders skipped: {ex.Message}");
            return [];
        }
    }

    private static IReadOnlyList<RecipeRecord> SafeListRecipes(MdkDataStore store)
    {
        try
        {
            return store.ListRecipes();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Machine monitor recipes skipped: {ex.Message}");
            return [];
        }
    }

    private static object SanitizeSetting(MdkSetting setting)
    {
        return new
        {
            setting.ProjectName,
            setting.MachineId,
            setting.MachineType,
            setting.CycleMs,
            setting.MonitoringPrefix,
            setting.StartPage,
            setting.ActiveRecipeId,
            setting.ActiveVisionId,
            setting.RecipeVarKeys,
            drivers = setting.Drivers.Select(RedactDriver).ToList(),
            devices = setting.Devices.Select(RedactDevice).ToList(),
            axes = setting.Axes.Select(RedactDevice).ToList(),
            platforms = setting.Platforms.Select(RedactDevice).ToList(),
            tasks = setting.Tasks,
            vars = setting.Vars,
            recipes = setting.Recipes,
            visions = setting.Visions.Select(v => new
            {
                v.Id,
                v.Name,
                v.Description,
                v.CameraDeviceId,
            }).ToList(),
            alarms = setting.Alarms,
        };
    }

    private static object RedactDriver(MdkSetting.DriverConfig d) => new
    {
        d.Id,
        d.Name,
        d.Type,
        d.Enabled,
        parameters = RedactParameters(d.Parameters),
    };

    private static object RedactDevice(MdkSetting.DeviceConfig d) => new
    {
        d.Id,
        d.Name,
        d.Type,
        d.DriverId,
        d.Enabled,
        parameters = RedactParameters(d.Parameters),
    };

    public static Dictionary<string, string> RedactParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (parameters is null)
        {
            return copy;
        }

        foreach (var (key, value) in parameters)
        {
            copy[key] = IsSecretKey(key) ? "***" : value;
        }

        return copy;
    }

    private static bool IsSecretKey(string key) =>
        key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase);
}
