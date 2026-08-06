using System.Net;
using System.Text.Json;

namespace MDKOSS.Core.Monitor;

/// <summary>
/// Handles /api/config — light-weight setting view/edit + persist.
/// Edits update <see cref="MdkSetting"/> only; restart runtime to apply to live devices/drivers.
/// </summary>
public sealed class ConfigApiModule : MonitoringApiModule
{
    private sealed class DriverPatch
    {
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

                if (segments.Length == 2 && isPatch)
                {
                    await PatchDriverAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
                    return true;
                }
            }

            if (segments.Length >= 1 && segments[0].Equals("devices", StringComparison.OrdinalIgnoreCase))
            {
                if (segments.Length == 1 && isGet)
                {
                    await WriteJsonAsync(context.Response, new
                    {
                        success = true,
                        devices = Runtime.Setting.Devices,
                    }, cancellationToken).ConfigureAwait(false);
                    return true;
                }

                if (segments.Length == 2 && isPatch)
                {
                    await PatchDeviceAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
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

                if (segments.Length == 2 && isPatch)
                {
                    await PatchTaskAsync(context, segments[1], cancellationToken).ConfigureAwait(false);
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
                tasks = s.Tasks.Count,
                recipes = s.Recipes.Count,
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

        if (patch.Enabled.HasValue) item.Enabled = patch.Enabled.Value;
        if (!string.IsNullOrWhiteSpace(patch.Type)) item.Type = patch.Type.Trim();
        MergeParameters(item.Parameters, patch.Parameters);

        await WriteJsonAsync(context.Response, new { success = true, driver = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task PatchDeviceAsync(HttpListenerContext context, string id, CancellationToken cancellationToken)
    {
        var body = await ReadBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
        var patch = Deserialize<DevicePatch>(body);
        if (patch is null)
        {
            await WriteErrorAsync(context.Response, "invalid_body", cancellationToken).ConfigureAwait(false);
            return;
        }

        var item = Runtime.Setting.Devices.FirstOrDefault(d =>
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
        MergeParameters(item.Parameters, patch.Parameters);

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
        MergeParameters(item.Parameters, patch.Parameters);

        await WriteJsonAsync(context.Response, new { success = true, task = item }, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void MergeParameters(Dictionary<string, string> target, Dictionary<string, string>? patch)
    {
        if (patch is null) return;
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
