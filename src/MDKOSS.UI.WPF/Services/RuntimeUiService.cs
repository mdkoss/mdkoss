using System.Text.Json;
using System.Windows.Threading;
using MDKOSS.Core;
using MDKOSS.Core.Data;

namespace MDKOSS.UI.WPF.Services;

public sealed class RuntimeUiService : IRuntimeUiService, IDisposable
{
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public RuntimeUiService(MdkRuntime runtime)
    {
        Runtime = runtime;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Refresh();
    }

    public MdkRuntime Runtime { get; }

    public event EventHandler? SnapshotChanged;

    public RuntimeSnapshot? LatestSnapshot { get; private set; }

    public string? SelectedOrderId { get; set; }

    public IReadOnlyList<ProductionOrderRecord> ListOrders()
    {
        try
        {
            return Runtime.DataStore.ListOrders();
        }
        catch
        {
            return [];
        }
    }

    public IReadOnlyList<TaskSnapshot> ListTasks() => Runtime.GetTaskSnapshots();

    public IReadOnlyList<MdkSetting.AlarmConfig> ListActiveAlarms() => Runtime.AlarmManager.GetActive();

    public RecipeSnapshot GetRecipeSnapshot() => Runtime.GetRecipeSnapshot();

    public void SendMachineCommand(string command)
    {
        if (command is "start" or "stop" or "reset" or "pause")
        {
            Runtime.Vars.Set("machine.command", command);
        }

        if (command is not "pause")
        {
            Runtime.Vars.Set("task.operation.command", command);
        }

        Refresh();
    }

    public bool TryApplyRecipe(string recipeId, out string? error) =>
        Runtime.TryApplyRecipe(recipeId, out error);

    public bool TryTriggerDemoAlarm(out string? error)
    {
        if (Runtime.AlarmManager.Trigger("alm-demo", out error))
        {
            Refresh();
            return true;
        }

        var ok = Runtime.AlarmManager.Trigger(
            "alm-demo",
            out error,
            allowAdHoc: true,
            msgOverride: "演示报警");
        if (ok)
        {
            Refresh();
        }

        return ok;
    }

    public void ClearAllAlarms()
    {
        Runtime.AlarmManager.ClearAll();
        Refresh();
    }

    public int AckAllAlarms()
    {
        var n = Runtime.AlarmManager.GetActive().Count;
        Runtime.AlarmManager.ClearAll();
        Refresh();
        return n;
    }

    public bool TryClearAlarm(string id, out string? error)
    {
        var ok = Runtime.AlarmManager.Clear(id, out error);
        if (ok)
        {
            Refresh();
        }

        return ok;
    }

    public bool TryWriteIo(string deviceId, string alias, bool value, out string? error)
    {
        var ok = Runtime.TryWriteDigitalOutput(deviceId, alias, value, out error);
        if (ok)
        {
            Refresh();
        }

        return ok;
    }

    public bool TryAxisJog(string axisId, double direction, double velocity, out string? error)
    {
        error = null;
        if (!Runtime.TryGetDevice(axisId, out var raw) || raw is not AxisDevice axis)
        {
            error = "axis_not_found";
            return false;
        }

        if (!axis.Jog(direction, velocity))
        {
            error = "axis_jog_failed";
            return false;
        }

        Refresh();
        return true;
    }

    public bool TryAxisMove(string axisId, double position, out string? error)
    {
        error = null;
        if (!Runtime.TryGetDevice(axisId, out var raw) || raw is not AxisDevice axis)
        {
            error = "axis_not_found";
            return false;
        }

        if (!axis.MoveTo(position))
        {
            error = "axis_move_failed";
            return false;
        }

        Refresh();
        return true;
    }

    public bool TryAxisEnable(string axisId, bool enabled, out string? error)
    {
        error = null;
        if (!Runtime.TryGetDevice(axisId, out var raw) || raw is not AxisDevice axis)
        {
            error = "axis_not_found";
            return false;
        }

        if (!axis.SetMotionEnabled(enabled))
        {
            error = "axis_enable_failed";
            return false;
        }

        Refresh();
        return true;
    }

    public bool TryAxisStop(string axisId, out string? error)
    {
        error = null;
        if (!Runtime.TryGetDevice(axisId, out var raw) || raw is not AxisDevice axis)
        {
            error = "axis_not_found";
            return false;
        }

        if (!axis.StopMotion())
        {
            error = "axis_stop_failed";
            return false;
        }

        Refresh();
        return true;
    }

    public bool TryPlatformEnable(string platformId, bool enabled, out string? error)
    {
        error = null;
        if (!Runtime.TryGetDevice(platformId, out var raw) || raw is not PlatformDevice platform)
        {
            error = "platform_not_found";
            return false;
        }

        if (!platform.SetMotion(enabled))
        {
            error = "platform_set_motion_failed";
            return false;
        }

        Refresh();
        return true;
    }

    public bool TryPlatformAxisJog(string platformId, string letter, double direction, double velocity, out string? error)
    {
        error = null;
        if (!TryGetPlatformAxis(platformId, letter, out var axis, out error))
        {
            return false;
        }

        if (!axis.Jog(direction, velocity))
        {
            error = "platform_axis_jog_failed";
            return false;
        }

        Refresh();
        return true;
    }

    public bool TryPlatformAxisMove(string platformId, string letter, double position, out string? error)
    {
        error = null;
        if (!TryGetPlatformAxis(platformId, letter, out var axis, out error))
        {
            return false;
        }

        if (!axis.MoveTo(position))
        {
            error = "platform_axis_move_failed";
            return false;
        }

        Refresh();
        return true;
    }

    public DeviceActionResult ExecuteAction(string deviceId, string action, Dictionary<string, object?>? parameters = null)
    {
        Dictionary<string, JsonElement>? json = null;
        if (parameters is { Count: > 0 })
        {
            json = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in parameters)
            {
                json[kv.Key] = JsonSerializer.SerializeToElement(kv.Value);
            }
        }

        var result = Runtime.ExecuteDeviceAction(deviceId, action, json);
        Refresh();
        return result;
    }

    public bool TryReadDriver(string driverId, string address, out object? value, out string? error) =>
        Runtime.TryReadDriverAddress(driverId, address, out value, out error);

    public bool TryWriteDriver(string driverId, string address, object? value, out string? error)
    {
        var ok = Runtime.TryWriteDriverAddress(driverId, address, value, out error);
        if (ok)
        {
            Refresh();
        }

        return ok;
    }

    public bool TrySaveSetting(out string? error)
    {
        error = null;
        var path = Runtime.SettingPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "setting_path_missing";
            return false;
        }

        try
        {
            Runtime.Setting.Save(path);
            Runtime.DataStore.PersistRecipesFromSetting(Runtime.Setting);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public void Refresh()
    {
        try
        {
            LatestSnapshot = Runtime.GetSnapshot();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"WPF UI snapshot failed: {ex.Message}");
        }

        SnapshotChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
    }

    private bool TryGetPlatformAxis(string platformId, string letter, out AxisDevice axis, out string? error)
    {
        axis = null!;
        error = null;
        if (!Runtime.TryGetDevice(platformId, out var raw) || raw is not PlatformDevice platform)
        {
            error = "platform_not_found";
            return false;
        }

        var entry = platform.Axes.FirstOrDefault(a =>
            string.Equals(a.AxisLetter, letter, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            error = "platform_axis_not_found";
            return false;
        }

        axis = entry.Axis;
        return true;
    }
}
