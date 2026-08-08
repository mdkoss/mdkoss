using System.Collections.Concurrent;
using MDKOSS.Core;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Tasks;

/// <summary>
/// Base task for motion-related multi-thread control scenarios.
/// Motion goes through <see cref="AxisDevice"/> / <see cref="PlatformDevice"/>;
/// IO goes through <see cref="GpioDevice"/>.
/// </summary>
public abstract class MotionTask : MTaskBase
{
    private readonly ConcurrentDictionary<string, object?> _params = new(StringComparer.OrdinalIgnoreCase);

    protected MotionTask(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices)
        : base(name, intervalMs)
    {
        Driver = driver;
        Vars = vars;
        Devices = devices;
    }

    protected IDriver Driver { get; }

    protected MVarStore Vars { get; }

    protected IReadOnlyDictionary<string, MDeviceBase> Devices { get; }

    // -----------------------------
    // Device lookup
    // -----------------------------
    protected bool TryGetDevice(string deviceId, out MDeviceBase device)
    {
        return Devices.TryGetValue(deviceId, out device!);
    }

    protected MDeviceBase GetDevice(string deviceId)
    {
        if (!TryGetDevice(deviceId, out var device))
        {
            throw new InvalidOperationException($"Device '{deviceId}' was not found for task '{Name}'.");
        }

        return device;
    }

    protected bool TryGetDevice<T>(string deviceId, out T? device) where T : MDeviceBase
    {
        device = default;
        if (!TryGetDevice(deviceId, out var raw) || raw is not T typed)
        {
            return false;
        }

        device = typed;
        return true;
    }

    protected bool TryGetAxisDevice(string deviceId, out AxisDevice? device) =>
        TryGetDevice(deviceId, out device);

    protected bool TryGetPlatformDevice(string deviceId, out PlatformDevice? device) =>
        TryGetDevice(deviceId, out device);

    protected bool TryGetGpioDevice(string? deviceId, out GpioDevice? device)
    {
        if (!string.IsNullOrWhiteSpace(deviceId) && TryGetDevice(deviceId, out device))
        {
            return true;
        }

        // Single shared GpioDevice: tasks may omit deviceId and use aliases only.
        device = Devices.Values.OfType<GpioDevice>().FirstOrDefault();
        return device is not null;
    }

    protected bool TryGetVioDevice(string deviceId, out VioDevice? device) =>
        TryGetDevice(deviceId, out device);

    // -----------------------------
    // Axis motion (AxisDevice)
    // -----------------------------
    protected bool AxisMoveTo(string axisDeviceId, double position)
    {
        if (!TryGetAxisDevice(axisDeviceId, out var axis) || axis is null)
        {
            return false;
        }

        return axis.MoveTo(position);
    }

    protected bool AxisSetMotionEnabled(string axisDeviceId, bool enabled)
    {
        if (!TryGetAxisDevice(axisDeviceId, out var axis) || axis is null)
        {
            return false;
        }

        return axis.SetMotionEnabled(enabled);
    }

    // -----------------------------
    // Platform motion (PlatformDevice)
    // -----------------------------
    protected bool PlatformSetMotion(string platformDeviceId, bool enabled)
    {
        if (!TryGetPlatformDevice(platformDeviceId, out var platform) || platform is null)
        {
            return false;
        }

        return platform.SetMotion(enabled);
    }

    protected bool PlatformStartMotion(string platformDeviceId) =>
        PlatformSetMotion(platformDeviceId, true);

    protected bool PlatformStopMotion(string platformDeviceId) =>
        PlatformSetMotion(platformDeviceId, false);

    protected bool PlatformAxisMoveTo(string platformDeviceId, string axisLetter, double position)
    {
        if (!TryGetPlatformDevice(platformDeviceId, out var platform) || platform is null)
        {
            return false;
        }

        var entry = platform.Axes.FirstOrDefault(a =>
            string.Equals(a.AxisLetter, axisLetter, StringComparison.OrdinalIgnoreCase));
        return entry?.Axis.MoveTo(position) ?? false;
    }

    // -----------------------------
    // GPIO IO (GpioDevice) — prefer one shared device; aliases carry driver routing.
    // -----------------------------
    protected bool GpioWriteOutput(string alias, bool value) =>
        GpioWriteOutput(null, alias, value);

    protected bool GpioWriteOutput(string? gpioDeviceId, string alias, bool value)
    {
        if (!TryGetGpioDevice(gpioDeviceId, out var gpio) || gpio is null)
        {
            return false;
        }

        return gpio.WriteOutput(alias, value);
    }

    protected bool GpioReadInput(string alias) =>
        GpioReadInput(null, alias);

    protected bool GpioReadInput(string? gpioDeviceId, string alias)
    {
        if (!TryGetGpioDevice(gpioDeviceId, out var gpio) || gpio is null)
        {
            return false;
        }

        return gpio.ReadInput(alias);
    }

    protected bool GpioTryReadInput(string alias, out bool value) =>
        GpioTryReadInput(null, alias, out value);

    protected bool GpioTryReadInput(string? gpioDeviceId, string alias, out bool value)
    {
        value = false;
        if (!TryGetGpioDevice(gpioDeviceId, out var gpio) || gpio is null)
        {
            return false;
        }

        value = gpio.ReadInput(alias);
        return true;
    }

    // -----------------------------
    // Device snapshot
    // -----------------------------
    protected bool DeviceTryGetSnapshot(string deviceId, out DeviceSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryGetDevice(deviceId, out var device))
        {
            return false;
        }

        snapshot = device.GetSnapshot();
        return true;
    }

    // -----------------------------
    // Task parameter APIs (thread-safe)
    // -----------------------------
    protected void SetParam<T>(string key, T value)
    {
        _params[key] = value;
    }

    protected T? GetParam<T>(string key)
    {
        if (!TryGetParam<T>(key, out var value))
        {
            return default;
        }

        return value;
    }

    protected bool TryGetParam<T>(string key, out T? value)
    {
        value = default;
        if (!_params.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is T typed)
        {
            value = typed;
            return true;
        }

        value = (T?)Convert.ChangeType(raw, typeof(T));
        return true;
    }

    // -----------------------------
    // MVar APIs (task-scoped by default)
    // -----------------------------
    protected void SetVar<T>(string keySuffix, T value)
    {
        Vars.Set(BuildTaskVarKey(keySuffix), value);
    }

    protected T? GetVar<T>(string keySuffix)
    {
        return Vars.Get<T>(BuildTaskVarKey(keySuffix));
    }

    protected bool TryGetVar<T>(string keySuffix, out T? value)
    {
        return Vars.TryGet(BuildTaskVarKey(keySuffix), out value);
    }

    protected void SetGlobalVar<T>(string key, T value)
    {
        Vars.Set(key, value);
    }

    protected T? GetGlobalVar<T>(string key)
    {
        return Vars.Get<T>(key);
    }

    protected bool TryGetGlobalVar<T>(string key, out T? value)
    {
        return Vars.TryGet(key, out value);
    }

    protected string BuildTaskVarKey(string suffix)
    {
        return $"task.{Name}.{suffix}";
    }

    protected void EnsureDriverConnected()
    {
        if (Driver.IsConnected)
        {
            return;
        }

        State = MTaskState.Fault;
        SetVar("state", "fault");
        SetVar("lastFaultUtc", DateTime.UtcNow);
        throw new InvalidOperationException($"Driver '{Driver.Name}' is not connected for task '{Name}'.");
    }
}

/// <summary>
/// Default motion task: loads config parameters and reports driver heartbeat.
/// Inherit <see cref="MotionTask"/> for custom motion logic.
/// </summary>
public sealed class TaskMotionTask : MotionTask
{
    public TaskMotionTask(
        string name,
        int intervalMs,
        IDriver driver,
        MVarStore vars,
        IReadOnlyDictionary<string, MDeviceBase> devices,
        IReadOnlyDictionary<string, string>? parameters = null)
        : base(name, intervalMs, driver, vars, devices)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var kv in parameters)
        {
            SetParam(kv.Key, kv.Value);
        }
    }

    protected override Task TickAsync(CancellationToken cancellationToken)
    {
        SetVar("alive", Driver.IsConnected);
        SetVar("lastTickUtc", DateTime.UtcNow);
        return Task.CompletedTask;
    }
}
