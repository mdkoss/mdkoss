using System.Collections.Concurrent;
using System.Globalization;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// Software simulation driver for controller development and testing.
/// Simulates all motion-control operations in memory without hardware.
/// </summary>
public sealed class DrvSim : IDriver
{
    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<short, int> _di = new();
    private readonly ConcurrentDictionary<short, int> _do = new();
    private readonly ConcurrentDictionary<short, SimAxisState> _axes = new();

    public string Name => "SIM";

    public bool IsConnected { get; private set; }

    public void Initialize(MdkSetting.DriverConfig config)
    {
        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.mode"] = "simulation";
        _memory["driver.lastCode"] = 0;
        IsConnected = true;
    }

    public bool TryRead(string address, out object? value)
    {
        value = null;
        if (!IsConnected || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (TryReadNativeAddress(address, out value))
        {
            return true;
        }

        return _memory.TryGetValue(address, out value);
    }

    public bool Write(string address, object? value)
    {
        if (!IsConnected || string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        if (TryWriteNativeAddress(address, value))
        {
            _memory[address] = value;
            return true;
        }

        _memory[address] = value;
        return true;
    }

    // ──────────────────────────────────────────────
    //  IO Control
    // ──────────────────────────────────────────────

    public bool TryReadDi(short diType, out int value)
    {
        value = 0;
        if (!IsConnected)
        {
            return false;
        }

        value = _di.GetOrAdd(diType, 0);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        if (!IsConnected)
        {
            return false;
        }

        value = _do.GetOrAdd(doType, 0);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool WriteDo(short doType, int value)
    {
        if (!IsConnected)
        {
            return false;
        }

        _do[doType] = value;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        if (!IsConnected || doIndex < 0 || doIndex > 31)
        {
            return false;
        }

        var current = _do.GetOrAdd(doType, 0);
        var bitMask = 1 << doIndex;
        var next = value ? (current | bitMask) : (current & ~bitMask);
        _do[doType] = next;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  Axis Servo Control
    // ──────────────────────────────────────────────

    public bool EnableAxis(short axis)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new SimAxisState());
        state.Enabled = true;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool DisableAxis(short axis)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new SimAxisState());
        state.Enabled = false;
        state.Moving = false;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool IsAxisEnabled(short axis)
    {
        return _axes.GetOrAdd(axis, _ => new SimAxisState()).Enabled;
    }

    // ──────────────────────────────────────────────
    //  Axis Status
    // ──────────────────────────────────────────────

    public bool TryGetAxisStatus(short axis, out int status)
    {
        status = 0;
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new SimAxisState());
        // Bit 0: servo enabled, Bit 1: moving, Bit 2: in-position, Bit 3: alarm
        if (state.Enabled) status |= 0x01;
        if (state.Moving) status |= 0x02;
        if (!state.Moving) status |= 0x04;
        if (state.Alarm) status |= 0x08;
        if (state.Homed) status |= 0x10;

        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        position = _axes.GetOrAdd(axis, _ => new SimAxisState()).PrfPosition;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisEncPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        position = _axes.GetOrAdd(axis, _ => new SimAxisState()).EncPosition;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool TryGetAxisVelocity(short axis, out double velocity)
    {
        velocity = 0;
        if (!IsConnected)
        {
            return false;
        }

        velocity = _axes.GetOrAdd(axis, _ => new SimAxisState()).Velocity;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  Position Setting
    // ──────────────────────────────────────────────

    public bool SetAxisPosition(short axis, double position)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new SimAxisState());
        state.PrfPosition = position;
        state.EncPosition = position;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  Motion Parameters
    // ──────────────────────────────────────────────

    public bool SetAxisVelocity(short axis, double velocity)
    {
        if (!IsConnected)
        {
            return false;
        }

        _axes.GetOrAdd(axis, _ => new SimAxisState()).Velocity = velocity;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool SetAxisAcceleration(short axis, double acceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        _axes.GetOrAdd(axis, _ => new SimAxisState()).Acceleration = acceleration;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool SetAxisDeceleration(short axis, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        _axes.GetOrAdd(axis, _ => new SimAxisState()).Deceleration = deceleration;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  Motion Control
    // ──────────────────────────────────────────────

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new SimAxisState());
        if (!state.Enabled)
        {
            _memory["driver.lastCode"] = -1;
            _memory[$"axis.{axis}.error"] = "Axis is not enabled.";
            return false;
        }

        state.PrfPosition = targetPosition;
        state.EncPosition = targetPosition;
        state.Velocity = velocity;
        state.Acceleration = acceleration;
        state.Deceleration = deceleration;
        state.Moving = false; // Instant in simulation
        _memory[$"axis.{axis}.targetPosition"] = targetPosition;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool MoveAxisJog(short axis, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new SimAxisState());
        if (!state.Enabled)
        {
            _memory["driver.lastCode"] = -1;
            _memory[$"axis.{axis}.error"] = "Axis is not enabled.";
            return false;
        }

        state.Moving = true;
        state.Velocity = velocity;
        state.Acceleration = acceleration;
        state.Deceleration = deceleration;
        _memory[$"axis.{axis}.jogVelocity"] = velocity;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool MoveAxisHome(short axis, short homeMode, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var state = _axes.GetOrAdd(axis, _ => new SimAxisState());
        if (!state.Enabled)
        {
            _memory["driver.lastCode"] = -1;
            _memory[$"axis.{axis}.error"] = "Axis is not enabled.";
            return false;
        }

        // Simulate instant home: zero position and mark homed
        state.PrfPosition = 0;
        state.EncPosition = 0;
        state.Homed = true;
        state.Moving = false;
        state.Velocity = velocity;
        state.Acceleration = acceleration;
        state.Deceleration = deceleration;
        _memory[$"axis.{axis}.homeMode"] = homeMode;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool Stop(int axisMask, int option = 0)
    {
        if (!IsConnected)
        {
            return false;
        }

        // Clear moving flag for all axes in the mask
        foreach (var kvp in _axes)
        {
            var bit = 1 << (kvp.Key - 1);
            if ((axisMask & bit) != 0)
            {
                kvp.Value.Moving = false;
                kvp.Value.Velocity = 0;
            }
        }

        _memory["motion.lastStopMask"] = axisMask;
        _memory["motion.lastStopOption"] = option;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    // ──────────────────────────────────────────────
    //  IDisposable
    // ──────────────────────────────────────────────

    public void Dispose()
    {
        IsConnected = false;
        _memory.Clear();
        _di.Clear();
        _do.Clear();
        _axes.Clear();
    }

    // ──────────────────────────────────────────────
    //  Address-based read/write helpers
    // ──────────────────────────────────────────────

    private bool TryReadNativeAddress(string address, out object? value)
    {
        value = null;

        if (TryParseTypeAndIndex(address, "di.", out var diType))
        {
            if (TryReadDi(diType, out var diValue))
            {
                value = diValue;
                return true;
            }

            return false;
        }

        if (TryParseTypeAndIndex(address, "do.", out var doType))
        {
            if (TryReadDo(doType, out var doValue))
            {
                value = doValue;
                return true;
            }

            return false;
        }

        if (TryParseTypeAndIndex(address, "axis.", out var axis))
        {
            var suffix = ExtractSuffixAfterIndex(address, "axis.");
            if (suffix is not null)
            {
                if (suffix.Equals("enc", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetAxisEncPosition(axis, out var encPos))
                    {
                        value = encPos;
                        return true;
                    }
                    return false;
                }

                if (suffix.Equals("status", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetAxisStatus(axis, out var status))
                    {
                        value = status;
                        return true;
                    }
                    return false;
                }

                if (suffix.Equals("vel", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetAxisVelocity(axis, out var vel))
                    {
                        value = vel;
                        return true;
                    }
                    return false;
                }

                if (suffix.Equals("enabled", StringComparison.OrdinalIgnoreCase))
                {
                    value = IsAxisEnabled(axis);
                    return true;
                }
            }

            // Default: return profile position
            if (TryGetAxisPrfPosition(axis, out var axisPos))
            {
                value = axisPos;
                return true;
            }

            return false;
        }

        return false;
    }

    private bool TryWriteNativeAddress(string address, object? value)
    {
        if (TryParseTypeAndIndex(address, "do.", out var doType))
        {
            if (!TryConvertToInt(value, out var doValue))
            {
                return false;
            }

            return WriteDo(doType, doValue);
        }

        if (TryParseDoBitAddress(address, out var doBitType, out var doBitIndex))
        {
            var doBitValue = Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture);
            return WriteDoBit(doBitType, doBitIndex, doBitValue);
        }

        if (TryParseTypeAndIndex(address, "axis.", out var axis))
        {
            if (!TryConvertToInt(value, out var target))
            {
                return false;
            }

            return MoveAxisTrap(axis, target, 1000, 10000, 10000);
        }

        return false;
    }

    // ──────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────

    private static string? ExtractSuffixAfterIndex(string address, string prefix)
    {
        var rest = address[prefix.Length..];
        var dotIdx = rest.IndexOf('.');
        if (dotIdx < 0)
        {
            return null;
        }

        var indexPart = rest[..dotIdx];
        if (!short.TryParse(indexPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        return rest[(dotIdx + 1)..];
    }

    private static bool TryParseTypeAndIndex(string address, string prefix, out short value)
    {
        value = 0;
        if (!address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = address[prefix.Length..];
        var dotIdx = suffix.IndexOf('.');
        var numPart = dotIdx >= 0 ? suffix[..dotIdx] : suffix;
        return short.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseDoBitAddress(string address, out short doType, out short doIndex)
    {
        doType = 0;
        doIndex = 0;
        if (!address.StartsWith("do.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = address.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 4 || !parts[2].Equals("bit", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return short.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out doType)
            && short.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out doIndex);
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        result = 0;
        if (value is null)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            result = boolValue ? 1 : 0;
            return true;
        }

        if (value is IConvertible convertible)
        {
            try
            {
                result = convertible.ToInt32(CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Simulated per-axis state stored in the concurrent dictionary.
    /// </summary>
    private sealed class SimAxisState
    {
        public bool Enabled;
        public bool Moving;
        public bool Homed;
        public bool Alarm;
        public double PrfPosition;
        public double EncPosition;
        public double Velocity;
        public double Acceleration;
        public double Deceleration;
    }
}
