using System.Collections.Concurrent;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Core;

/// <summary>
/// Base task for motion-related multi-thread control scenarios.
/// Provides unified GPIO/Axis/Platform operation APIs plus parameter/MVar helpers.
/// </summary>
public abstract class MotionTask : MTaskBase
{
    private readonly ConcurrentDictionary<string, object?> _params = new(StringComparer.OrdinalIgnoreCase);

    protected MotionTask(string name, int intervalMs, IDriver driver, MVarStore vars)
        : base(name, intervalMs)
    {
        Driver = driver;
        Vars = vars;
    }

    protected IDriver Driver { get; }

    protected MVarStore Vars { get; }

    // -----------------------------
    // GPIO APIs
    // -----------------------------
    protected bool GpioTryReadDi(short diType, out int value)
    {
        EnsureDriverConnected();
        return Driver.TryReadDi(diType, out value);
    }

    protected bool GpioTryReadDo(short doType, out int value)
    {
        EnsureDriverConnected();
        return Driver.TryReadDo(doType, out value);
    }

    protected bool GpioWriteDo(short doType, int value)
    {
        EnsureDriverConnected();
        return Driver.WriteDo(doType, value);
    }

    protected bool GpioWriteDoBit(short doType, short doIndex, bool value)
    {
        EnsureDriverConnected();
        return Driver.WriteDoBit(doType, doIndex, value);
    }

    // -----------------------------
    // Axis APIs
    // -----------------------------
    protected bool AxisEnable(short axis)
    {
        EnsureDriverConnected();
        return Driver.EnableAxis(axis);
    }

    protected bool AxisDisable(short axis)
    {
        EnsureDriverConnected();
        return Driver.DisableAxis(axis);
    }

    protected bool AxisStop(int axisMask, int option = 0)
    {
        EnsureDriverConnected();
        return Driver.Stop(axisMask, option);
    }

    protected bool AxisTryGetPosition(short axis, out double position)
    {
        EnsureDriverConnected();
        return Driver.TryGetAxisPrfPosition(axis, out position);
    }

    protected bool AxisMoveTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
    {
        EnsureDriverConnected();
        return Driver.MoveAxisTrap(axis, targetPosition, velocity, acceleration, deceleration);
    }

    // -----------------------------
    // Platform APIs
    // -----------------------------
    protected bool PlatformSetMotionEnabled(bool enabled, string address = "platform.motionEnabled")
    {
        EnsureDriverConnected();
        var ok = Driver.Write(address, enabled);
        if (ok)
        {
            SetVar("platform.motionEnabled", enabled);
            SetVar("platform.lastMotionUpdateUtc", DateTime.UtcNow);
        }

        return ok;
    }

    protected bool PlatformStartMotion(string address = "platform.motionEnabled") => PlatformSetMotionEnabled(true, address);

    protected bool PlatformStopMotion(string address = "platform.motionEnabled") => PlatformSetMotionEnabled(false, address);

    protected bool PlatformWrite(string address, object? value)
    {
        EnsureDriverConnected();
        return Driver.Write(address, value);
    }

    protected bool PlatformTryRead(string address, out object? value)
    {
        EnsureDriverConnected();
        return Driver.TryRead(address, out value);
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
