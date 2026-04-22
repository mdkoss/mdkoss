using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MDKOSS.Core.Drivers;

/// <summary>
/// GTS motion driver implementation backed by gts.dll.
/// </summary>
public sealed class DrvGts : IDriver
{
    private readonly ConcurrentDictionary<string, object?> _memory = new();
    private short _cardNo = 1;
    private short _channel;

    public string Name => "GTS";

    public bool IsConnected { get; private set; }

    public void Initialize(MDKOSS.Core.MdkSetting.DriverConfig config)
    {
        _cardNo = GetShort(config, "cardNo", 1);
        _channel = GetShort(config, "channel", 0);
        var openParam = GetShort(config, "openParam", 0);
        var resetOnInit = GetBool(config, "resetOnInit", false);

        _memory["driver.id"] = config.Id;
        _memory["driver.type"] = config.Type;
        _memory["driver.cardNo"] = _cardNo;
        _memory["driver.channel"] = _channel;

        var rc = NativeGts.GT_Open(_cardNo, _channel, openParam);
        IsConnected = rc == 0;
        _memory["driver.lastCode"] = rc;

        if (!IsConnected)
        {
            return;
        }

        if (resetOnInit)
        {
            _ = NativeGts.GT_Reset(_cardNo);
        }
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

        var handledByNative = TryWriteNativeAddress(address, value);
        if (handledByNative)
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

        var rc = NativeGts.GT_GetDi(_cardNo, diType, out var nativeValue);
        _memory["driver.lastCode"] = rc;
        value = nativeValue;
        return rc == 0;
    }

    public bool TryReadDo(short doType, out int value)
    {
        value = 0;
        if (!IsConnected)
        {
            return false;
        }

        var rc = NativeGts.GT_GetDo(_cardNo, doType, out var nativeValue);
        _memory["driver.lastCode"] = rc;
        value = nativeValue;
        return rc == 0;
    }

    public bool WriteDo(short doType, int value)
    {
        return IsConnected && Call(() => NativeGts.GT_SetDo(_cardNo, doType, value));
    }

    public bool WriteDoBit(short doType, short doIndex, bool value)
    {
        var bit = value ? (short)1 : (short)0;
        return IsConnected && Call(() => NativeGts.GT_SetDoBit(_cardNo, doType, doIndex, bit));
    }

    public bool EnableAxis(short axis)
    {
        return IsConnected && Call(() => NativeGts.GT_AxisOn(_cardNo, axis));
    }

    public bool DisableAxis(short axis)
    {
        return IsConnected && Call(() => NativeGts.GT_AxisOff(_cardNo, axis));
    }

    public bool Stop(int axisMask, int option = 0)
    {
        return IsConnected && Call(() => NativeGts.GT_Stop(_cardNo, axisMask, option));
    }

    public bool TryGetAxisPrfPosition(short axis, out double position)
    {
        position = 0;
        if (!IsConnected)
        {
            return false;
        }

        var rc = NativeGts.GT_GetPrfPos(_cardNo, axis, out var nativePos, 1, out _);
        _memory["driver.lastCode"] = rc;
        position = nativePos;
        return rc == 0;
    }

    public bool MoveAxisTrap(short axis, int targetPosition, double velocity, double acceleration, double deceleration)
    {
        if (!IsConnected)
        {
            return false;
        }

        var trap = new NativeGts.TTrapPrm
        {
            acc = acceleration,
            dec = deceleration,
            velStart = 0,
            smoothTime = 0
        };

        if (!Call(() => NativeGts.GT_PrfTrap(_cardNo, axis))
            || !Call(() => NativeGts.GT_SetTrapPrm(_cardNo, axis, ref trap))
            || !Call(() => NativeGts.GT_SetPos(_cardNo, axis, targetPosition))
            || !Call(() => NativeGts.GT_SetVel(_cardNo, axis, velocity)))
        {
            return false;
        }

        var mask = 1 << (axis - 1);
        return Call(() => NativeGts.GT_Update(_cardNo, mask));
    }

    public void Dispose()
    {
        if (IsConnected)
        {
            _ = NativeGts.GT_Close(_cardNo);
        }

        IsConnected = false;
        _memory.Clear();
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
            if (TryGetAxisPrfPosition(axis, out var pos))
            {
                value = pos;
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
            return WriteDoBit(doBitType, doBitIndex, Convert.ToBoolean(value ?? false, CultureInfo.InvariantCulture));
        }

        if (TryParseTypeAndIndex(address, "axis.", out var axis))
        {
            if (!TryConvertToInt(value, out var target))
            {
                return false;
            }

            const double defaultVel = 1000;
            const double defaultAcc = 10000;
            const double defaultDec = 10000;
            return MoveAxisTrap(axis, target, defaultVel, defaultAcc, defaultDec);
        }

        return false;
    }

    private bool Call(Func<short> invoke)
    {
        var rc = invoke();
        _memory["driver.lastCode"] = rc;
        return rc == 0;
    }

    private static short GetShort(MDKOSS.Core.MdkSetting.DriverConfig config, string key, short defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        return short.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
    }

    private static bool GetBool(MDKOSS.Core.MdkSetting.DriverConfig config, string key, bool defaultValue)
    {
        if (!config.Parameters.TryGetValue(key, out var raw))
        {
            return defaultValue;
        }

        return bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        result = 0;
        if (value is null)
        {
            return false;
        }

        if (value is bool b)
        {
            result = b ? 1 : 0;
            return true;
        }

        if (value is IConvertible c)
        {
            try
            {
                result = c.ToInt32(CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
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

    private static class NativeGts
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct TTrapPrm
        {
            public double acc;
            public double dec;
            public double velStart;
            public short smoothTime;
        }

        [DllImport("gts.dll")]
        internal static extern short GT_Open(short cardNum, short channel, short param);

        [DllImport("gts.dll")]
        internal static extern short GT_Close(short cardNum);

        [DllImport("gts.dll")]
        internal static extern short GT_Reset(short cardNum);

        [DllImport("gts.dll")]
        internal static extern short GT_GetDi(short cardNum, short diType, out int pValue);

        [DllImport("gts.dll")]
        internal static extern short GT_GetDo(short cardNum, short doType, out int pValue);

        [DllImport("gts.dll")]
        internal static extern short GT_SetDo(short cardNum, short doType, int value);

        [DllImport("gts.dll")]
        internal static extern short GT_SetDoBit(short cardNum, short doType, short doIndex, short value);

        [DllImport("gts.dll")]
        internal static extern short GT_AxisOn(short cardNum, short axis);

        [DllImport("gts.dll")]
        internal static extern short GT_AxisOff(short cardNum, short axis);

        [DllImport("gts.dll")]
        internal static extern short GT_Stop(short cardNum, int mask, int option);

        [DllImport("gts.dll")]
        internal static extern short GT_GetPrfPos(short cardNum, short profile, out double pValue, short count, out uint pClock);

        [DllImport("gts.dll")]
        internal static extern short GT_PrfTrap(short cardNum, short profile);

        [DllImport("gts.dll")]
        internal static extern short GT_SetTrapPrm(short cardNum, short profile, ref TTrapPrm pPrm);

        [DllImport("gts.dll")]
        internal static extern short GT_SetPos(short cardNum, short profile, int pos);

        [DllImport("gts.dll")]
        internal static extern short GT_SetVel(short cardNum, short profile, double vel);

        [DllImport("gts.dll")]
        internal static extern short GT_Update(short cardNum, int mask);
    }
}
