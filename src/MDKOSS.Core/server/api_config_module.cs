using System.Net;
using System.Text.Json;
using MDKOSS.Core.Vision;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles /api/config — light-weight setting view/edit + persist.
/// Edits update <see cref="MdkSetting"/> only; restart runtime to apply to live devices/drivers.
/// </summary>
public sealed class ConfigApiModule : MonitoringApiModule
{
    private sealed class DriverPatch
    {
        public string? Name { get; set; }
        public bool? Enabled { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
        public string? Type { get; set; }
    }

    private sealed class DevicePatch
    {
        public string? Name { get; set; }
        public bool? Enabled { get; set; }
        public string? DriverId { get; set; }
        public string? Type { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
    }

    private sealed class TaskPatch
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? DriverId { get; set; }
        public int? IntervalMs { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
    }

    private sealed class AlarmPatch
    {
        public string? Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        public string? Level { get; set; }
        public bool? Enabled { get; set; }
        public string? VarKey { get; set; }
        public string? Op { get; set; }
        public string? Value { get; set; }
        public string? Message { get; set; }
        public string? Solution { get; set; }
        public string? Module { get; set; }
        public bool? Latch { get; set; }
    }

    private sealed class VisionPatch
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? CameraDeviceId { get; set; }
        public JsonElement? Pipeline { get; set; }
    }

    private sealed class VarPatch
    {
        public string? Key { get; set; }
        public JsonElement? Value { get; set; }
    }

    private sealed class MachinePatch
    {
        public string? ProjectName { get; set; }
        public string? MachineId { get; set; }
        public string? MachineType { get; set; }
        public int? CycleMs { get; set; }
        public string? MonitoringPrefix { get; set; }
        public string? StartPage { get; set; }
        public string? DatabasePath { get; set; }
        public string? ActiveRecipeId { get; set; }
        public string? ActiveVisionId { get; set; }
        public string? RecipeVarKeys { get; set; }
        public Dictionary<string, string>? Parameters { get; set; }
    }

    public ConfigApiModule(MdkRuntime runtime) : base(runtime) { }

    public override string RoutePrefix => "/api/config";

    public override async Task<bool> HandleAsync(
        HttpListenerContext context,
        string remainingPath,
        CancellationToken cancellationToken)
    {
        var path = remainingPath.Trim('/');
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var method = context.Request.HttpMethod ?? "GET";
        var isGet = method.Equals("GET", StringComparison.OrdinalIgnoreCase);
        var isPost = method.Equals("POST", StringComparison.OrdinalIgnoreCase);
        var isPatch = method.Equals("PATCH", StringComparison.OrdinalIgnoreCase)
                      || method.Equals("PUT", StringComparison.OrdinalIgnoreCase);

        try
        {
            if (segments.Length == 0 && isGet)
            {
                await WriteSummaryAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (segments.Length >= 1 && segments[0].Equals("catalog", StringComparison.OrdinalIgnoreCase) && isGet)
            {
                await WriteCatalogAsync(context, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (segments.Length == 1 && segments[0].Equals("save", StringComparison.OrdinalIgnoreCase) && isPost)
            {
                await HandleSaveAsync(context.Response, cancellationToken).ConfigureAwait(false);
                return true;
            }

            if (segments.Length >= 1 && segments[0].Equals("drivers", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        success = true,
                        drivers = Runtime.Setting.Drivers,
                    }, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 1 && isPost)
                {
                    await CreateDriverAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && isPatch)
                {
                    await PatchDriverAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    await DeleteDriverAsync(context.Response, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            if (segments.Length >= 1 && TryDeviceSection(segments[0], out var deviceList, out var deviceKey))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, new Dictionary<string, object?>
                    {
                        ["success"] = true,
                        [deviceKey] = deviceList,
                    }, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 1 && isPost)
                {
                    await CreateDeviceAsync(context, deviceList, deviceKey, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && isPatch)
                {
                    await PatchDeviceAsync(context, deviceList, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    await DeleteDeviceAsync(context.Response, deviceList, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            if (segments.Length >= 1 && segments[0].Equals("tasks", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        success = true,
                        tasks = Runtime.Setting.Tasks,
                    }, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 1 && isPost)
                {
                    await CreateTaskAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && isPatch)
                {
                    await PatchTaskAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    await DeleteTaskAsync(context.Response, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            if (segments.Length >= 1 && segments[0].Equals("alarms", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        success = true,
                        alarms = Runtime.Setting.Alarms,
                    }, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 1 && isPost)
                {
                    await CreateAlarmAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && isPatch)
                {
                    await PatchAlarmAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    await DeleteAlarmAsync(context.Response, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            if (segments.Length >= 1 && segments[0].Equals("visions", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        success = true,
                        activeVisionId = Runtime.Setting.ActiveVisionId ?? "",
                        visions = Runtime.Setting.Visions,
                    }, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 1 && isPost)
                {
                    await CreateVisionAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && isPatch)
                {
                    await PatchVisionAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    await DeleteVisionAsync(context.Response, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            if (segments.Length >= 1 && segments[0].Equals("vars", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        success = true,
                        vars = Runtime.Setting.Vars.Select(kv => new { key = kv.Key, value = kv.Value }),
                    }, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 1 && isPost)
                {
                    await CreateVarAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && isPatch)
                {
                    await PatchVarAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && method.Equals("DELETE", StringComparison.OrdinalIgnoreCase))
                {
                    await DeleteVarAsync(context.Response, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            if (segments.Length >= 1 && segments[0].Equals("machine", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, MachinePayload(), cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 1 && isPatch)
                {
                    await PatchMachineAsync(context, cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            await WriteErrorAsync(context.Response, "invalid_json", cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (InvalidOperationException ex) when (ex.Message == "setting_path_unset")
        {
            await WriteErrorAsync(context.Response, "setting_path_unset", cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private Task WriteSummaryAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        var s = Runtime.Setting;
        return WriteJsonAsync(response, new
        {
            success = true,
            projectName = s.ProjectName,
            settingPath = Runtime.SettingPath,
            databasePath = Runtime.DataStore.DatabasePath,
            counts = new
            {
                drivers = s.Drivers.Count,
                devices = s.Devices.Count,
                axes = s.Axes.Count,
                platforms = s.Platforms.Count,
                tasks = s.Tasks.Count,
                recipes = s.Recipes.Count,
                visions = s.Visions.Count,
                alarms = s.Alarms.Count,
                vars = s.Vars.Count,
            },
            note = "PATCH updates Setting in memory; POST /api/config/save writes disk. Restart runtime to apply to live devices.",
            timestampUtc = DateTime.UtcNow,
        }, cancellationToken);
    }

    private async Task HandleSaveAsync(HttpListenerResponse response, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Runtime.SettingPath))
        {
            await WriteErrorAsync(response, "setting_path_unset", cancellationToken).ConfigureAwait(false);
            return;
        }

        Runtime.SaveSetting();
        await WriteJsonAsync(response, new
        {
            success = true,
            action = "save",
            settingPath = Runtime.SettingPath,
            message = "Saved. Restart runtime for device/driver changes to take effect.",
            timestampUtc = DateTime.UtcNow,
        }, cancellationToken).ConfigureAwait(false);
    }

    private Task WriteCatalogAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var query = context.Request.QueryString;
        var module = (query["module"] ?? "").Trim().ToLowerInvariant();
        var type = query["type"];
        var driverId = query["driverId"];
        if (!string.IsNullOrWhiteSpace(module))
        {
            var template = TemplateFor(module, type, driverId);
            return WriteJsonAsync(context.Response, new { success = true, module, type = type ?? "", parameters = template }, cancellationToken);
        }

        var s = Runtime.Setting;
        var cameraIds = s.Devices
            .Where(d => string.Equals(d.Type, "cameradev", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(d.Type, "extcamera", StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
        return WriteJsonAsync(context.Response, new
        {
            success = true,
            driverIds = s.Drivers.Select(d => d.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            axisIds = s.Axes.Select(a => a.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToList(),
            cameraDeviceIds = cameraIds,
            types = new
            {
                drivers = DriverParameterPresets.KnownTypes,
                devices = new[]
                {
                    "gpio", "vio", "cameradev", "visiondev", "serialdev", "tcpdev", "mysqldev",
                    "extcamera", "devpyscript", "devmodserver", "devmodclient", "tray",
                },
                axes = new[] { "linear", "rotary", "axis" },
                platforms = new[] { "platform", "x", "xy", "xyz", "xyzu", "xyzuv", "xyzuvw" },
                tasks = new[] { "pollDriver", "operation", "machine", "cycle", "cloud-machine", "motion", "flow", "pnpCycle", "pnpConveyor" },
            },
            defaults = new
            {
                drivers = "sim",
                devices = "gpio",
                axes = "linear",
                platforms = "xyz",
                tasks = "pollDriver",
            },
        }, cancellationToken);
    }

    private static Dictionary<string, string> TemplateFor(string module, string? type, string? driverId) =>
        module switch
        {
            "drivers" => DeviceParameterPresets.ForDriver(type),
            "devices" or "axes" or "platforms" => DeviceParameterPresets.ForDevice(type, driverId),
            "tasks" => DeviceParameterPresets.ForTask(type),
            _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

    private async Task CreateDriverAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = string.IsNullOrWhiteSpace(body) ? new DriverPatch() : Deserialize<DriverPatch>(body) ?? new DriverPatch();
        var type = string.IsNullOrWhiteSpace(patch.Type) ? "sim" : patch.Type.Trim();
        var id = UniqueId(Runtime.Setting.Drivers.Select(d => d.Id), "drv-new");
        var item = new MdkSetting.DriverConfig
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(patch.Name) ? id : patch.Name.Trim(),
            Type = type,
            Enabled = patch.Enabled ?? true,
            Parameters = patch.Parameters is { Count: > 0 }
                ? new Dictionary<string, string>(patch.Parameters, StringComparer.OrdinalIgnoreCase)
                : DeviceParameterPresets.ForDriver(type),
        };
        Runtime.Setting.Drivers.Add(item);
        await WriteJsonAsync(context.Response, new { success = true, driver = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteDriverAsync(HttpListenerResponse response, string id, CancellationToken cancellationToken)
    {
        var removed = Runtime.Setting.Drivers.RemoveAll(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            await WriteErrorAsync(response, "driver_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, new { success = true, action = "delete" }, cancellationToken)
            .ConfigureAwait(false);
    }

    private IEnumerable<string> AllDeviceIds() =>
        Runtime.Setting.Devices.Select(d => d.Id)
            .Concat(Runtime.Setting.Axes.Select(d => d.Id))
            .Concat(Runtime.Setting.Platforms.Select(d => d.Id));

    private async Task CreateDeviceAsync(
        HttpListenerContext context,
        List<MdkSetting.DeviceConfig> list,
        string deviceKey,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = string.IsNullOrWhiteSpace(body) ? new DevicePatch() : Deserialize<DevicePatch>(body) ?? new DevicePatch();
        var type = string.IsNullOrWhiteSpace(patch.Type)
            ? deviceKey switch { "axes" => "linear", "platforms" => "xyz", _ => "gpio" }
            : patch.Type.Trim();
        var prefix = deviceKey switch { "axes" => "dev-new", "platforms" => "plat-new", _ => "dev-new" };
        var id = UniqueId(AllDeviceIds(), prefix);
        var driverId = patch.DriverId ?? Runtime.Setting.Drivers.FirstOrDefault()?.Id ?? "";
        var item = new MdkSetting.DeviceConfig
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(patch.Name) ? id : patch.Name.Trim(),
            Type = type,
            DriverId = driverId,
            Enabled = patch.Enabled ?? true,
            Parameters = patch.Parameters is { Count: > 0 }
                ? new Dictionary<string, string>(patch.Parameters, StringComparer.OrdinalIgnoreCase)
                : DeviceParameterPresets.ForDevice(type, driverId),
        };
        list.Add(item);
        await WriteJsonAsync(context.Response, new { success = true, device = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteDeviceAsync(
        HttpListenerResponse response,
        List<MdkSetting.DeviceConfig> list,
        string id,
        CancellationToken cancellationToken)
    {
        var removed = list.RemoveAll(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            await WriteErrorAsync(response, "device_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, new { success = true, action = "delete" }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CreateTaskAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = string.IsNullOrWhiteSpace(body) ? new TaskPatch() : Deserialize<TaskPatch>(body) ?? new TaskPatch();
        var type = string.IsNullOrWhiteSpace(patch.Type) ? "pollDriver" : patch.Type.Trim();
        var name = UniqueId(Runtime.Setting.Tasks.Select(t => t.Name), "task-new");
        var item = new MdkSetting.TaskConfig
        {
            Name = name,
            Type = type,
            DriverId = patch.DriverId ?? Runtime.Setting.Drivers.FirstOrDefault()?.Id ?? "",
            IntervalMs = patch.IntervalMs is > 0 ? patch.IntervalMs.Value : 100,
            Parameters = patch.Parameters is { Count: > 0 }
                ? new Dictionary<string, string>(patch.Parameters, StringComparer.OrdinalIgnoreCase)
                : DeviceParameterPresets.ForTask(type),
        };
        Runtime.Setting.Tasks.Add(item);
        await WriteJsonAsync(context.Response, new { success = true, task = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteTaskAsync(HttpListenerResponse response, string name, CancellationToken cancellationToken)
    {
        var removed = Runtime.Setting.Tasks.RemoveAll(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            await WriteErrorAsync(response, "task_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, new { success = true, action = "delete" }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PatchDriverAsync(HttpListenerContext context, string id, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<DriverPatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = Runtime.Setting.Drivers.FirstOrDefault(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            await WriteErrorAsync(context.Response, "driver_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (patch.Name is not null) item.Name = patch.Name;
        if (patch.Enabled.HasValue) item.Enabled = patch.Enabled.Value;
        if (!string.IsNullOrWhiteSpace(patch.Type)) item.Type = patch.Type.Trim();
        ReplaceParameters(item.Parameters, patch.Parameters);

        await WriteJsonAsync(context.Response, new { success = true, driver = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private bool TryDeviceSection(string segment, out List<MdkSetting.DeviceConfig> list, out string jsonKey)
    {
        if (segment.Equals("devices", StringComparison.OrdinalIgnoreCase))
        {
            list = Runtime.Setting.Devices;
            jsonKey = "devices";
            return true;
        }

        if (segment.Equals("axes", StringComparison.OrdinalIgnoreCase))
        {
            list = Runtime.Setting.Axes;
            jsonKey = "axes";
            return true;
        }

        if (segment.Equals("platforms", StringComparison.OrdinalIgnoreCase))
        {
            list = Runtime.Setting.Platforms;
            jsonKey = "platforms";
            return true;
        }

        list = [];
        jsonKey = "";
        return false;
    }

    private async Task PatchDeviceAsync(
        HttpListenerContext context,
        List<MdkSetting.DeviceConfig> list,
        string id,
        CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<DevicePatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = list.FirstOrDefault(d =>
            string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            await WriteErrorAsync(context.Response, "device_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (patch.Name is not null) item.Name = patch.Name;
        if (patch.Enabled.HasValue) item.Enabled = patch.Enabled.Value;
        if (patch.DriverId is not null) item.DriverId = patch.DriverId;
        if (!string.IsNullOrWhiteSpace(patch.Type)) item.Type = patch.Type.Trim();
        ReplaceParameters(item.Parameters, patch.Parameters);

        await WriteJsonAsync(context.Response, new { success = true, device = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PatchTaskAsync(HttpListenerContext context, string name, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<TaskPatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = Runtime.Setting.Tasks.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            await WriteErrorAsync(context.Response, "task_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (patch.Name is not null) item.Name = patch.Name;
        if (patch.Type is not null) item.Type = patch.Type;
        if (patch.DriverId is not null) item.DriverId = patch.DriverId;
        if (patch.IntervalMs.HasValue) item.IntervalMs = Math.Max(1, patch.IntervalMs.Value);
        ReplaceParameters(item.Parameters, patch.Parameters);

        await WriteJsonAsync(context.Response, new { success = true, task = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task CreateAlarmAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<AlarmPatch>(body) ?? new AlarmPatch();
        var id = string.IsNullOrWhiteSpace(patch.Id)
            ? UniqueId(Runtime.Setting.Alarms.Select(a => a.EffectiveId), "alm-new")
            : patch.Id.Trim();
        if (Runtime.Setting.Alarms.Any(a =>
            string.Equals(a.EffectiveId, id, StringComparison.OrdinalIgnoreCase)))
        {
            await WriteErrorAsync(context.Response, "alarm_exists", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = ApplyAlarmPatch(new MdkSetting.AlarmConfig
        {
            Id = id,
            Key = id,
            Enabled = true,
            Level = "error",
            Op = "eq",
        }, patch);
        Runtime.Setting.Alarms.Add(item);
        await WriteJsonAsync(context.Response, new { success = true, alarm = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PatchAlarmAsync(HttpListenerContext context, string id, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<AlarmPatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = Runtime.Setting.Alarms.FirstOrDefault(a =>
            string.Equals(a.EffectiveId, id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            await WriteErrorAsync(context.Response, "alarm_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        ApplyAlarmPatch(item, patch);
        await WriteJsonAsync(context.Response, new { success = true, alarm = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteAlarmAsync(HttpListenerResponse response, string id, CancellationToken cancellationToken)
    {
        var removed = Runtime.Setting.Alarms.RemoveAll(a =>
            string.Equals(a.EffectiveId, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            await WriteErrorAsync(response, "alarm_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, new { success = true, action = "delete" }, cancellationToken)
            .ConfigureAwait(false);
    }

    private static MdkSetting.AlarmConfig ApplyAlarmPatch(MdkSetting.AlarmConfig item, AlarmPatch patch)
    {
        if (patch.Code is not null) item.Code = patch.Code;
        if (patch.Name is not null) item.Name = patch.Name;
        if (!string.IsNullOrWhiteSpace(patch.Level)) item.Level = patch.Level.Trim();
        if (patch.Enabled.HasValue) item.Enabled = patch.Enabled.Value;
        if (patch.VarKey is not null) item.VarKey = patch.VarKey;
        if (!string.IsNullOrWhiteSpace(patch.Op)) item.Op = patch.Op.Trim();
        if (patch.Value is not null) item.Value = patch.Value;
        if (patch.Message is not null)
        {
            item.Message = patch.Message;
            item.Msg = patch.Message;
        }
        if (patch.Latch.HasValue) item.Latch = patch.Latch.Value;
        if (patch.Solution is not null) item.Solution = patch.Solution;
        if (patch.Module is not null) item.Module = patch.Module;
        if (string.IsNullOrWhiteSpace(item.Key)) item.Key = item.EffectiveId;
        if (string.IsNullOrWhiteSpace(item.Id)) item.Id = item.EffectiveId;
        return item;
    }

    private async Task CreateVisionAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = string.IsNullOrWhiteSpace(body) ? new VisionPatch() : Deserialize<VisionPatch>(body) ?? new VisionPatch();
        var id = string.IsNullOrWhiteSpace(patch.Id)
            ? UniqueId(Runtime.Setting.Visions.Select(v => v.Id), "vision-new")
            : patch.Id.Trim();
        if (Runtime.Setting.Visions.Any(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            await WriteErrorAsync(context.Response, "vision_exists", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = new MdkSetting.VisionConfig
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(patch.Name) ? id : patch.Name.Trim(),
            Description = patch.Description,
            CameraDeviceId = patch.CameraDeviceId ?? "",
            Pipeline = TryReadPipeline(patch.Pipeline) ?? VisionDocument.CreateBasicInspectPipeline(),
        };
        Runtime.Setting.Visions.Add(item);
        if (string.IsNullOrWhiteSpace(Runtime.Setting.ActiveVisionId))
        {
            Runtime.Setting.ActiveVisionId = item.Id;
        }

        await WriteJsonAsync(context.Response, new { success = true, vision = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PatchVisionAsync(HttpListenerContext context, string id, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<VisionPatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = Runtime.Setting.Visions.FirstOrDefault(v =>
            string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            await WriteErrorAsync(context.Response, "vision_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (patch.Name is not null) item.Name = patch.Name;
        if (patch.Description is not null) item.Description = patch.Description;
        if (patch.CameraDeviceId is not null) item.CameraDeviceId = patch.CameraDeviceId;
        if (patch.Pipeline.HasValue)
        {
            var doc = TryReadPipeline(patch.Pipeline);
            if (doc is null)
            {
                await WriteErrorAsync(context.Response, "invalid_pipeline", cancellationToken).ConfigureAwait(false);
                return;
            }

            var errors = doc.Validate();
            if (errors.Count > 0)
            {
                await WriteErrorAsync(context.Response, "invalid_pipeline: " + string.Join("; ", errors), cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            doc.RebuildLinearEdges();
            item.Pipeline = doc;
        }

        await WriteJsonAsync(context.Response, new { success = true, vision = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteVisionAsync(HttpListenerResponse response, string id, CancellationToken cancellationToken)
    {
        var removed = Runtime.Setting.Visions.RemoveAll(v =>
            string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            await WriteErrorAsync(response, "vision_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (string.Equals(Runtime.Setting.ActiveVisionId, id, StringComparison.OrdinalIgnoreCase))
        {
            Runtime.Setting.ActiveVisionId = Runtime.Setting.Visions.FirstOrDefault()?.Id;
        }

        await WriteJsonAsync(response, new { success = true, action = "delete" }, cancellationToken)
            .ConfigureAwait(false);
    }

    private static VisionDocument? TryReadPipeline(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return null;
        }

        try
        {
            var doc = element.Value.Deserialize<VisionDocument>(IoWriteJsonOptions);
            return doc;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string UniqueId(IEnumerable<string> existing, string prefix)
    {
        var set = new HashSet<string>(existing.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(prefix))
        {
            return prefix;
        }

        for (var i = 2; i < 1000; i++)
        {
            var id = $"{prefix}-{i}";
            if (!set.Contains(id))
            {
                return id;
            }
        }

        return prefix + "-" + Guid.NewGuid().ToString("N")[..6];
    }

    private async Task CreateVarAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = string.IsNullOrWhiteSpace(body) ? new VarPatch() : Deserialize<VarPatch>(body) ?? new VarPatch();
        var key = string.IsNullOrWhiteSpace(patch.Key)
            ? UniqueId(Runtime.Setting.Vars.Keys, "var.new")
            : patch.Key.Trim();
        if (Runtime.Setting.Vars.ContainsKey(key))
        {
            await WriteErrorAsync(context.Response, "var_exists", cancellationToken).ConfigureAwait(false);
            return;
        }

        var value = FromJsonElement(patch.Value);
        Runtime.Setting.Vars[key] = value;
        await WriteJsonAsync(context.Response, new { success = true, varItem = new { key, value } }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PatchVarAsync(HttpListenerContext context, string oldKey, CancellationToken cancellationToken)
    {
        oldKey = Uri.UnescapeDataString(oldKey);
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<VarPatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!Runtime.Setting.Vars.ContainsKey(oldKey))
        {
            await WriteErrorAsync(context.Response, "var_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        var newKey = string.IsNullOrWhiteSpace(patch.Key) ? oldKey : patch.Key.Trim();
        if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)
            && Runtime.Setting.Vars.ContainsKey(newKey))
        {
            await WriteErrorAsync(context.Response, "var_exists", cancellationToken).ConfigureAwait(false);
            return;
        }

        var value = patch.Value.HasValue
            ? FromJsonElement(patch.Value)
            : Runtime.Setting.Vars[oldKey];
        if (!string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase))
        {
            Runtime.Setting.Vars.Remove(oldKey);
        }

        Runtime.Setting.Vars[newKey] = value;
        await WriteJsonAsync(context.Response, new { success = true, varItem = new { key = newKey, value } }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task DeleteVarAsync(HttpListenerResponse response, string key, CancellationToken cancellationToken)
    {
        key = Uri.UnescapeDataString(key);
        if (!Runtime.Setting.Vars.Remove(key))
        {
            await WriteErrorAsync(response, "var_not_found", cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteJsonAsync(response, new { success = true, action = "delete" }, cancellationToken)
            .ConfigureAwait(false);
    }

    private object MachinePayload()
    {
        var s = Runtime.Setting;
        return new
        {
            success = true,
            projectName = s.ProjectName,
            machineId = s.MachineId ?? "",
            machineType = s.MachineType ?? "",
            cycleMs = s.CycleMs,
            monitoringPrefix = s.MonitoringPrefix ?? "",
            startPage = s.StartPage ?? "",
            databasePath = s.DatabasePath ?? "",
            activeRecipeId = s.ActiveRecipeId ?? "",
            activeVisionId = s.ActiveVisionId ?? "",
            recipeVarKeys = s.RecipeVarKeys,
            parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["projectName"] = s.ProjectName ?? "",
                ["machineId"] = s.MachineId ?? "",
                ["machineType"] = s.MachineType ?? "",
                ["cycleMs"] = s.CycleMs.ToString(),
                ["monitoringPrefix"] = s.MonitoringPrefix ?? "",
                ["startPage"] = s.StartPage ?? "",
                ["databasePath"] = s.DatabasePath ?? "",
                ["activeRecipeId"] = s.ActiveRecipeId ?? "",
                ["activeVisionId"] = s.ActiveVisionId ?? "",
                ["recipeVarKeys"] = string.Join(",", s.RecipeVarKeys ?? []),
            },
        };
    }

    private async Task PatchMachineAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<MachinePatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        var s = Runtime.Setting;
        var book = patch.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Read(string key, string? fallback) =>
            book.TryGetValue(key, out var v) && v is not null ? v : (fallback ?? "");

        var projectName = FirstNonEmpty(patch.ProjectName, Read("projectName", s.ProjectName));
        s.ProjectName = string.IsNullOrWhiteSpace(projectName) ? "MDKOSS" : projectName.Trim();
        s.MachineId = EmptyToNull(FirstNonEmpty(patch.MachineId, Read("machineId", s.MachineId)));
        s.MachineType = EmptyToNull(FirstNonEmpty(patch.MachineType, Read("machineType", s.MachineType)));

        var cycleRaw = patch.CycleMs?.ToString() ?? Read("cycleMs", s.CycleMs.ToString());
        if (!int.TryParse(cycleRaw, out var cycle) || cycle <= 0)
        {
            await WriteErrorAsync(context.Response, "cycleMs_invalid", cancellationToken).ConfigureAwait(false);
            return;
        }

        s.CycleMs = cycle;
        s.MonitoringPrefix = EmptyToNull(FirstNonEmpty(patch.MonitoringPrefix, Read("monitoringPrefix", s.MonitoringPrefix)));
        s.StartPage = EmptyToNull(FirstNonEmpty(patch.StartPage, Read("startPage", s.StartPage))?.Trim().TrimStart('/'));
        s.DatabasePath = EmptyToNull(FirstNonEmpty(patch.DatabasePath, Read("databasePath", s.DatabasePath)));
        s.ActiveRecipeId = EmptyToNull(FirstNonEmpty(patch.ActiveRecipeId, Read("activeRecipeId", s.ActiveRecipeId)));
        s.ActiveVisionId = EmptyToNull(FirstNonEmpty(patch.ActiveVisionId, Read("activeVisionId", s.ActiveVisionId)));

        var keysRaw = FirstNonEmpty(patch.RecipeVarKeys, Read("recipeVarKeys", string.Join(",", s.RecipeVarKeys ?? []))) ?? "";
        s.RecipeVarKeys = keysRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToList();

        await WriteJsonAsync(context.Response, MachinePayload(), cancellationToken).ConfigureAwait(false);
    }

    private static string? FirstNonEmpty(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : b;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object? FromJsonElement(JsonElement? element)
    {
        if (element is null || element.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return "";
        }

        var e = element.Value;
        return e.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when e.TryGetInt64(out var l) => l,
            JsonValueKind.Number => e.GetDouble(),
            JsonValueKind.String => e.GetString() ?? "",
            _ => e.GetRawText(),
        };
    }

    /// <summary>Replace the parameter book (Config.Wpf Apply* behavior). Null patch leaves existing keys.</summary>
    private static void ReplaceParameters(Dictionary<string, string> target, Dictionary<string, string>? patch)
    {
        if (patch is null) return;
        target.Clear();
        foreach (var (k, v) in patch)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            target[k] = v ?? string.Empty;
        }
    }

    private Task WriteJsonAsync(HttpListenerResponse response, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, SnapshotJsonOptions);
        return WriteResponseAsync(response, "application/json; charset=utf-8", json, cancellationToken);
    }
}
