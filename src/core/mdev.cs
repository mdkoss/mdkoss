using System.Globalization;
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
        return new DeviceSnapshot(Id, Name, Type.ToString(), State.ToString(), Driver.Name, Driver.IsConnected, null);
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

/// <summary>
/// GPIO device: maps logical aliases to physical IO on one or more <see cref="IDriver"/> instances
/// (each driver may expose its own addresses). Optional <c>driverIds</c> in config limits the driver set.
/// </summary>
public sealed class GpioDevice : MDeviceBase
{
    private readonly IReadOnlyDictionary<string, IDriver> _drivers;
    private readonly Dictionary<string, GpioPoint> _points = new(StringComparer.OrdinalIgnoreCase);

    public GpioDevice(string id, string name, IReadOnlyDictionary<string, IDriver> drivers, MVarStore vars)
        : base(id, name, MDeviceType.Gpio, SelectPrimaryDriver(drivers), vars)
    {
        _drivers = drivers;
    }

    /// <summary>Drivers visible to this GPIO instance (full runtime map or a filtered scope).</summary>
    public IReadOnlyDictionary<string, IDriver> Drivers => _drivers;

    public int PointCount => _points.Count;

    /// <summary>
    /// Requires every driver referenced by mapped IO points to be connected (not only a single primary driver).
    /// </summary>
    public override void Start()
    {
        foreach (var driverId in _points.Values.Select(p => p.DriverId).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_drivers.TryGetValue(driverId, out var driver) || !driver.IsConnected)
            {
                State = MDeviceState.Fault;
                WriteState("fault");
                throw new InvalidOperationException(
                    $"Driver '{driverId}' is not connected for GPIO device '{Id}'.");
            }
        }

        State = MDeviceState.Running;
        WriteState("running");
    }

    public void RegisterInput(string alias, string driverId, string address)
    {
        RegisterPoint(alias, driverId, address, isOutput: false);
    }

    public void RegisterOutput(string alias, string driverId, string address)
    {
        RegisterPoint(alias, driverId, address, isOutput: true);
    }

    public bool ReadInput(string alias)
    {
        if (!_points.TryGetValue(alias, out var point) || point.IsOutput)
        {
            return false;
        }

        if (!TryGetPointDriver(point, out var driver))
        {
            return false;
        }

        if (!driver.TryRead(point.Address, out var raw) || raw is null)
        {
            return false;
        }

        return Convert.ToBoolean(raw);
    }

    public bool WriteOutput(string alias, bool value)
    {
        if (!_points.TryGetValue(alias, out var point) || !point.IsOutput)
        {
            return false;
        }

        if (!TryGetPointDriver(point, out var driver))
        {
            return false;
        }

        var ok = driver.Write(point.Address, value);
        Vars.Set(BuildVarKey("lastOutputAlias"), alias);
        Vars.Set(BuildVarKey("lastOutputAddress"), point.Address);
        Vars.Set(BuildVarKey("lastOutputDriverId"), point.DriverId);
        Vars.Set(BuildVarKey("lastOutputValue"), value);
        WriteState(State.ToString().ToLowerInvariant());
        return ok;
    }

    public override DeviceSnapshot GetSnapshot()
    {
        var rows = new List<GpioIoPointSnapshot>();
        foreach (var point in _points.Values.OrderBy(p => p.Alias, StringComparer.OrdinalIgnoreCase))
        {
            string? value = null;
            var driverOnline = false;
            if (_drivers.TryGetValue(point.DriverId, out var dr))
            {
                driverOnline = dr.IsConnected;
                if (driverOnline && dr.TryRead(point.Address, out var raw))
                {
                    value = FormatIoValue(raw);
                }
            }

            rows.Add(new GpioIoPointSnapshot(
                point.Alias,
                point.IsOutput ? "out" : "in",
                point.DriverId,
                point.Address,
                driverOnline,
                value));
        }

        var allConnected = _points.Count == 0
            || _points.Values
                .Select(p => p.DriverId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .All(driverId => _drivers.TryGetValue(driverId, out var d) && d.IsConnected);

        return new DeviceSnapshot(
            Id,
            Name,
            Type.ToString(),
            State.ToString(),
            "multi-driver-gpio",
            allConnected,
            rows);
    }

    private static string? FormatIoValue(object? raw)
    {
        return raw switch
        {
            null => null,
            bool b => b ? "true" : "false",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => raw.ToString(),
        };
    }

    private static IDriver SelectPrimaryDriver(IReadOnlyDictionary<string, IDriver> drivers)
    {
        if (drivers.Count == 0)
        {
            throw new InvalidOperationException("No drivers are available for GpioDevice.");
        }

        return drivers.Values.First();
    }

    private void RegisterPoint(string alias, string driverId, string address, bool isOutput)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("GPIO alias cannot be empty.", nameof(alias));
        }
        if (string.IsNullOrWhiteSpace(driverId))
        {
            throw new ArgumentException("GPIO point driverId cannot be empty.", nameof(driverId));
        }
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ArgumentException("GPIO point address cannot be empty.", nameof(address));
        }
        if (!_drivers.ContainsKey(driverId))
        {
            throw new InvalidOperationException($"GPIO point '{alias}' uses unknown driver '{driverId}'.");
        }

        _points[alias] = new GpioPoint(alias, driverId, address, isOutput);
        Vars.Set(BuildVarKey("pointCount"), _points.Count);
    }

    private bool TryGetPointDriver(GpioPoint point, out IDriver driver)
    {
        if (!_drivers.TryGetValue(point.DriverId, out driver!))
        {
            State = MDeviceState.Fault;
            WriteState("fault");
            return false;
        }

        if (driver.IsConnected)
        {
            return true;
        }

        State = MDeviceState.Fault;
        WriteState("fault");
        return false;
    }

    private sealed record GpioPoint(string Alias, string DriverId, string Address, bool IsOutput);
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

/// <summary>One logical GPIO point for monitoring (may span multiple drivers).</summary>
public sealed record GpioIoPointSnapshot(
    string Alias,
    string Direction,
    string DriverId,
    string Address,
    bool DriverOnline,
    string? Value);

public sealed record DeviceSnapshot(
    string Id,
    string Name,
    string Type,
    string State,
    string DriverType,
    bool DriverConnected,
    IReadOnlyList<GpioIoPointSnapshot>? GpioIoPoints = null);
