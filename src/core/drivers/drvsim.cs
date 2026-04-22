using System.Collections.Concurrent;
using System.Globalization;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// Software simulation driver for controller development and testing.
/// </summary>
public sealed class DrvSim : IDriver
{
    private readonly ConcurrentDictionary<string, object?> _memory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<short, int> _di = new();
    private readonly ConcurrentDictionary<short, int> _do = new();
    private readonly ConcurrentDictionary<short, bool> _axisEnabled = new();
    private readonly ConcurrentDictionary<short, double> _axisPosition = new();

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

    public bool EnableAxis(short axis)
    {
        if (!IsConnected)
        {
            return false;
        }

        _axisEnabled[axis] = true;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool DisableAxis(short axis)
    {
        if (!IsConnected)
        {
            return false;
        }

        _axisEnabled[axis] = false;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool Stop(int axisMask, int option = 0)
    {
        if (!IsConnected)
        {
            return false;
        }

        _memory["motion.lastStopMask"] = axisMask;
        _memory["motion.lastStopOption"] = option;
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

        position = _axisPosition.GetOrAdd(axis, 0);
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        if (!_axisEnabled.GetOrAdd(axis, false))
        {
            _memory["driver.lastCode"] = -1;
            _memory[$"axis.{axis}.error"] = "Axis is not enabled.";
            return false;
        }

        _axisPosition[axis] = targetPosition;
        _memory[$"axis.{axis}.targetPosition"] = targetPosition;
        _memory[$"axis.{axis}.velocity"] = velocity;
        _memory[$"axis.{axis}.acceleration"] = acceleration;
        _memory[$"axis.{axis}.deceleration"] = deceleration;
        _memory["driver.lastCode"] = 0;
        return true;
    }

    public void Dispose()
    {
        IsConnected = false;
        _memory.Clear();
        _di.Clear();
        _do.Clear();
        _axisEnabled.Clear();
        _axisPosition.Clear();
    }

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

    private static bool TryParseTypeAndIndex(string address, string prefix, out short value)
    {
        value = 0;
        if (!address.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var suffix = address[prefix.Length..];
        return short.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
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
}
