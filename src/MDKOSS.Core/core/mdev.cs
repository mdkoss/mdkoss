using System.Globalization;
using MDKOSS.Core.Drivers;

namespace MDKOSS.Core;

public enum MDeviceType
{
    Gpio,
    /// <summary>Virtual GPIO: logical IO backed by driver memory (typically sim driver).</summary>
    Vio,
    Axis,
    Platform,


    CameraDev,

    SerialDev,

    TcpDev,

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

/// <summary>Multi-axis platform layout: each logical axis is a separate <see cref="AxisDevice"/> bound to a <see cref="Drivers.IDriver"/>.</summary>
public enum MPlatformKind
{
    /// <summary>Single-axis platform (e.g. transfer Y).</summary>
    X,
    Xy,
    Xyz,
    XyzU,
    XyzUv,
    XyzUvw,
}

/// <summary>Axis letters for each <see cref="MPlatformKind"/> (order is motion order).</summary>
public static class MPlatformKindExtensions
{
    public static IReadOnlyList<string> AxisLetters(this MPlatformKind kind) => kind switch
    {
        MPlatformKind.X => new[] { "X" },
        MPlatformKind.Xy => new[] { "X", "Y" },
        MPlatformKind.Xyz => new[] { "X", "Y", "Z" },
        MPlatformKind.XyzU => new[] { "X", "Y", "Z", "U" },
        MPlatformKind.XyzUv => new[] { "X", "Y", "Z", "U", "V" },
        MPlatformKind.XyzUvw => new[] { "X", "Y", "Z", "U", "V", "W" },
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    public static string ToConfigToken(this MPlatformKind kind) => kind switch
    {
        MPlatformKind.X => "x",
        MPlatformKind.Xy => "xy",
        MPlatformKind.Xyz => "xyz",
        MPlatformKind.XyzU => "xyzu",
        MPlatformKind.XyzUv => "xyzuv",
        MPlatformKind.XyzUvw => "xyzuvw",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };
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

    /// <summary>Driver instance bound to this device (multi-axis platforms use the first axis driver as the nominal primary).</summary>
    public IDriver LinkedDriver => Driver;

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
        return new DeviceSnapshot(Id, Name, Type.ToString(), State.ToString(), Driver.Name, Driver.IsConnected, null, null, null, null);
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
            rows,
            null,
            null,
            null);
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

/// <summary>
/// Virtual GPIO: maps logical aliases to stable driver memory keys on a single <see cref="IDriver"/>
/// (intended for sim and other software-backed IO). Not for multi-controller routing.
/// </summary>
public sealed class VioDevice : MDeviceBase
{
    private readonly string _driverId;
    private readonly Dictionary<string, VioPoint> _points = new(StringComparer.OrdinalIgnoreCase);

    public VioDevice(string id, string name, string driverId, IDriver driver, MVarStore vars)
        : base(id, name, MDeviceType.Vio, driver, vars)
    {
        if (string.IsNullOrWhiteSpace(driverId))
        {
            throw new ArgumentException("VIO driverId cannot be empty.", nameof(driverId));
        }

        _driverId = driverId.Trim();
    }

    /// <summary>Configured runtime driver id (for monitoring).</summary>
    public string DriverId => _driverId;

    public int PointCount => _points.Count;

    public void RegisterVirtualInput(string alias)
    {
        RegisterPoint(alias, isOutput: false);
    }

    public void RegisterVirtualOutput(string alias)
    {
        RegisterPoint(alias, isOutput: true);
    }

    public bool ReadInput(string alias)
    {
        if (!_points.TryGetValue(alias, out var point) || point.IsOutput)
        {
            return false;
        }

        if (!Driver.IsConnected)
        {
            State = MDeviceState.Fault;
            WriteState("fault");
            return false;
        }

        if (!Driver.TryRead(point.Address, out var raw) || raw is null)
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

        if (!Driver.IsConnected)
        {
            State = MDeviceState.Fault;
            WriteState("fault");
            return false;
        }

        var ok = Driver.Write(point.Address, value);
        Vars.Set(BuildVarKey("lastOutputAlias"), alias);
        Vars.Set(BuildVarKey("lastOutputAddress"), point.Address);
        Vars.Set(BuildVarKey("lastOutputDriverId"), _driverId);
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
            var driverOnline = Driver.IsConnected;
            if (driverOnline && Driver.TryRead(point.Address, out var raw))
            {
                value = FormatIoValue(raw);
            }

            rows.Add(new GpioIoPointSnapshot(
                point.Alias,
                point.IsOutput ? "out" : "in",
                _driverId,
                point.Address,
                driverOnline,
                value));
        }

        return new DeviceSnapshot(
            Id,
            Name,
            Type.ToString(),
            State.ToString(),
            "vio",
            Driver.IsConnected,
            rows,
            null,
            null,
            null);
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

    private void RegisterPoint(string alias, bool isOutput)
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            throw new ArgumentException("VIO alias cannot be empty.", nameof(alias));
        }

        var address = BuildVirtualAddress(alias, isOutput);
        _points[alias] = new VioPoint(alias, address, isOutput);
        Vars.Set(BuildVarKey("pointCount"), _points.Count);
    }

    private string BuildVirtualAddress(string alias, bool isOutput)
    {
        var dir = isOutput ? "out" : "in";
        return $"vio.{Id}.{dir}.{alias}";
    }

    private sealed record VioPoint(string Alias, string Address, bool IsOutput);
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

    /// <summary>Writes motion enable to this axis driver (used by <see cref="PlatformDevice"/> for coordinated motion).</summary>
    public bool SetMotionEnabled(bool enabled)
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

    /// <summary>Stops axis motion (clears jog and disables motion enable).</summary>
    public bool StopMotion()
    {
        EnsureConnected();
        Driver.Write(BuildVarKey("jogCommand"), 0.0);
        Vars.Set(BuildVarKey("jogCommand"), 0.0);
        return SetMotionEnabled(false);
    }

    /// <summary>Issues a jog command: signed velocity = direction * velocity.</summary>
    public bool Jog(double direction, double velocity = 1.0)
    {
        EnsureConnected();
        var command = direction * velocity;
        var ok = Driver.Write(BuildVarKey("jogCommand"), command);
        if (ok)
        {
            Vars.Set(BuildVarKey("jogCommand"), command);
        }

        WriteState(State.ToString().ToLowerInvariant());
        return ok;
    }
}

/// <summary>One axis slot on a <see cref="PlatformDevice"/> (letter key, config driver id, runtime axis device).</summary>
public sealed record PlatformAxisRef(string AxisLetter, string DriverId, AxisDevice Axis, short AxisIndex = 0);

/// <summary>
/// Cartesian platform: <see cref="MPlatformKind"/> selects axis count (XY … XYZUVW). Each axis has its own <see cref="AxisDevice"/> and driver.
/// </summary>
public sealed class PlatformDevice : MDeviceBase
{
    private readonly MPlatformKind _kind;
    private readonly IReadOnlyList<PlatformAxisRef> _axes;

    public PlatformDevice(
        string id,
        string name,
        MPlatformKind kind,
        IReadOnlyList<PlatformAxisRef> axes,
        MVarStore vars)
        : base(id, name, MDeviceType.Platform, axes[0].Axis.LinkedDriver, vars)
    {
        if (axes.Count == 0)
        {
            throw new ArgumentException("Platform requires at least one axis.", nameof(axes));
        }

        _kind = kind;
        _axes = axes;
        Vars.Set(BuildVarKey("platformKind"), kind.ToConfigToken());
        Vars.Set(BuildVarKey("axisCount"), axes.Count);
    }

    public MPlatformKind Kind => _kind;

    /// <summary>Axes in motion order (e.g. X, Y, Z, …).</summary>
    public IReadOnlyList<PlatformAxisRef> Axes => _axes;

    public override void Start()
    {
        foreach (var entry in _axes)
        {
            if (!entry.Axis.LinkedDriver.IsConnected)
            {
                State = MDeviceState.Fault;
                WriteState("fault");
                throw new InvalidOperationException(
                    $"Driver '{entry.DriverId}' is not connected for platform '{Id}' axis '{entry.AxisLetter}'.");
            }
        }

        State = MDeviceState.Running;
        WriteState("running");
    }

    public bool SetMotion(bool enabled)
    {
        var ok = true;
        foreach (var entry in _axes)
        {
            ok = entry.Axis.SetMotionEnabled(enabled) && ok;
        }

        if (ok)
        {
            Vars.Set(BuildVarKey("motionEnabled"), enabled);
        }

        WriteState(State.ToString().ToLowerInvariant());
        return ok;
    }

    public override DeviceSnapshot GetSnapshot()
    {
        var rows = new List<PlatformAxisSnapshot>();
        var allConnected = true;
        foreach (var entry in _axes)
        {
            var online = entry.Axis.LinkedDriver.IsConnected;
            allConnected &= online;
            rows.Add(new PlatformAxisSnapshot(entry.AxisLetter, entry.DriverId, online));
        }

        return new DeviceSnapshot(
            Id,
            Name,
            Type.ToString(),
            State.ToString(),
            $"platform-{_kind.ToConfigToken()}",
            allConnected,
            null,
            rows,
            null,
            null);
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

/// <summary>Snapshot data for serial port monitoring.</summary>
public sealed record SerialPortSnapshot(
    string PortName,
    int BaudRate,
    bool IsOpen,
    int BytesToRead,
    int DataBits = 8,
    string Parity = "None",
    string StopBits = "One");

/// <summary>Snapshot data for TCP connection monitoring.</summary>
public sealed record TcpConnectionSnapshot(
    string Host,
    int Port,
    bool IsConnected,
    int BytesToRead);

/// <summary>One platform axis for monitoring (each axis may use a different driver).</summary>
public sealed record PlatformAxisSnapshot(string AxisLetter, string DriverId, bool DriverOnline);

public sealed record DeviceSnapshot(
    string Id,
    string Name,
    string Type,
    string State,
    string DriverType,
    bool DriverConnected,
    IReadOnlyList<GpioIoPointSnapshot>? GpioIoPoints = null,
    IReadOnlyList<PlatformAxisSnapshot>? PlatformAxes = null,
    SerialPortSnapshot? SerialPortInfo = null,
    TcpConnectionSnapshot? TcpConnectionInfo = null);
