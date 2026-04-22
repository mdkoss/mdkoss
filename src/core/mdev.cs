using MDKOSS.Core.Drivers;

namespace MDKOSS.Core;

public enum MDeviceType
{
    Gpio,
    Axis,
    Platform,


    CameraDev,

    SerialDev,

    Generic,
}

public enum MDeviceState
{
    Created,
    Initialized,
    Running,
    Stopped,
    Fault
}

/// <summary>
/// Common base for all runtime devices.
/// </summary>
public abstract class MDeviceBase : IDisposable
{
    protected readonly IDriver Driver;
    protected readonly MVarStore Vars;

    protected MDeviceBase(string id, string name, MDeviceType type, IDriver driver, MVarStore vars)
    {
        Id = id;
        Name = name;
        Type = type;
        Driver = driver;
        Vars = vars;
    }

    public string Id { get; }

    public string Name { get; }

    public MDeviceType Type { get; }

    public MDeviceState State { get; protected set; } = MDeviceState.Created;

    /// <summary>Transitions device to initialized state.</summary>
    public virtual void Initialize()
    {
        State = MDeviceState.Initialized;
        WriteState("initialized");
    }

    /// <summary>Transitions device to running state.</summary>
    public virtual void Start()
    {
        EnsureConnected();
        State = MDeviceState.Running;
        WriteState("running");
    }

    /// <summary>Transitions device to stopped state.</summary>
    public virtual void Stop()
    {
        State = MDeviceState.Stopped;
        WriteState("stopped");
    }

    /// <summary>Releases device resources (override when needed).</summary>
    public virtual void Dispose()
    {
        Stop();
    }

    /// <summary>Returns monitor-friendly device snapshot.</summary>
    public virtual DeviceSnapshot GetSnapshot()
    {
        return new DeviceSnapshot(Id, Name, Type.ToString(), State.ToString(), Driver.Name, Driver.IsConnected);
    }

    /// <summary>Guards operations that require online driver.</summary>
    protected void EnsureConnected()
    {
        if (Driver.IsConnected)
        {
            return;
        }

        State = MDeviceState.Fault;
        WriteState("fault");
        throw new InvalidOperationException($"Driver '{Driver.Name}' is not connected for device '{Id}'.");
    }

    // Uses a stable namespace to avoid key collisions across devices.
    protected string BuildVarKey(string suffix)
    {
        return $"device.{Name}.{Id}.{suffix}";
    }

    // Persists lifecycle state into shared vars for monitoring.
    protected void WriteState(string state)
    {
        Vars.Set(BuildVarKey("state"), state);
        Vars.Set(BuildVarKey("lastUpdateUtc"), DateTime.UtcNow);
    }
}

/// <summary>Basic GPIO device abstraction.</summary>
public sealed class GpioDevice : MDeviceBase
{
    public GpioDevice(string id, string name, IDriver driver, MVarStore vars)
        : base(id, name, MDeviceType.Gpio, driver, vars)
    {
    }

    public bool ReadInput(string address)
    {
        EnsureConnected();
        if (!Driver.TryRead(address, out var raw) || raw is null)
        {
            return false;
        }

        return Convert.ToBoolean(raw);
    }

    public bool WriteOutput(string address, bool value)
    {
        EnsureConnected();
        var ok = Driver.Write(address, value);
        Vars.Set(BuildVarKey("lastOutputAddress"), address);
        Vars.Set(BuildVarKey("lastOutputValue"), value);
        WriteState(State.ToString().ToLowerInvariant());
        return ok;
    }
}

/// <summary>Basic motion axis device abstraction.</summary>
public sealed class AxisDevice : MDeviceBase
{
    public AxisDevice(string id, string name, IDriver driver, MVarStore vars)
        : base(id, name, MDeviceType.Axis, driver, vars)
    {
    }

    public bool MoveTo(double position)
    {
        EnsureConnected();
        var ok = Driver.Write(BuildVarKey("targetPosition"), position);
        if (ok)
        {
            Vars.Set(BuildVarKey("position"), position);
        }

        WriteState(State.ToString().ToLowerInvariant());
        return ok;
    }
}

/// <summary>Basic platform-level device abstraction.</summary>
public sealed class PlatformDevice : MDeviceBase
{
    public PlatformDevice(string id, string name, IDriver driver, MVarStore vars)
        : base(id, name, MDeviceType.Platform, driver, vars)
    {
    }

    public bool SetMotion(bool enabled)
    {
        EnsureConnected();
        var ok = Driver.Write(BuildVarKey("motionEnabled"), enabled);
        if (ok)
        {
            Vars.Set(BuildVarKey("motionEnabled"), enabled);
        }

        WriteState(State.ToString().ToLowerInvariant());
        return ok;
    }
}

/// <summary>Basic camera device abstraction.</summary>
public sealed class CameraDevDevice : MDeviceBase
{
    public CameraDevDevice(string id, string name, IDriver driver, MVarStore vars)
        : base(id, name, MDeviceType.CameraDev, driver, vars)
    {
    }

    public bool TriggerCapture(string recipe)
    {
        EnsureConnected();
        var captureId = Guid.NewGuid().ToString("N");
        var ok = Driver.Write(BuildVarKey("capture.recipe"), recipe)
                 && Driver.Write(BuildVarKey("capture.id"), captureId);

        if (ok)
        {
            Vars.Set(BuildVarKey("lastCaptureRecipe"), recipe);
            Vars.Set(BuildVarKey("lastCaptureId"), captureId);
        }

        WriteState(State.ToString().ToLowerInvariant());
        return ok;
    }
}

public sealed record DeviceSnapshot(
    string Id,
    string Name,
    string Type,
    string State,
    string DriverType,
    bool DriverConnected);
